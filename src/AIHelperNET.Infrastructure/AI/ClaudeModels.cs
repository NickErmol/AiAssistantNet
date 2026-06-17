using AIHelperNET.Application.Abstractions;

namespace AIHelperNET.Infrastructure.AI;

/// <summary>Maps the abstract <see cref="AnswerModel"/> selection to a concrete Claude model id.</summary>
public static class ClaudeModels
{
    /// <summary>Resolves the model id for the given selection.</summary>
    public static string Resolve(AnswerModel model) => model switch
    {
        AnswerModel.Haiku  => "claude-haiku-4-5-20251001",
        AnswerModel.Sonnet => "claude-sonnet-4-6",
        AnswerModel.Opus   => "claude-opus-4-8",
        _                  => "claude-haiku-4-5-20251001"
    };
}
