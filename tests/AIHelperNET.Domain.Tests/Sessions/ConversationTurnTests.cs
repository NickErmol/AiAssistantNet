using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Domain.Tests.Sessions;

public class ConversationTurnTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static readonly SessionId SId = new(Guid.NewGuid());
    private static readonly QuestionId QId = QuestionId.New();

    [Fact]
    public void Create_SetsDetectedStatus()
    {
        var turn = ConversationTurn.Create(SId, QId, "What is DI?", Now);
        turn.Status.Should().Be(ConversationTurnStatus.Detected);
        turn.InitialQuestionText.Should().Be("What is DI?");
        turn.AnswerVersions.Should().BeEmpty();
    }

    [Fact]
    public void TransitionTo_ValidTransition_Succeeds()
    {
        var turn = ConversationTurn.Create(SId, QId, "What is DI?", Now);
        var result = turn.TransitionTo(ConversationTurnStatus.GeneratingPreliminary);
        result.IsSuccess.Should().BeTrue();
        turn.Status.Should().Be(ConversationTurnStatus.GeneratingPreliminary);
    }

    [Fact]
    public void TransitionTo_FromTerminalState_Fails()
    {
        var turn = ConversationTurn.Create(SId, QId, "Q?", Now);
        turn.Dismiss();
        var result = turn.TransitionTo(ConversationTurnStatus.GeneratingPreliminary);
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void AddAnswerVersion_AppendsPreliminary()
    {
        var turn = ConversationTurn.Create(SId, QId, "Q?", Now);
        var version = AnswerVersion.Create(AnswerVersionType.Preliminary, "Answer text", Now);
        turn.AddAnswerVersion(version);
        turn.AnswerVersions.Should().HaveCount(1);
        turn.AnswerVersions[0].VersionType.Should().Be(AnswerVersionType.Preliminary);
    }

    [Fact]
    public void AttachClarificationQuestion_AddsToList()
    {
        var turn = ConversationTurn.Create(SId, QId, "Q?", Now);
        var itemId = new TranscriptItemId(Guid.NewGuid());
        turn.AttachClarificationQuestion(itemId);
        turn.ClarificationQuestionIds.Should().Contain(itemId);
    }

    [Fact]
    public void AttachClarificationResponse_AddsToList()
    {
        var turn = ConversationTurn.Create(SId, QId, "Q?", Now);
        var itemId = new TranscriptItemId(Guid.NewGuid());
        turn.AttachClarificationResponse(itemId);
        turn.ClarificationResponseIds.Should().Contain(itemId);
    }

    [Fact]
    public void Dismiss_SetsStatus()
    {
        var turn = ConversationTurn.Create(SId, QId, "Q?", Now);
        turn.Dismiss();
        turn.Status.Should().Be(ConversationTurnStatus.Dismissed);
    }

    [Fact]
    public void Resolve_SetsStatus()
    {
        var turn = ConversationTurn.Create(SId, QId, "Q?", Now);
        turn.Resolve();
        turn.Status.Should().Be(ConversationTurnStatus.Resolved);
    }

    [Fact]
    public void AppendToQuestion_AppendsTextWithSpace()
    {
        var turn = ConversationTurn.Create(SId, QId, "Can we use them", Now);
        turn.AppendToQuestion("and what is the difference?");
        turn.InitialQuestionText.Should().Be("Can we use them and what is the difference?");
    }

    [Fact]
    public void AppendToQuestion_UpdatesUpdatedAt()
    {
        var turn = ConversationTurn.Create(SId, QId, "Initial question text here?", Now);
        var before = turn.UpdatedAt;
        turn.AppendToQuestion("extra");
        turn.UpdatedAt.Should().BeAfter(before);
    }
}
