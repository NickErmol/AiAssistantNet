namespace AIHelperNET.Domain.Ids;

/// <summary>Strongly-typed identifier for an answer version.</summary>
public readonly record struct AnswerVersionId(Guid Value)
{
    /// <summary>Creates a new unique answer version identifier.</summary>
    public static AnswerVersionId New() => new(Guid.CreateVersion7());
}
