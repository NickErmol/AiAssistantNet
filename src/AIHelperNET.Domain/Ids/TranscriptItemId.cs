namespace AIHelperNET.Domain.Ids;

/// <summary>Strongly-typed identifier for a transcript item.</summary>
public readonly record struct TranscriptItemId(Guid Value)
{
    /// <summary>Creates a new unique transcript item identifier.</summary>
    public static TranscriptItemId New() => new(Guid.CreateVersion7());
}
