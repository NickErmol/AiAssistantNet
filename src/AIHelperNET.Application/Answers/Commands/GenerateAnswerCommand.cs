using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Answers.Commands;

/// <summary>Command to generate an AI answer for a detected question.</summary>
/// <param name="SessionId">Session containing the question.</param>
/// <param name="QuestionId">The question to answer.</param>
/// <param name="ScreenContext">Optional OCR context captured from the screen.</param>
public sealed record GenerateAnswerCommand(
    SessionId SessionId,
    QuestionId QuestionId,
    string? ScreenContext = null) : IRequest<Result<AnswerId>>;

/// <summary>Handles <see cref="GenerateAnswerCommand"/>.</summary>
public sealed class GenerateAnswerHandler(
    ISessionRepository repository,
    IAnswerProvider answerProvider,
    IAnswerStreamSink streamSink,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<GenerateAnswerCommand, Result<AnswerId>>
{
    /// <inheritdoc/>
    public async ValueTask<Result<AnswerId>> Handle(GenerateAnswerCommand cmd, CancellationToken cancellationToken)
    {
        var get = await repository.GetAsync(cmd.SessionId, cancellationToken);
        if (get.IsFailed) return get.ToResult<AnswerId>();
        var session = get.Value;

        var question = session.Questions.FirstOrDefault(q => q.Id == cmd.QuestionId);
        if (question is null) return Result.Fail("Question not found.");

        var start = session.StartAnswer(cmd.QuestionId, clock.GetUtcNow());
        if (start.IsFailed) return Result.Fail(start.Error);
        var answer = start.Value;

        var prompt = PromptBuilderService.Build(session.CodeProfile, session.AnswerSettings, question, cmd.ScreenContext);

        try
        {
            await foreach (var chunk in answerProvider.StreamAnswerAsync(prompt, cancellationToken))
            {
                answer.AppendChunk(chunk);
                await streamSink.PushAsync(answer.Id, chunk, cancellationToken);
            }
            answer.Complete(clock.GetUtcNow());
        }
        catch (OperationCanceledException)
        {
            answer.Cancel(clock.GetUtcNow());
        }
#pragma warning disable CA1031 // catching all exceptions to mark answer as failed
        catch (Exception)
        {
            answer.Fail(clock.GetUtcNow());
        }
#pragma warning restore CA1031

        repository.Update(session);
        var save = await unitOfWork.SaveChangesAsync(cancellationToken);
        return save.IsFailed ? save.ToResult<AnswerId>() : Result.Ok(answer.Id);
    }
}
