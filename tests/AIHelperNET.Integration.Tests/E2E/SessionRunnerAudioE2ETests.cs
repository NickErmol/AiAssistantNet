using AIHelperNET.App.Services;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>
/// Tier B E2E: drives the real <see cref="SessionRunner"/> orchestration loop (frame fan-out →
/// transcription → same-speaker merge → speaker-change flush → pipeline) over scripted audio,
/// reusing the Tier A <see cref="InterviewHost"/> harness. The DI-registered NAudio/Whisper services
/// are unused; scripted fakes are passed to the runner directly.
/// </summary>
/// <remarks>
/// Synchronization avoids the shared-cache SQLite read/write contention: the test never polls the
/// database while the loop runs. It awaits the loop's natural completion (<see
/// cref="SessionRunner.WaitForCompletionAsync"/>) plus, where an answer is expected, the in-memory
/// sink, and only reads the database once after <see cref="SessionRunner.StopAsync"/>.
/// </remarks>
public class SessionRunnerAudioE2ETests : IAsyncLifetime
{
    private InterviewHost _host = null!;

    public async Task InitializeAsync() => _host = await InterviewHost.CreateAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    private const int MergeWindowMs = 120;
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(15);

    private static readonly AudioDeviceSelection Devices = new("mic", "loopback");

    private static BoundaryClassificationResult NewQuestion(string text) =>
        new(BoundaryLabel.NewQuestion, 0.95, ShouldGenerateAnswer: true,
            ShouldRefineExistingAnswer: false, ShouldCreateNewTurn: true,
            NormalizedQuestionText: text, Reason: "scripted");

    private async Task<Session> PersistNewSessionAsync()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, DateTimeOffset.UnixEpoch).Value;
        await using var scope = _host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await repo.AddAsync(session, default);
        await uow.SaveChangesAsync(default);
        return session;
    }

    private SessionRunner NewRunner(IReadOnlyList<ScriptedUtterance> script) =>
        new(_host.Services.GetRequiredService<IServiceScopeFactory>(),
            new ScriptedAudioCaptureService(script),
            new ScriptedTranscriptionService(script),
            _host.Services.GetRequiredService<TranscriptPipelineService>(),
            segmentMergeWindowMs: MergeWindowMs);

    /// <summary>Reads the session once, retrying briefly on a transient SQLite lock from a trailing
    /// background write under the shared-cache in-memory database.</summary>
    private async Task<Session> ReloadAsync(SessionId id)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var scope = _host.Services.CreateAsyncScope();
                var repo = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
                return (await repo.GetAsync(id, default)).Value;
            }
            catch (Microsoft.Data.Sqlite.SqliteException) when (attempt < 5)
            {
                await Task.Delay(50);
            }
        }
    }

    /// <summary>Polls the database until <paramref name="predicate"/> holds. Persistence happens on
    /// the runner's session scope after generation, so this must be called <em>before</em>
    /// <see cref="SessionRunner.StopAsync"/> (which disposes that scope) for answer assertions.</summary>
    private async Task PollUntilAsync(SessionId id, Func<Session, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (predicate(await ReloadAsync(id))) return;
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Session {id} did not reach the expected DB state in {timeout}.");
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task SingleOtherUtterance_ProducesOneTurn()
    {
        var session = await PersistNewSessionAsync();
        var script = new[] { new ScriptedUtterance(Speaker.Other, "What is dependency injection?", 0) };
        _host.Classifier.Enqueue(NewQuestion("What is dependency injection?"));

        var runner = NewRunner(script);
        await runner.StartAsync(session.Id, Devices, WhisperModelSize.Base, "en", AudioSourceMode.Both);

        await runner.WaitForCompletionAsync();                                   // loop fully drained
        await PollUntilAsync(session.Id, s => s.ConversationTurns.Count >= 1, AnswerTimeout);
        await runner.StopAsync();

        var reloaded = await ReloadAsync(session.Id);
        reloaded.ConversationTurns.Should().ContainSingle(); // capture -> transcription -> pipeline
        _host.Sink.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task TwoOtherFragments_WithinMergeWindow_ProduceOneTurn()
    {
        var session = await PersistNewSessionAsync();
        var script = new[]
        {
            new ScriptedUtterance(Speaker.Other, "What is dependency", 0),
            new ScriptedUtterance(Speaker.Other, "injection?", 0), // same speaker, no gap -> merged
        };
        // One detection is expected after the merge. If the merge regressed into two transcript
        // items, the fully drained loop would create two turns and the single-turn assertion fails.
        _host.Classifier.Enqueue(NewQuestion("What is dependency injection?"));

        var runner = NewRunner(script);
        await runner.StartAsync(session.Id, Devices, WhisperModelSize.Base, "en", AudioSourceMode.Both);

        await runner.WaitForCompletionAsync();
        await PollUntilAsync(session.Id, s => s.ConversationTurns.Count >= 1, AnswerTimeout);
        await runner.StopAsync();

        var reloaded = await ReloadAsync(session.Id);
        reloaded.ConversationTurns.Should().ContainSingle();
        _host.Sink.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task TwoOtherUtterances_BeyondMergeWindow_ProduceTwoTurns()
    {
        var session = await PersistNewSessionAsync();
        // Two interviewer utterances separated by more than the merge window are NOT merged: the
        // consumer flushes the first on the window timeout, then processes the second separately —
        // the complement of the same-speaker merge test. Both are Speaker.Other, so they share the
        // single loopback transcription channel and arrive in a deterministic order (no cross-channel
        // race).
        // Texts mirror Tier A's two-Other scenario: the "Now …" prefix makes the heuristic treat the
        // second utterance as a new topic, so two turns are created. Asserts the turn structure
        // (count), not both answers completing — concurrent multi-turn answer persistence is the known
        // flaky area, so we only require both turn structures to be persisted.
        var script = new[]
        {
            new ScriptedUtterance(Speaker.Other, "What is dependency injection in one sentence?", 0),
            new ScriptedUtterance(Speaker.Other, "Now explain CQRS in one sentence?", MergeWindowMs * 3),
        };
        _host.Classifier.Enqueue(NewQuestion("What is dependency injection?"));
        _host.Classifier.Enqueue(NewQuestion("Now explain CQRS?"));

        var runner = NewRunner(script);
        await runner.StartAsync(session.Id, Devices, WhisperModelSize.Base, "en", AudioSourceMode.Both);

        await runner.WaitForCompletionAsync();
        await PollUntilAsync(session.Id, s => s.ConversationTurns.Count >= 2, AnswerTimeout);
        await runner.StopAsync();

        var reloaded = await ReloadAsync(session.Id);
        reloaded.ConversationTurns.Should().HaveCount(2);
        _host.Sink.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task MicrophoneOnly_DropsOtherLoopbackUtterance_ProducesNoTurn()
    {
        var session = await PersistNewSessionAsync();
        var script = new[] { new ScriptedUtterance(Speaker.Other, "What is DI?", 0) };
        // No classifier result enqueued: the Other frame is gated out and never reaches the pipeline.

        var runner = NewRunner(script);
        await runner.StartAsync(session.Id, Devices, WhisperModelSize.Base, "en", AudioSourceMode.MicrophoneOnly);

        await runner.WaitForCompletionAsync(); // nothing to transcribe; loop drains immediately
        await runner.StopAsync();

        var reloaded = await ReloadAsync(session.Id);
        reloaded.ConversationTurns.Should().BeEmpty();
        _host.Sink.Errors.Should().BeEmpty();
    }
}
