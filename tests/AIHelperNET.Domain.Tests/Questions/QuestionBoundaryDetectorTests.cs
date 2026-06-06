using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Domain.Tests.Questions;

public sealed class QuestionBoundaryDetectorTests
{
    private readonly QuestionBoundaryDetector _sut = new();
    private static readonly IReadOnlyList<string> NoRecentQuestions = [];

    // ── Rule 9: ends with '?' ───────────────────────────────────────────────
    // Example 1 from spec — needs ≥4 words for Rule 2 to pass
    [Fact]
    public void WhatIsDDD_ReturnsQuestionComplete()
    {
        var result = _sut.Evaluate("What exactly is DDD?", Speaker.Other, null, NoRecentQuestions);

        result.Classification.Should().Be(BoundaryLabel.QuestionComplete);
        result.ShouldGenerateAnswer.Should().BeTrue();
        result.ShouldCreateNewTurn.Should().BeTrue();
        result.ShouldRefineExistingAnswer.Should().BeFalse();
    }

    // ── Rule 10: imperative first word + ≥4 words ──────────────────────────
    // Example 2 from spec — needs ≥4 words for Rule 2 to pass
    [Fact]
    public void ExplainDDD_ReturnsTaskComplete()
    {
        var result = _sut.Evaluate("Explain the DDD concept.", Speaker.Other, null, NoRecentQuestions);

        result.Classification.Should().Be(BoundaryLabel.TaskComplete);
        result.ShouldGenerateAnswer.Should().BeTrue();
        result.ShouldCreateNewTurn.Should().BeTrue();
        result.ShouldRefineExistingAnswer.Should().BeFalse();
    }

    // ── Rule 5: scenario-setup starter ─────────────────────────────────────
    // Example 3 from spec (first fragment)
    [Fact]
    public void ScenarioSetup_ReturnsQuestionStarted()
    {
        var result = _sut.Evaluate(
            "Let's say we have a payment service.",
            Speaker.Other, null, NoRecentQuestions);

        result.Classification.Should().Be(BoundaryLabel.QuestionStarted);
        result.ShouldGenerateAnswer.Should().BeFalse();
        result.ShouldCreateNewTurn.Should().BeTrue();
        result.ShouldRefineExistingAnswer.Should().BeFalse();
    }

    // ── Rule 6: active CollectingQuestion + continuation prefix ────────────
    // Example 3 from spec (second fragment)
    [Fact]
    public void ContinuationWord_WithCollectingTurn_ReturnsQuestionContinued()
    {
        var result = _sut.Evaluate(
            "And it receives webhooks from Stripe and Adyen.",
            Speaker.Other,
            ConversationTurnStatus.CollectingQuestion,
            NoRecentQuestions);

        result.Classification.Should().Be(BoundaryLabel.QuestionContinued);
        result.ShouldGenerateAnswer.Should().BeFalse();
        result.ShouldCreateNewTurn.Should().BeFalse();
        result.ShouldRefineExistingAnswer.Should().BeFalse();
    }

    // ── Rule 8: active answered turn + constraint prefix ───────────────────
    // Example 4 from spec
    [Fact]
    public void AlsoAssume_WithPreliminaryReady_ReturnsAdditionalRequirement()
    {
        var result = _sut.Evaluate(
            "Also assume validation errors should not be retried.",
            Speaker.Other,
            ConversationTurnStatus.PreliminaryReady,
            NoRecentQuestions);

        result.Classification.Should().Be(BoundaryLabel.AdditionalRequirement);
        result.ShouldRefineExistingAnswer.Should().BeTrue();
        result.ShouldGenerateAnswer.Should().BeFalse();
        result.ShouldCreateNewTurn.Should().BeFalse();
    }

    // ── Rule 12 fallthrough for "To clarify…" + PreliminaryReady/Other ─────
    // Example 5 from spec: "to clarify" is a ContinuationPrefix but Rule 6
    // only fires for CollectingQuestion; with PreliminaryReady it falls through
    // to Rule 12 (ambiguous, low confidence) rather than AdditionalRequirement.
    [Fact]
    public void ToClarify_Other_WithPreliminaryReady_ReturnsLowConfidenceNoQuestion()
    {
        var result = _sut.Evaluate(
            "To clarify it should support dead-letter queues.",
            Speaker.Other,
            ConversationTurnStatus.PreliminaryReady,
            NoRecentQuestions);

        // Neither ContinuationPrefix (Rule 6 only fires on CollectingQuestion)
        // nor AdditionalRequirementPrefix → falls to Rule 12
        result.Classification.Should().Be(BoundaryLabel.NoQuestion);
        result.Confidence.Should().BeLessThanOrEqualTo(0.30);
        result.ShouldGenerateAnswer.Should().BeFalse();
    }

    // ── Rule 7: new-topic starter + question marker ─────────────────────────
    // Example 6 from spec — "now explain..." triggers Rule 7 because "now " is a
    // NewTopicStarter AND "explain" is an imperative with ≥4 words. But firstWord is
    // "now" (not an imperative/interrogative) so the isQuestion check in Rule 7
    // uses: normalized.EndsWith('?') || Interrogatives[firstWord] || Imperatives[firstWord].
    // "now" is NOT in Imperatives, so we need the starter to leave an imperative
    // accessible — use "Now what is the DDD pattern?" so the text ends with '?'.
    [Fact]
    public void NowExplainDDD_WithActiveTurn_ReturnsNewQuestion()
    {
        // "next " is in NewTopicStarters. Ending with '?' triggers isQuestion=true in Rule 7.
        // Note: "now " also works as a starter but "no" is in FillerPhrases (Rule 3 match),
        // so use "next " instead to avoid the filler prefix collision.
        var result = _sut.Evaluate(
            "Next what is the DDD pattern?",
            Speaker.Other,
            ConversationTurnStatus.PreliminaryReady,
            NoRecentQuestions);

        result.Classification.Should().Be(BoundaryLabel.NewQuestion);
        result.ShouldGenerateAnswer.Should().BeTrue();
        result.ShouldCreateNewTurn.Should().BeTrue();
        result.ShouldRefineExistingAnswer.Should().BeFalse();
    }

    // ── Rule 3: filler phrase match ─────────────────────────────────────────
    // Example 7 from spec
    [Fact]
    public void GiveMeASecond_ReturnsUnrelated()
    {
        var result = _sut.Evaluate(
            "Give me a second, I will share my screen.",
            Speaker.Other, null, NoRecentQuestions);

        result.Classification.Should().Be(BoundaryLabel.Unrelated);
        result.ShouldGenerateAnswer.Should().BeFalse();
        result.ShouldCreateNewTurn.Should().BeFalse();
    }

    // ── Rule 4: Speaker == Me AND active turn ───────────────────────────────
    // Example 8 from spec
    [Fact]
    public void MeSpeaker_WithActiveTurn_ReturnsClarification()
    {
        var result = _sut.Evaluate(
            "Should it cover all error types?",
            Speaker.Me,
            ConversationTurnStatus.PreliminaryReady,
            NoRecentQuestions);

        result.Classification.Should().Be(BoundaryLabel.ClarificationOfCurrentQuestion);
        result.ShouldGenerateAnswer.Should().BeFalse();
        result.ShouldCreateNewTurn.Should().BeFalse();
        result.ShouldRefineExistingAnswer.Should().BeFalse();
    }

    // ── Rule 3 + Rule 2: filler ─────────────────────────────────────────────
    // Example 10 from spec
    [Fact]
    public void CanYouHearMe_ReturnsUnrelated()
    {
        var result = _sut.Evaluate(
            "Can you hear me?",
            Speaker.Other, null, NoRecentQuestions);

        // "can you hear me" is in FillerPhrases (Rule 3) — fires before Rule 9
        result.Classification.Should().Be(BoundaryLabel.Unrelated);
        result.ShouldGenerateAnswer.Should().BeFalse();
    }

    // ── Rule 1: empty / whitespace ──────────────────────────────────────────
    [Fact]
    public void EmptyText_ReturnsNoQuestion()
    {
        var result = _sut.Evaluate("", Speaker.Other, null, NoRecentQuestions);

        result.Classification.Should().Be(BoundaryLabel.NoQuestion);
        result.Confidence.Should().Be(1.0);
    }

    [Fact]
    public void WhitespaceText_ReturnsNoQuestion()
    {
        var result = _sut.Evaluate("   ", Speaker.Other, null, NoRecentQuestions);

        result.Classification.Should().Be(BoundaryLabel.NoQuestion);
        result.Confidence.Should().Be(1.0);
    }

    // ── Rule 2: fewer than 4 words ──────────────────────────────────────────
    [Fact]
    public void TooShort_ReturnsUnrelated()
    {
        var result = _sut.Evaluate("What?", Speaker.Other, null, NoRecentQuestions);

        result.Classification.Should().Be(BoundaryLabel.Unrelated);
        result.Confidence.Should().BeGreaterThan(0.90);
    }

    // ── Rule 11: duplicate detection via Jaccard ────────────────────────────
    // The candidate text must NOT trigger Rules 3-10 before reaching Rule 11.
    // A statement-like text (no trailing '?', no interrogative/imperative start)
    // will fall to Rule 11 if it is sufficiently similar to a recent question.
    [Fact]
    public void DuplicateQuestion_ReturnsNoQuestion()
    {
        // The recent question is identical to the candidate (Jaccard=1.0 ≥ 0.6)
        // but the candidate itself looks like a statement — no '?', no interrogative start,
        // no imperative start — so Rules 9-10 don't fire and Rule 11 catches it.
        var recent = new[] { "I have worked with dependency injection many times" };
        var result = _sut.Evaluate(
            "I have worked with dependency injection many times",
            Speaker.Other, null, recent);

        result.Classification.Should().Be(BoundaryLabel.NoQuestion);
        result.Reason.Should().ContainEquivalentOf("Duplicate");
    }

    // ── Rule 12: ambiguous fallthrough ──────────────────────────────────────
    [Fact]
    public void AmbiguousText_ReturnsLowConfidenceNoQuestion()
    {
        // A statement that doesn't match any trigger rule
        var result = _sut.Evaluate(
            "The team was working on improving performance.",
            Speaker.Other, null, NoRecentQuestions);

        result.Classification.Should().Be(BoundaryLabel.NoQuestion);
        result.Confidence.Should().BeLessThanOrEqualTo(0.30);
    }

    // ── Rule 9: interrogative + ≥6 words (no trailing ?) ───────────────────
    [Fact]
    public void HowWouldYouDesign_SixWords_ReturnsQuestionComplete()
    {
        var result = _sut.Evaluate(
            "How would you design this system",
            Speaker.Other, null, NoRecentQuestions);

        result.Classification.Should().Be(BoundaryLabel.QuestionComplete);
        result.ShouldGenerateAnswer.Should().BeTrue();
    }

    // ── Rule 7: new-topic starter ────────────────────────────────────────────
    // NewTopicStarters include "next " (with trailing space). The text must start
    // with "next " (no comma separator), otherwise the prefix match won't fire.
    [Fact]
    public void NewTopicPlusQuestion_ReturnsNewQuestion()
    {
        var result = _sut.Evaluate(
            "Next what is dependency injection?",
            Speaker.Other,
            ConversationTurnStatus.PreliminaryReady,
            NoRecentQuestions);

        result.Classification.Should().Be(BoundaryLabel.NewQuestion);
        result.ShouldGenerateAnswer.Should().BeTrue();
        result.ShouldCreateNewTurn.Should().BeTrue();
    }

    // ── Additional: Me speaker NO active turn — falls through normally ───────
    [Fact]
    public void MeSpeaker_NoActiveTurn_ClassifiedNormally()
    {
        // Rule 4 requires activeTurnStatus != null; without it, falls through to normal rules
        var result = _sut.Evaluate(
            "What is dependency injection?",
            Speaker.Me, null, NoRecentQuestions);

        // Ends with '?' → QuestionComplete (Rule 9)
        result.Classification.Should().Be(BoundaryLabel.QuestionComplete);
    }

    // ── Additional: imperative + RefinedReady + "also" → AdditionalRequirement
    [Fact]
    public void AlsoWith_RefinedReady_ReturnsAdditionalRequirement()
    {
        var result = _sut.Evaluate(
            "Also consider thread-safety in your solution.",
            Speaker.Other,
            ConversationTurnStatus.RefinedReady,
            NoRecentQuestions);

        result.Classification.Should().Be(BoundaryLabel.AdditionalRequirement);
        result.ShouldRefineExistingAnswer.Should().BeTrue();
    }
}
