namespace AIHelperNET.Domain.Sessions;

/// <summary>
/// Represents the source of a question.
/// </summary>
public enum QuestionSource
{
    /// <summary>The question was captured from audio.</summary>
    Audio,

    /// <summary>The question was captured via OCR (optical character recognition).</summary>
    Ocr,

    /// <summary>The question was entered manually.</summary>
    Manual
}
