namespace AIHelperNET.Domain.Ids;

/// <summary>Strongly-typed identifier for a question.</summary>
public readonly record struct QuestionId(Guid Value)
{
    /// <summary>Creates a new unique question identifier.</summary>
    public static QuestionId New() => new(Guid.CreateVersion7());
}
