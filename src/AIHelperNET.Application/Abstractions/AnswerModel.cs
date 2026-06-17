namespace AIHelperNET.Application.Abstractions;

/// <summary>
/// Selects which Claude model generates answers. Ordered so the default (<see cref="Haiku"/> = 0)
/// preserves the current configured behavior. Maps to a concrete model id in the Infrastructure layer.
/// Applies to the Claude backend only; the Ollama backend ignores it.
/// </summary>
public enum AnswerModel
{
    /// <summary>Fastest time-to-first-token, lowest cost. Default.</summary>
    Haiku,

    /// <summary>Balanced — stronger reasoning than Haiku, faster than Opus.</summary>
    Sonnet,

    /// <summary>Highest quality, slowest time-to-first-token.</summary>
    Opus
}
