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

    private static readonly string[] FillerPhrases =
    [
        "okay", "ok", "right", "sure", "great", "thanks", "thank you",
        "can you hear me", "give me a second", "one moment", "let me think",
        "alright", "sounds good", "got it", "perfect", "yep", "nope",
        "i will share my screen", "i'll share my screen"
    ];

    // Greeting shapes matched as substrings — anywhere in the segment ("Hey Kumar, how are you?").
    // "How was your ..." is NOT matched bare: "how was your experience with X" is a canonical
    // experience question; only the unambiguous day/weekend/trip variants are listed (the
    // weekend/vacation words are also covered by PersonalTopicWords).
    private static readonly string[] GreetingPhrases =
    [
        "how are you", "how's it going", "how is it going", "how have you been",
        "nice to meet you", "good morning", "good afternoon", "good evening",
        "how was your day", "how was your weekend", "how was your trip",
        "how's your day", "how is your day"
    ];

    // Personal-life vocabulary matched as whole words. Deliberately narrow: words like "hurt",
    // "health", or "recovery" are excluded because they occur in real technical questions
    // ("does it hurt performance?", "disaster recovery").
    private static readonly HashSet<string> PersonalTopicWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "injury", "injured", "sick", "illness", "weather", "weekend",
        "vacation", "holiday", "holidays", "family", "hobbies", "hobby"
    };

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

        // Rule 2: Word count < 4 → Unrelated, UNLESS it begins with an imperative command.
        // A short fragment from the interviewer (Other) that looks like a technical topic
        // ("N+1 queries", "Func vs Expression<Func>") may be an implicit "explain this" — emit
        // low confidence so the pipeline (confidence < 0.7) defers to the AI classifier. A bare
        // topic from the candidate (Me) is a mid-answer aside, so it stays high-confidence
        // Unrelated and never burns an AI call.
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 4 && !QuestionLexicon.StartsWithImperative(normalized))
        {
            return speaker == Speaker.Other && LooksLikeTopic(normalized)
                ? Unrelated(normalized, 0.50, "Short topic — deferring to AI classifier")
                : Unrelated(normalized, 0.95, "Fewer than 4 words");
        }

        // Rule 3: Filler list match → Unrelated
        var matchedFiller = FindFillerPhrase(normalizedLower);
        if (matchedFiller is not null)
        {
            return Unrelated(normalized, 0.90, $"Starts with filler phrase '{matchedFiller}'");
        }

        // Rule 3.5: Social/personal question from the interviewer → defer to the AI classifier.
        // Greetings and personal-life questions ("what injury do you have?") are grammatically
        // complete questions, so the rules below would fire QuestionComplete at 0.85 and burn a
        // generation on small talk. Low confidence routes them to the AI classifier (whose
        // Unrelated label covers social questions) — this rule only defers, never hard-drops.
        if (speaker == Speaker.Other && LooksSocial(normalizedLower))
        {
            return Unrelated(normalized, 0.50, "Social/personal question — deferring to AI classifier");
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
                    || (QuestionLexicon.ImperativeVerbs.Contains(firstWord) && words.Length >= 4);

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
            && QuestionLexicon.ImperativeVerbs.Contains(words[1].ToLowerInvariant().Trim('.', '?', '!')))
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

        // Rule 10: TaskComplete — imperative command (verb or phrase, politeness-stripped)
        // with ≥2 words. The 2-word floor lets short commands ("Define recursion") through;
        // the interrogative gates above keep their ≥4/≥6 floors.
        if (QuestionLexicon.StartsWithImperative(normalized)
            && QuestionLexicon.StrippedWordCount(normalized) >= 2)
        {
            return new BoundaryClassificationResult(
                Classification: BoundaryLabel.TaskComplete,
                Confidence: 0.85,
                ShouldGenerateAnswer: true,
                ShouldRefineExistingAnswer: false,
                ShouldCreateNewTurn: true,
                NormalizedQuestionText: normalized,
                Reason: "Imperative command start with sufficient word count");
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

    /// <summary>Sniffs interviewer small talk: a greeting shape anywhere in the text, or a
    /// personal-life word as a whole word. Used only to LOWER confidence so the AI classifier
    /// decides — never to assert Unrelated on its own.</summary>
    private static bool LooksSocial(string normalizedLower)
    {
        foreach (var greeting in GreetingPhrases)
        {
            if (normalizedLower.Contains(greeting, StringComparison.Ordinal))
                return true;
        }

        foreach (var token in normalizedLower.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (PersonalTopicWords.Contains(token.Trim('.', ',', '?', '!', ';', ':')))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Backchannel / acknowledgement / function words that carry no topic content. A short
    /// fragment built only from these (plus <see cref="Interrogatives"/>) is conversational noise,
    /// not a topic. Used by <see cref="LooksLikeTopic"/> to keep acknowledgements from being
    /// escalated to the AI classifier.
    /// </summary>
    private static readonly HashSet<string> TopicStopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "okay", "ok", "oh", "ah", "um", "uh", "hmm", "mhm", "yeah", "yep", "yes", "no", "nope",
        "nah", "right", "sure", "great", "good", "nice", "cool", "fine", "perfect", "thanks",
        "thank", "you", "your", "got", "it", "its", "make", "makes", "made", "sense", "sound",
        "sounds", "alright", "well", "so", "and", "but", "or", "the", "a", "an", "of", "to", "in",
        "on", "at", "for", "is", "are", "am", "was", "were", "be", "i", "we", "me", "my", "our",
        "that", "this", "these", "those", "here", "there", "please", "kindly", "gotcha", "totally",
        "absolutely", "definitely", "agreed", "indeed", "exactly", "see", "correct", "true",
        "really", "very", "just", "like", "about", "mean", "okay.",
    };

    /// <summary>
    /// Heuristic sniff for a short fragment that reads like a topic worth explaining. Matches two
    /// shapes: (1) a <em>technical</em> token — code punctuation, "vs"/"versus", a mixed
    /// alphanumeric token ("N+1", "IPv4"), or an internal capital ("OnPush", "PascalCase"); or
    /// (2) a <em>plain-English</em> noun-phrase topic ("dependency injection", "the event loop")
    /// — two or more words that aren't pure backchannel/acknowledgement. Used only to LOWER
    /// confidence so the AI classifier is consulted — never to assert a question on its own.
    /// </summary>
    private static bool LooksLikeTopic(string text)
    {
        if (text.IndexOfAny(['<', '>', '(', ')', '{', '}', '[', ']', ':', '+', '/', '#']) >= 0)
            return true;

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var contentWords = 0;
        foreach (var token in tokens)
        {
            var bare = token.Trim('.', ',', '?', '!', ';', ':');
            if (bare.Equals("vs", StringComparison.OrdinalIgnoreCase)
                || bare.Equals("versus", StringComparison.OrdinalIgnoreCase))
                return true;
            if (bare.Any(char.IsDigit) && bare.Any(char.IsLetter))
                return true;
            if (bare.Length > 1 && bare.Skip(1).Any(char.IsUpper))
                return true;

            // A "content word" is a real word (≥3 letters) that isn't a stopword or interrogative.
            if (bare.Length >= 3 && bare.All(char.IsLetter)
                && !TopicStopwords.Contains(bare) && !Interrogatives.Contains(bare))
                contentWords++;
        }

        // Plain-English topic: ≥2 words overall with at least one content word. The 2-word floor
        // (with the stopword filter) excludes single-word and pure-acknowledgement fragments
        // ("Okay great", "Got it") so they don't burn an AI call.
        return tokens.Length >= 2 && contentWords >= 1;
    }

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
