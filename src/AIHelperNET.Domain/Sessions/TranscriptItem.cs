using AIHelperNET.Domain.Ids;

namespace AIHelperNET.Domain.Sessions;

/// <summary>Represents a single utterance captured from the audio transcript.</summary>
public sealed class TranscriptItem
{
    /// <summary>Unique identifier for this transcript item.</summary>
    public TranscriptItemId Id { get; }

    /// <summary>The speaker who produced this utterance.</summary>
    public Speaker Speaker { get; }

    /// <summary>The trimmed text of the utterance.</summary>
    public string Text { get; }

    /// <summary>When the utterance was captured.</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>Speech recognition confidence score (0.0–1.0).</summary>
    public float Confidence { get; }

    /// <summary>The role this transcript item played in question boundary detection.</summary>
    public BoundaryRole BoundaryRole { get; private set; }

    private TranscriptItem(TranscriptItemId id, Speaker speaker, string text,
        DateTimeOffset ts, float confidence)
        => (Id, Speaker, Text, Timestamp, Confidence) = (id, speaker, text, ts, confidence);

    /// <summary>Creates a new <see cref="TranscriptItem"/>, trimming <paramref name="text"/>.</summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text"/> is null, empty, or whitespace.</exception>
    public static TranscriptItem Create(Speaker speaker, string text, DateTimeOffset ts, float confidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new TranscriptItem(TranscriptItemId.New(), speaker, text.Trim(), ts, confidence);
    }

    /// <summary>Sets the boundary role for this transcript item.</summary>
    /// <param name="role">The boundary role to assign.</param>
    public void SetBoundaryRole(BoundaryRole role) => BoundaryRole = role;

#pragma warning disable CS8618 // EF Core parameterless constructor — properties set by materialiser
    private TranscriptItem() { }
#pragma warning restore CS8618
}
