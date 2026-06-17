using AIHelperNET.Application.Abstractions;

namespace AIHelperNET.Application.Answers;

/// <summary>A structured prompt ready to send to an AI answer provider.</summary>
/// <param name="System">System/context instructions for the model.</param>
/// <param name="User">The user-facing question turn.</param>
/// <param name="OutputLanguage">Language the answer should be in.</param>
/// <param name="MaxTokens">Maximum token budget for the response.</param>
/// <param name="Model">Preferred answer model (Claude backend); null ⇒ provider default.</param>
public sealed record AnswerPrompt(
    string System,
    string User,
    string OutputLanguage,
    int MaxTokens,
    AnswerModel? Model = null);
