using AIHelperNET.Domain.Ids;

namespace AIHelperNET.Domain.Sessions;

/// <summary>Represents a question detected from the transcript or screen capture.</summary>
public sealed class DetectedQuestion
{
    /// <summary>Unique identifier for this question.</summary>
    public QuestionId Id { get; }

    /// <summary>The trimmed question text.</summary>
    public string Text { get; }

    /// <summary>The source from which the question was captured.</summary>
    public QuestionSource Source { get; }

    /// <summary>When the question was detected.</summary>
    public DateTimeOffset DetectedAt { get; }

    private DetectedQuestion(QuestionId id, string text, QuestionSource src, DateTimeOffset at)
        => (Id, Text, Source, DetectedAt) = (id, text, src, at);

    /// <summary>Creates a new <see cref="DetectedQuestion"/>, trimming <paramref name="text"/>.</summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text"/> is null, empty, or whitespace.</exception>
    public static DetectedQuestion Create(string text, QuestionSource source, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new DetectedQuestion(QuestionId.New(), text.Trim(), source, at);
    }

#pragma warning disable CS8618 // EF Core parameterless constructor — properties set by materialiser
    private DetectedQuestion() { }
#pragma warning restore CS8618
}
