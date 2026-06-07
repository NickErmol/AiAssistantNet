using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Domain.Questions;

/// <summary>
/// Heuristic classifier that maps a transcript segment to a <see cref="BoundaryLabel"/>
/// using fast, deterministic rules (no external dependencies, &lt;1 ms per call).
/// Rules are evaluated in short-circuit order; the first matching rule wins.
/// </summary>
public sealed class QuestionBoundaryDetector
{
    private const double DuplicateThreshold = 0.6;

    private static readonly HashSet<string> Interrogatives = new(StringComparer.OrdinalIgnoreCase)
    {
        "what", "why", "how", "when", "where", "which", "who",
        "can", "could", "would", "will", "do", "does", "did",
        "is", "are", "should"
    };

    private static readonly HashSet<string> Imperatives = new(StringComparer.OrdinalIgnoreCase)
    {
        "explain", "describe", "write", "implement", "design", "compare",
        "optimize", "refactor", "debug", "walk", "tell", "give", "show",
        "analyze", "fix", "build", "create", "outline", "discuss"
    };

    private static readonly string[] FillerPhrases =
    [
        "okay", "ok", "right", "sure", "great", "thanks", "thank you",
        "can you hear me", "give me a second", "one moment", "let me think",
        "alright", "sounds good", "got it", "perfect", "yep", "nope",
        "i will share my screen", "i'll share my screen"
    ];

    private static readonly string[] ScenarioStarters =
    [
        "let's say", "let us say", "imagine that", "imagine we",
        "suppose that", "suppose we", "let's assume", "assume that we", "assume that you"
    ];

    private static readonly string[] ContinuationPrefixes =
    [
        "and ", "also ", "including ", "assume that", "it should", "the system should",
        "to clarify", "one more thing", "additionally", "furthermore", "moreover",
        "on top of that", "in addition"
    ];

    private static readonly string[] NewTopicStarters =
    [
        "now ", "next ", "another question", "let's move to", "what about",
        "moving on", "let's talk about", "switching to", "now let's"
    ];

    private static readonly string[] AdditionalRequirementPrefixes =
    [
        "also ", "assume ", "one more", "additionally", "but also", "as well"
    ];

    /// <summary>
    /// Evaluates a transcript segment and returns a <see cref="BoundaryClassificationResult"/>
    /// describing how it should be handled in the conversation flow.
    /// </summary>
    /// <param name="text">The transcript text to classify.</param>
    /// <param name="speaker">The speaker who produced this segment.</param>
    /// <param name="activeTurnStatus">
    /// The status of the currently active conversation turn, or <see langword="null"/>
    /// if no turn is active.
    /// </param>
    /// <param name="recentQuestions">
    /// Recent question texts used for duplicate detection via Jaccard similarity.
    /// </param>
    /// <returns>
    /// A <see cref="BoundaryClassificationResult"/> with a populated
    /// <see cref="BoundaryClassificationResult.Classification"/> and supporting flags.
    /// </returns>
#pragma warning disable CA1822 // instance method intentional — callers hold a QuestionBoundaryDetector reference
    public BoundaryClassificationResult Evaluate(
        string text,
        Speaker speaker,
        ConversationTurnStatus? activeTurnStatus,
        IReadOnlyList<string> recentQuestions)
#pragma warning restore CA1822
    {
        // Rule 1: Empty/whitespace → NoQuestion
        if (string.IsNullOrWhiteSpace(text))
        {
            return NoQuestion(text, 1.0, "Empty or whitespace input");
        }

        var normalized = text.Trim();
        var normalizedLower = normalized.ToLowerInvariant();

        // Rule 2: Word count < 4 → Unrelated
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 4)
        {
            return Unrelated(normalized, 0.95, "Fewer than 4 words");
        }

        // Rule 3: Filler list match → Unrelated
        var matchedFiller = FindFillerPhrase(normalizedLower);
        if (matchedFiller is not null)
        {
            return Unrelated(normalized, 0.90, $"Starts with filler phrase '{matchedFiller}'");
        }

        // Rule 4: Speaker == Me AND active turn → ClarificationOfCurrentQuestion
        // CollectingQuestion is the only state where the heuristic is reliable: the interviewer
        // is mid-sentence, so Speaker.Me is almost certainly a clarification interjection.
        // All other statuses use low confidence because the pipeline's view of turn status is
        // stale (answer-handler runs in a separate DI scope and updates a different Session
        // instance), so the AI classifier must decide between clarification and new question.
        if (speaker == Speaker.Me && activeTurnStatus is not null)
        {
            var isCollecting = activeTurnStatus == ConversationTurnStatus.CollectingQuestion;
            return new BoundaryClassificationResult(
                Classification: BoundaryLabel.ClarificationOfCurrentQuestion,
                Confidence: isCollecting ? 0.85 : 0.50,
                ShouldGenerateAnswer: false,
                ShouldRefineExistingAnswer: false,
                ShouldCreateNewTurn: false,
                NormalizedQuestionText: normalized,
                Reason: isCollecting
                    ? "Speaker is Me while interviewer is collecting a question — treating as clarification"
                    : "Speaker is Me with active turn — AI classifier needed (turn status may be stale)");
        }

        // Rule 5: Scenario setup starters → QuestionStarted
        var firstWord = FirstWord(normalized);
        foreach (var starter in ScenarioStarters)
        {
            if (normalizedLower.StartsWith(starter, StringComparison.OrdinalIgnoreCase)
                && !normalized.EndsWith('?')
                && !Interrogatives.Contains(firstWord))
            {
                return new BoundaryClassificationResult(
                    Classification: BoundaryLabel.QuestionStarted,
                    Confidence: 0.85,
                    ShouldGenerateAnswer: false,
                    ShouldRefineExistingAnswer: false,
                    ShouldCreateNewTurn: true,
                    NormalizedQuestionText: normalized,
                    Reason: $"Scenario setup starter '{starter}'");
            }
        }

        // Rule 6: Active CollectingQuestion turn + continuation word → QuestionContinued
        if (activeTurnStatus == ConversationTurnStatus.CollectingQuestion)
        {
            foreach (var prefix in ContinuationPrefixes)
            {
                if (normalizedLower.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return new BoundaryClassificationResult(
                        Classification: BoundaryLabel.QuestionContinued,
                        Confidence: 0.85,
                        ShouldGenerateAnswer: false,
                        ShouldRefineExistingAnswer: false,
                        ShouldCreateNewTurn: false,
                        NormalizedQuestionText: normalized,
                        Reason: $"Continuation of collecting-question turn with prefix '{prefix}'");
                }
            }
        }

        // Rule 7: New-topic markers + question/task → NewQuestion
        foreach (var starter in NewTopicStarters)
        {
            if (normalizedLower.StartsWith(starter, StringComparison.OrdinalIgnoreCase))
            {
                var isQuestion = normalized.EndsWith('?')
                    || (Interrogatives.Contains(firstWord) && words.Length >= 6)
                    || (Imperatives.Contains(firstWord) && words.Length >= 4);

                if (isQuestion)
                {
                    return new BoundaryClassificationResult(
                        Classification: BoundaryLabel.NewQuestion,
                        Confidence: 0.85,
                        ShouldGenerateAnswer: true,
                        ShouldRefineExistingAnswer: false,
                        ShouldCreateNewTurn: true,
                        NormalizedQuestionText: normalized,
                        Reason: $"New-topic starter '{starter}' with question/task marker");
                }
            }
        }

        // Rule 8: Active answered turn + constraint word → AdditionalRequirement
        if (activeTurnStatus is ConversationTurnStatus.PreliminaryReady
            or ConversationTurnStatus.RefinedReady)
        {
            foreach (var prefix in AdditionalRequirementPrefixes)
            {
                if (normalizedLower.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return new BoundaryClassificationResult(
                        Classification: BoundaryLabel.AdditionalRequirement,
                        Confidence: 0.85,
                        ShouldGenerateAnswer: false,
                        ShouldRefineExistingAnswer: true,
                        ShouldCreateNewTurn: false,
                        NormalizedQuestionText: normalized,
                        Reason: $"Additional requirement prefix '{prefix}' on answered turn");
                }
            }
        }

        // Rule 9: QuestionComplete — ends with "?" OR interrogative first word with ≥6 words
        if (normalized.EndsWith('?')
            || (Interrogatives.Contains(firstWord) && words.Length >= 6))
        {
            return new BoundaryClassificationResult(
                Classification: BoundaryLabel.QuestionComplete,
                Confidence: 0.85,
                ShouldGenerateAnswer: true,
                ShouldRefineExistingAnswer: false,
                ShouldCreateNewTurn: true,
                NormalizedQuestionText: normalized,
                Reason: "Ends with '?' or interrogative start with sufficient word count");
        }

        // Rule 9.5: Indirect imperative — "you [imperative-verb] …" (≥5 words)
        // Handles phrases like "You tell me about X", "You explain how Y works"
        if (words.Length >= 5
            && words[0].Trim(',', '.', '?', '!').Equals("you", StringComparison.OrdinalIgnoreCase)
            && Imperatives.Contains(words[1].ToLowerInvariant().Trim('.', '?', '!')))
        {
            return new BoundaryClassificationResult(
                Classification: BoundaryLabel.TaskComplete,
                Confidence: 0.85,
                ShouldGenerateAnswer: true,
                ShouldRefineExistingAnswer: false,
                ShouldCreateNewTurn: true,
                NormalizedQuestionText: normalized,
                Reason: $"Indirect imperative 'you {words[1].ToLowerInvariant().Trim('.', '?', '!')}'");
        }

        // Rule 10: TaskComplete — imperative first word with ≥4 words
        if (Imperatives.Contains(firstWord) && words.Length >= 4)
        {
            return new BoundaryClassificationResult(
                Classification: BoundaryLabel.TaskComplete,
                Confidence: 0.85,
                ShouldGenerateAnswer: true,
                ShouldRefineExistingAnswer: false,
                ShouldCreateNewTurn: true,
                NormalizedQuestionText: normalized,
                Reason: "Imperative verb start with sufficient word count");
        }

        // Rule 11: Duplicate detection via Jaccard similarity
        var candidateTokens = QuestionDetector.Tokenize(normalized);
        foreach (var prior in recentQuestions)
        {
            if (QuestionDetector.Jaccard(candidateTokens, QuestionDetector.Tokenize(prior)) >= DuplicateThreshold)
            {
                return NoQuestion(normalized, 0.90, "Duplicate of recent question");
            }
        }

        // Rule 12: Fallthrough → NoQuestion with low confidence (signals: call AI classifier)
        return new BoundaryClassificationResult(
            Classification: BoundaryLabel.NoQuestion,
            Confidence: 0.30,
            ShouldGenerateAnswer: false,
            ShouldRefineExistingAnswer: false,
            ShouldCreateNewTurn: false,
            NormalizedQuestionText: normalized,
            Reason: "Ambiguous — AI classifier needed");
    }

    private static BoundaryClassificationResult NoQuestion(string text, double confidence, string reason) =>
        new(
            Classification: BoundaryLabel.NoQuestion,
            Confidence: confidence,
            ShouldGenerateAnswer: false,
            ShouldRefineExistingAnswer: false,
            ShouldCreateNewTurn: false,
            NormalizedQuestionText: text,
            Reason: reason);

    private static BoundaryClassificationResult Unrelated(string text, double confidence, string reason) =>
        new(
            Classification: BoundaryLabel.Unrelated,
            Confidence: confidence,
            ShouldGenerateAnswer: false,
            ShouldRefineExistingAnswer: false,
            ShouldCreateNewTurn: false,
            NormalizedQuestionText: text,
            Reason: reason);

    private static string FirstWord(string text) =>
        text.Split(' ')[0].ToLowerInvariant().Trim('.', '?', '!');

    /// <summary>
    /// Detects if text starts with a filler phrase, using word-boundary awareness for single-word fillers.
    /// Multi-word fillers use StartsWith; single-word fillers must be complete words.
    /// </summary>
    private static string? FindFillerPhrase(string normalizedLower)
    {
        foreach (var filler in FillerPhrases)
        {
            if (!normalizedLower.StartsWith(filler, StringComparison.OrdinalIgnoreCase))
                continue;

            // Multi-word fillers: StartsWith is sufficient
            if (filler.Contains(' '))
                return filler;

            // Single-word fillers: ensure it's a complete word (end of string or followed by space/punctuation)
            if (normalizedLower.Length == filler.Length)
                return filler;

            var nextChar = normalizedLower[filler.Length];
            if (nextChar == ' ' || nextChar == ',' || nextChar == '.' || nextChar == '!' || nextChar == '?')
                return filler;
        }

        return null;
    }
}
