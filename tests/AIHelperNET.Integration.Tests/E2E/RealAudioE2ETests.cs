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
/// Tier C E2E: drives the real SessionRunner + real Whisper transcription over committed WAV
/// fixtures (Me=mic, Other=system), asserting per-speaker transcript and answer-card outcomes.
/// Tagged RealAudio so the default fast run can exclude it (--filter "Category!=RealAudio").
/// Uses the deterministic FakeAnswerProvider; only routing/structure is asserted.
/// </summary>
/// <remarks>
/// Model selection: Whisper Small is used for E2E fixture tests. Base/Small/Medium/LargeTurbo
/// all load and produce correct transcription for other_di.wav. Small is a good balance of
/// speed and accuracy for this suite; switch to Medium or LargeTurbo if higher accuracy is
/// needed (at the cost of slower first-run load time).
/// </remarks>
[Trait("Category", "RealAudio")]
public class RealAudioE2ETests : IAsyncLifetime
{
    private InterviewHost _host = null!;

    public async Task InitializeAsync() => _host = await InterviewHost.CreateAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    // Real Whisper Small (CPU): generous merge window (segments arrive after model latency) + long timeouts.
    // Calibrated: MergeWindowMs=2500 handles real-model segment latency (~3.3s audio + model processing);
    // AnswerTimeout=60s covers model load + transcription + answer generation (FakeAnswerProvider is fast).
    // WaitForCompletionAsync completes in ~4-5s on this machine; PollUntilAsync rarely needs > 5s after that.
    private const int MergeWindowMs = 2500;
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(60);

    private static readonly AudioDeviceSelection Devices = new("mic", "loopback");

    // Whisper Small: good accuracy for fixture-based tests; all four models (Base/Small/Medium/LargeTurbo) load.
    private const WhisperModelSize Model = WhisperModelSize.Small;

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

    private SessionRunner NewRunner(IReadOnlyList<WavUtterance> script) =>
        new(_host.Services.GetRequiredService<IServiceScopeFactory>(),
            new WavFileAudioCaptureService(script),
            _host.Services.GetRequiredService<ITranscriptionService>(), // REAL Whisper
            _host.Services.GetRequiredService<TranscriptPipelineService>(),
            segmentMergeWindowMs: MergeWindowMs);

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

    private async Task PollUntilAsync(SessionId id, Func<Session, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (predicate(await ReloadAsync(id))) return;
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Session {id} did not reach the expected DB state in {timeout}.");
            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task Scenario1_SingleOtherQuestion_ProducesTranscriptAndOneCard()
    {
        var session = await PersistNewSessionAsync();
        _host.Classifier.Enqueue(NewQuestion("What is dependency injection?"));

        var runner = NewRunner(new[]
        {
            new WavUtterance(Speaker.Other, "other_di.wav", GapMsBefore: 0),
        });
        await runner.StartAsync(session.Id, Devices, Model, "en", AudioSourceMode.Both);

        await runner.WaitForCompletionAsync();
        await PollUntilAsync(session.Id, s => s.ConversationTurns.Count >= 1, AnswerTimeout);
        await runner.StopAsync();

        _host.Transcripts.TextFor(Speaker.Other).Should()
            .ContainSingle().Which.ToLowerInvariant().Should().Contain("dependency");

        var reloaded = await ReloadAsync(session.Id);
        reloaded.ConversationTurns.Should().ContainSingle();
        _host.Sink.Errors.Should().BeEmpty();
    }
}
