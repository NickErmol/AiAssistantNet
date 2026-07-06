using AIHelperNET.Domain.Ids;

namespace AIHelperNET.Domain.Sessions;

/// <summary>Stores one LLM-generated markdown report per session.</summary>
public sealed class SessionReview
{
    /// <summary>Unique identifier for this review.</summary>
    public SessionReviewId Id { get; }

    /// <summary>The session this review belongs to.</summary>
    public SessionId SessionId { get; }

    /// <summary>The generated markdown content of the review.</summary>
    public string Markdown { get; private set; }

    /// <summary>The model identifier used to generate this review.</summary>
    public string ModelUsed { get; private set; }

    /// <summary>When the review was generated.</summary>
    public DateTimeOffset GeneratedAt { get; private set; }

    private SessionReview(SessionReviewId id, SessionId sessionId,
        string markdown, string modelUsed, DateTimeOffset generatedAt)
    {
        Id = id;
        SessionId = sessionId;
        Markdown = markdown;
        ModelUsed = modelUsed;
        GeneratedAt = generatedAt;
    }

    /// <summary>Creates a new <see cref="SessionReview"/>.</summary>
    /// <param name="sessionId">The session this review belongs to.</param>
    /// <param name="markdown">The generated markdown content. Must not be empty or whitespace.</param>
    /// <param name="modelUsed">The model identifier used. Must not be empty or whitespace.</param>
    /// <param name="generatedAt">When the review was generated.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="markdown"/> or <paramref name="modelUsed"/> is null, empty, or whitespace.</exception>
    public static SessionReview Create(
        SessionId sessionId, string markdown, string modelUsed, DateTimeOffset generatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelUsed);
        return new SessionReview(SessionReviewId.New(), sessionId, markdown, modelUsed, generatedAt);
    }

    /// <summary>Replaces the review content in place (used by Regenerate).</summary>
    /// <param name="markdown">The new markdown content. Must not be empty or whitespace.</param>
    /// <param name="modelUsed">The new model identifier. Must not be empty or whitespace.</param>
    /// <param name="generatedAt">The new generation timestamp.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="markdown"/> or <paramref name="modelUsed"/> is null, empty, or whitespace.</exception>
    public void Replace(string markdown, string modelUsed, DateTimeOffset generatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelUsed);
        Markdown = markdown;
        ModelUsed = modelUsed;
        GeneratedAt = generatedAt;
    }

#pragma warning disable CS8618 // EF Core parameterless constructor — properties set by materialiser
    private SessionReview() { }
#pragma warning restore CS8618
}
