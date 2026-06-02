using System.Text;
using AIHelperNET.Domain.Ids;

namespace AIHelperNET.Domain.Sessions;

/// <summary>Represents an AI-generated answer that is streamed in chunks.</summary>
public sealed class GeneratedAnswer
{
    private readonly StringBuilder _buffer = new();

    /// <summary>Unique identifier for this answer.</summary>
    public AnswerId Id { get; }

    /// <summary>The question this answer responds to.</summary>
    public QuestionId QuestionId { get; }

    /// <summary>When answer generation began.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>When answer generation finished (completed, cancelled, or failed).</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Current generation status.</summary>
    public AnswerStatus Status { get; private set; }

    /// <summary>The accumulated answer content built from streamed chunks.</summary>
    public string Content => _buffer.ToString();

    private GeneratedAnswer(AnswerId id, QuestionId questionId, DateTimeOffset at)
        => (Id, QuestionId, StartedAt, Status) = (id, questionId, at, AnswerStatus.Streaming);

    /// <summary>Creates a new <see cref="GeneratedAnswer"/> in <see cref="AnswerStatus.Streaming"/> state.</summary>
    public static GeneratedAnswer Create(QuestionId questionId, DateTimeOffset at)
        => new(AnswerId.New(), questionId, at);

    /// <summary>Appends a streamed chunk to the accumulated content.</summary>
    public void AppendChunk(string chunk) => _buffer.Append(chunk);

    /// <summary>Marks the answer as successfully completed.</summary>
    public void Complete(DateTimeOffset at) { Status = AnswerStatus.Completed; CompletedAt = at; }

    /// <summary>Cancels a streaming answer; no-op if already finalised.</summary>
    public void Cancel(DateTimeOffset at)
    {
        if (Status == AnswerStatus.Streaming)
        {
            Status = AnswerStatus.Cancelled;
            CompletedAt = at;
        }
    }

    /// <summary>Marks the answer as failed.</summary>
    public void Fail(DateTimeOffset at) { Status = AnswerStatus.Failed; CompletedAt = at; }

#pragma warning disable CS8618 // EF Core parameterless constructor — properties set by materialiser
    private GeneratedAnswer() { }
#pragma warning restore CS8618
}
