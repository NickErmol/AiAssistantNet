namespace AIHelperNET.Application.Answers;

/// <summary>Display-only markers a streaming answer provider may append to the live chunk stream.
/// They must be stripped before persisting the answer so they never contaminate the recent-Q&amp;A
/// context injected into future prompts. Lives in Application (not Infrastructure) so the command
/// handlers can reference it without violating the onion dependency rule.</summary>
public static class AnswerStreamMarkers
{
    /// <summary>Appended when generation stopped at the max_tokens cap, so the user sees the answer
    /// is incomplete instead of trusting a silent mid-sentence stop.</summary>
    public const string Truncated = "\n\n*(answer cut off at the length limit)*";

    /// <summary>Removes a trailing display marker before the answer text is persisted.</summary>
    public static string StripForStorage(string text)
        => text.EndsWith(Truncated, StringComparison.Ordinal)
            ? text[..^Truncated.Length].TrimEnd()
            : text;
}
