namespace AIHelperNET.Application.Abstractions;

/// <summary>Result of question classification.</summary>
public enum ClassificationResult
{
    /// <summary>The text is a new question, not seen in recent context.</summary>
    NewQuestion,
    /// <summary>The text is a continuation of a previous question or conversation flow.</summary>
    Continuation,
    /// <summary>The text is not a question at all.</summary>
    NotAQuestion
}

/// <summary>Port for classifying user input as questions using an LLM.</summary>
public interface IQuestionClassifier
{
    /// <summary>Classifies user input text as a new question, continuation, or not a question.</summary>
    /// <param name="combinedText">The combined transcript text to classify.</param>
    /// <param name="recentQuestions">Recent questions from the conversation history for context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The classification result.</returns>
    Task<ClassificationResult> ClassifyAsync(
        string combinedText,
        IReadOnlyList<string> recentQuestions,
        CancellationToken ct);
}
