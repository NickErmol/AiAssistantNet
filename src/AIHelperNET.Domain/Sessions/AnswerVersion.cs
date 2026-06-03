using AIHelperNET.Domain.Ids;

namespace AIHelperNET.Domain.Sessions;

/// <summary>An immutable snapshot of one AI-generated answer version within a conversation turn.</summary>
public sealed record AnswerVersion(
    AnswerVersionId Id,
    AnswerVersionType VersionType,
    string Text,
    DateTimeOffset CreatedAt,
    AnswerVersionId? SupersedesId = null)
{
    /// <summary>Creates a new <see cref="AnswerVersion"/> with a fresh ID.</summary>
    public static AnswerVersion Create(
        AnswerVersionType type, string text, DateTimeOffset now,
        AnswerVersionId? supersedes = null)
        => new(AnswerVersionId.New(), type, text, now, supersedes);
}
