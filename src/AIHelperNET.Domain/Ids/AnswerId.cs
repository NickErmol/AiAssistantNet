namespace AIHelperNET.Domain.Ids;

/// <summary>Strongly-typed identifier for an answer.</summary>
public readonly record struct AnswerId(Guid Value)
{
    /// <summary>Creates a new unique answer identifier.</summary>
    public static AnswerId New() => new(Guid.CreateVersion7());
}
