using AIHelperNET.Domain.Sessions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Domain.Tests.Sessions;

public class EntityTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void TranscriptItem_Create_TrimsAndStores()
    {
        var item = TranscriptItem.Create(Speaker.Other, "  Hello  ", Now, 0.9f);
        item.Text.Should().Be("Hello");
        item.Speaker.Should().Be(Speaker.Other);
        item.Confidence.Should().Be(0.9f);
    }

    [Fact]
    public void TranscriptItem_Create_EmptyText_Throws()
    {
        var act = () => TranscriptItem.Create(Speaker.Me, "   ", Now, 0.5f);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DetectedQuestion_Create_TrimsAndStores()
    {
        var q = DetectedQuestion.Create("  What is CQRS?  ", QuestionSource.Audio, Now);
        q.Text.Should().Be("What is CQRS?");
        q.Source.Should().Be(QuestionSource.Audio);
    }

    [Fact]
    public void GeneratedAnswer_AppendChunk_BuildsContent()
    {
        var a = GeneratedAnswer.Create(new Domain.Ids.QuestionId(Guid.NewGuid()), Now);
        a.AppendChunk("Hello ");
        a.AppendChunk("world");
        a.Content.Should().Be("Hello world");
        a.Status.Should().Be(AnswerStatus.Streaming);
    }

    [Fact]
    public void GeneratedAnswer_Complete_SetsStatus()
    {
        var a = GeneratedAnswer.Create(new Domain.Ids.QuestionId(Guid.NewGuid()), Now);
        a.Complete(Now.AddSeconds(5));
        a.Status.Should().Be(AnswerStatus.Completed);
        a.CompletedAt.Should().Be(Now.AddSeconds(5));
    }

    [Fact]
    public void GeneratedAnswer_Cancel_SetsStatus()
    {
        var a = GeneratedAnswer.Create(new Domain.Ids.QuestionId(Guid.NewGuid()), Now);
        a.Cancel(Now.AddSeconds(1));
        a.Status.Should().Be(AnswerStatus.Cancelled);
    }
}
