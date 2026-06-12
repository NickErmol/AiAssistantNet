using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>
/// Drives a scripted interview through the real <see cref="TranscriptPipelineService"/>, feeding
/// ordered segments and deterministically awaiting answer completion between generating steps.
/// </summary>
public sealed class InterviewDriver(
    TranscriptPipelineService pipeline,
    IUnitOfWork unitOfWork,
    CapturingAnswerStreamSink sink,
    FakeQuestionBoundaryClassifier classifier,
    IServiceProvider services)
{
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PersistenceTimeout = TimeSpan.FromSeconds(5);
    private readonly Dictionary<ConversationTurnId, int> _preliminaryCount = new();
    private DateTimeOffset _clock = DateTimeOffset.UnixEpoch;

    private DateTimeOffset NextTimestamp() => _clock = _clock.AddSeconds(1);

    /// <summary>
    /// Feeds an interviewer (<see cref="Speaker.Other"/>) segment with a scripted boundary result,
    /// then awaits the resulting (re)generation to complete and the answer version to be persisted
    /// to the database.
    /// </summary>
    /// <remarks>
    /// After the sink signals completion, this method polls the database until the turn's
    /// <c>AnswerVersions</c> count reaches the expected target. The handler calls
    /// <c>SaveChangesAsync</c> after <c>OnCompleteAsync</c>, so a brief wait is necessary.
    /// </remarks>
    public async Task OtherAsync(Session session, string text, BoundaryClassificationResult scripted)
    {
        classifier.Enqueue(scripted);
        await pipeline.ProcessAsync(
            session, TranscriptItem.Create(Speaker.Other, text, NextTimestamp(), 0.95f),
            unitOfWork, CancellationToken.None);

        var turnId = session.ConversationTurns[^1].Id;
        var target = _preliminaryCount.GetValueOrDefault(turnId) + 1;
        _preliminaryCount[turnId] = target;

        // Wait for the sink to signal streaming completion.
        await sink.WaitForCompletionCountAsync(
            turnId, AnswerVersionType.Preliminary, target, AnswerTimeout);

        // The handler calls SaveChangesAsync *after* OnCompleteAsync. Poll the DB until the
        // AnswerVersion row actually appears, so the caller can assert immediately after this
        // method returns without any race condition.
        await WaitForAnswerVersionInDbAsync(session.Id, turnId, target, PersistenceTimeout);
    }

    /// <summary>
    /// Feeds a candidate (<see cref="Speaker.Me"/>) segment. Per the conversation model this never
    /// generates, so there is nothing to await.
    /// </summary>
    public async Task MeAsync(Session session, string text)
        => await pipeline.ProcessAsync(
            session, TranscriptItem.Create(Speaker.Me, text, NextTimestamp(), 0.95f),
            unitOfWork, CancellationToken.None);

    /// <summary>
    /// Polls the database until the specified turn has at least <paramref name="minVersionCount"/>
    /// answer versions, or throws <see cref="TimeoutException"/> if the deadline is reached.
    /// </summary>
    private async Task WaitForAnswerVersionInDbAsync(
        SessionId sessionId, ConversationTurnId turnId, int minVersionCount, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            await using var scope = services.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
            var result = await repo.GetAsync(sessionId, CancellationToken.None);
            if (result.IsSuccess)
            {
                var t = result.Value.ConversationTurns.FirstOrDefault(t => t.Id == turnId);
                if (t is not null && t.AnswerVersions.Count >= minVersionCount)
                    return;
            }

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    $"Turn {turnId} did not reach {minVersionCount} persisted AnswerVersion(s) in {timeout}.");

            await Task.Delay(50);
        }
    }
}
