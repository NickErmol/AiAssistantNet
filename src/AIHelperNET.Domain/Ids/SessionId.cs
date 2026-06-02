namespace AIHelperNET.Domain.Ids;

/// <summary>Strongly-typed identifier for a session.</summary>
public readonly record struct SessionId(Guid Value)
{
    /// <summary>Creates a new unique session identifier.</summary>
    public static SessionId New() => new(Guid.CreateVersion7());
}
