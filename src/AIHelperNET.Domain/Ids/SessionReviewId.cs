namespace AIHelperNET.Domain.Ids;

/// <summary>Strongly-typed identifier for a session review.</summary>
public readonly record struct SessionReviewId(Guid Value)
{
    /// <summary>Creates a new unique session review identifier.</summary>
    public static SessionReviewId New() => new(Guid.CreateVersion7());
}
