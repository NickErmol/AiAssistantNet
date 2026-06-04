namespace AIHelperNET.Domain.ValueObjects;

/// <summary>
/// Specifies the desired length of answers.
/// </summary>
public enum AnswerLength
{
    /// <summary>Very brief answer.</summary>
    VeryShort,

    /// <summary>Short answer.</summary>
    ShortLength,

    /// <summary>Medium-length answer.</summary>
    Medium,

    /// <summary>Detailed answer with comprehensive explanation.</summary>
    Detailed,

    /// <summary>Deep dive with exhaustive detail.</summary>
    DeepDive
}

/// <summary>
/// Specifies the complexity level of answers.
/// </summary>
public enum AnswerComplexity
{
    /// <summary>Simple, beginner-friendly explanation.</summary>
    Simple,

    /// <summary>Balanced approach suitable for most developers.</summary>
    Balanced,

    /// <summary>Advanced concepts and patterns.</summary>
    Advanced,

    /// <summary>Senior-level expertise and insights.</summary>
    Senior
}

/// <summary>
/// Specifies the style or format of answer presentation.
/// </summary>
public enum AnswerStyle
{
    /// <summary>Natural, conversational style.</summary>
    Natural,

    /// <summary>Interview-focused approach with tips and strategies.</summary>
    Interview,

    /// <summary>Technical, precise explanation.</summary>
    Technical,

    /// <summary>Step-by-step walkthrough.</summary>
    StepByStep,

    /// <summary>Code-first approach with implementation examples.</summary>
    CodeFirst,

    /// <summary>Architecture and system design focused.</summary>
    Architecture,

    /// <summary>Debugging and troubleshooting approach.</summary>
    Debugging
}

/// <summary>
/// Specifies the tone of answers.
/// </summary>
public enum AnswerTone
{
    /// <summary>Calm and relaxed tone.</summary>
    Calm,

    /// <summary>Confident and assertive tone.</summary>
    Confident,

    /// <summary>Professional and formal tone.</summary>
    Professional,

    /// <summary>Friendly and approachable tone.</summary>
    Friendly
}

/// <summary>
/// Specifies the format for presenting answers.
/// </summary>
public enum AnswerFormat
{
    /// <summary>Verbal/text only, no code.</summary>
    VerbalOnly,

    /// <summary>Explanation with accompanying code examples.</summary>
    ExplanationPlusCode,

    /// <summary>Code only, minimal explanation.</summary>
    CodeOnly,

    /// <summary>Explanation with notes but no code.</summary>
    ExplanationPlusNotes
}

/// <summary>
/// Value object that configures how answers are generated and presented.
/// </summary>
/// <param name="Length">Desired answer length.</param>
/// <param name="Complexity">Desired complexity level.</param>
/// <param name="Style">Desired presentation style.</param>
/// <param name="Tone">Desired tone of the answer.</param>
/// <param name="Format">Desired format for presenting the answer.</param>
/// <param name="OutputLanguage">Output language for the answer.</param>
public sealed record AnswerSettings(
    AnswerLength Length,
    AnswerComplexity Complexity,
    AnswerStyle Style,
    AnswerTone Tone,
    AnswerFormat Format,
    string OutputLanguage)
{
    /// <summary>
    /// Gets the default answer settings (medium length, balanced complexity, interview style, confident tone, explanation with code, English).
    /// </summary>
    public static AnswerSettings Default => new(
        AnswerLength.ShortLength,
        AnswerComplexity.Balanced,
        AnswerStyle.Interview,
        AnswerTone.Confident,
        AnswerFormat.VerbalOnly,
        "English");
}
