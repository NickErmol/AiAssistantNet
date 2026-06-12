using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Integration.Tests.E2E;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Integration.Tests.E2E;

public class CapturingAnswerStreamSinkTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task WaitForCompletion_CompletesAfterOnComplete_AndCapturesText()
    {
        var sink = new CapturingAnswerStreamSink();
        var turn = ConversationTurnId.New();

        await sink.OnChunkAsync(turn, AnswerVersionType.Preliminary, "Hello ", default);
        await sink.OnChunkAsync(turn, AnswerVersionType.Preliminary, "world", default);
        await sink.OnCompleteAsync(turn, AnswerVersionType.Preliminary, default);

        await sink.WaitForCompletionCountAsync(turn, AnswerVersionType.Preliminary, 1, Timeout);
        sink.Text(turn, AnswerVersionType.Preliminary).Should().Be("Hello world");
    }

    [Fact]
    public async Task WaitForCompletion_SecondGeneration_AwaitsSecondCompletion()
    {
        var sink = new CapturingAnswerStreamSink();
        var turn = ConversationTurnId.New();

        await sink.OnCompleteAsync(turn, AnswerVersionType.Preliminary, default);
        await sink.WaitForCompletionCountAsync(turn, AnswerVersionType.Preliminary, 1, Timeout);

        var pending = sink.WaitForCompletionCountAsync(turn, AnswerVersionType.Preliminary, 2, Timeout);
        pending.IsCompleted.Should().BeFalse();

        await sink.OnCompleteAsync(turn, AnswerVersionType.Preliminary, default);
        await pending;
    }

    [Fact]
    public async Task WaitForCompletion_TimesOut_WhenNeverCompleted()
    {
        var sink = new CapturingAnswerStreamSink();
        var turn = ConversationTurnId.New();

        var act = async () => await sink.WaitForCompletionCountAsync(
            turn, AnswerVersionType.Preliminary, 1, TimeSpan.FromMilliseconds(200));

        await act.Should().ThrowAsync<TimeoutException>();
    }
}
