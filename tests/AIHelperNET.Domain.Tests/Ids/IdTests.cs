using AIHelperNET.Domain.Ids;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Domain.Tests.Ids;

public class IdTests
{
    [Fact]
    public void SessionId_New_IsUnique()
    {
        var a = SessionId.New();
        var b = SessionId.New();
        a.Should().NotBe(b);
    }

    [Fact]
    public void SessionId_EqualityByValue()
    {
        var g = Guid.CreateVersion7();
        new SessionId(g).Should().Be(new SessionId(g));
    }

    [Fact]
    public void QuestionId_NotAssignableToSessionId()
    {
        // Compile-time guarantee — this test just ensures the types exist and are distinct record structs.
        typeof(QuestionId).Should().NotBeSameAs(typeof(SessionId));
    }

    [Fact]
    public void ConversationTurnId_New_IsUnique()
    {
        var a = ConversationTurnId.New();
        var b = ConversationTurnId.New();
        a.Should().NotBe(b);
    }

    [Fact]
    public void AnswerVersionId_New_IsUnique()
    {
        var a = AnswerVersionId.New();
        var b = AnswerVersionId.New();
        a.Should().NotBe(b);
    }
}
