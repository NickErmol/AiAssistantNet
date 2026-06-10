using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.Ids;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

public class ScreenTaskContextStoreTests
{
    private static ConversationTurnId NewId() => ConversationTurnId.New();

    [Fact]
    public void Register_NewGroup_SetsContextWithDerivedTopicAndEmptyAdditions()
    {
        var store = new ScreenTaskContextStore();
        var card = NewId();

        store.Register(card, "Implement LRU cache", ScreenAnalysisMode.SolveCodingTask, isNewGroup: true);

        store.Current.Should().NotBeNull();
        store.Current!.ScreenCardId.Should().Be(card);
        store.Current.LatestCardId.Should().Be(card);
        store.Current.TopicLabel.Should().Be("Implement LRU cache");
        store.Current.Additions.Should().BeEmpty();
    }

    [Fact]
    public void Register_SameGroup_UpdatesOcrButKeepsAdditions()
    {
        var store = new ScreenTaskContextStore();
        var card = NewId();
        store.Register(card, "task v1", ScreenAnalysisMode.SolveCodingTask, isNewGroup: true);
        store.AddAddition("make it thread-safe");

        store.Register(card, "task v1 + v2", ScreenAnalysisMode.SolveCodingTask, isNewGroup: false);

        store.Current!.Ocr.Should().Be("task v1 + v2");
        store.Current.Additions.Should().ContainSingle().Which.Should().Be("make it thread-safe");
    }

    [Fact]
    public void AddAddition_AccumulatesInOrder_AndCapsAtEight()
    {
        var store = new ScreenTaskContextStore();
        store.Register(NewId(), "task", ScreenAnalysisMode.SolveCodingTask, isNewGroup: true);

        for (var i = 1; i <= 10; i++) store.AddAddition($"cond{i}");

        store.Current!.Additions.Should().HaveCount(8);
        store.Current.Additions[0].Should().Be("cond3");   // oldest two dropped
        store.Current.Additions[^1].Should().Be("cond10");
    }

    [Fact]
    public void SetLatestCard_UpdatesParentPointer()
    {
        var store = new ScreenTaskContextStore();
        var a = NewId();
        store.Register(a, "task", ScreenAnalysisMode.SolveCodingTask, isNewGroup: true);
        var b = NewId();

        store.SetLatestCard(b);

        store.Current!.LatestCardId.Should().Be(b);
        store.Current.ScreenCardId.Should().Be(a, "the anchor card id is unchanged");
    }

    [Fact]
    public void Clear_RemovesContext()
    {
        var store = new ScreenTaskContextStore();
        store.Register(NewId(), "task", ScreenAnalysisMode.SolveCodingTask, isNewGroup: true);

        store.Clear();

        store.Current.Should().BeNull();
    }
}
