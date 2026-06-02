using System.Globalization;
using System.Text;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;

namespace AIHelperNET.Application.Answers;

/// <summary>Builds structured <see cref="AnswerPrompt"/> objects from session context.</summary>
public sealed class PromptBuilderService
{
    /// <summary>Constructs an <see cref="AnswerPrompt"/> from the given session context.</summary>
    /// <param name="profile">Candidate's code profile.</param>
    /// <param name="settings">Answer generation settings.</param>
    /// <param name="question">The detected question to answer.</param>
    /// <param name="screenContext">Optional OCR text captured from the screen.</param>
    public static AnswerPrompt Build(
        CodeProfile profile,
        AnswerSettings settings,
        DetectedQuestion question,
        string? screenContext = null)
    {
        var system = new StringBuilder();
        system.AppendLine("You are an expert technical interview assistant. " +
            "Answer the candidate's interview question concisely and correctly.");

        AppendCodeProfile(system, profile);
        AppendAnswerSettings(system, settings);

        var user = new StringBuilder();
        user.AppendLine(CultureInfo.InvariantCulture, $"Question: {question.Text}");
        if (!string.IsNullOrWhiteSpace(screenContext))
            user.AppendLine(CultureInfo.InvariantCulture, $"\nOn-screen context (OCR):\n{screenContext}");

        return new AnswerPrompt(
            System: system.ToString(),
            User: user.ToString(),
            OutputLanguage: settings.OutputLanguage,
            MaxTokens: MapLengthToTokens(settings.Length));
    }

    private static void AppendCodeProfile(StringBuilder sb, CodeProfile p)
    {
        sb.AppendLine("\n# Candidate technical profile (use this stack in code/examples):");
        AppendIf(sb, "Programming language", p.ProgrammingLanguage);
        AppendIf(sb, "Backend framework",    p.BackendFramework);
        AppendIf(sb, "Frontend framework",   p.FrontendFramework);
        AppendIf(sb, "Database",             p.Database);
        AppendIf(sb, "Cloud/DevOps",         p.CloudDevOps);
        AppendIf(sb, "Messaging",            p.Messaging);
        AppendIf(sb, "Architecture style",   p.ArchitectureStyle);
        AppendIf(sb, "Testing framework",    p.TestingFramework);
        AppendIf(sb, "Notes",                p.CustomNotes);
    }

    private static void AppendAnswerSettings(StringBuilder sb, AnswerSettings s)
    {
        sb.AppendLine("\n# Answer requirements:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Length: {s.Length}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Complexity: {s.Complexity}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Style: {s.Style}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Tone: {s.Tone}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Format: {s.Format}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Output language: {s.OutputLanguage}");
    }

    private static void AppendIf(StringBuilder sb, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            sb.AppendLine(CultureInfo.InvariantCulture, $"- {label}: {value}");
    }

    private static int MapLengthToTokens(AnswerLength length) => length switch
    {
        AnswerLength.VeryShort    => 200,
        AnswerLength.ShortLength  => 400,
        AnswerLength.Medium       => 800,
        AnswerLength.Detailed     => 1500,
        AnswerLength.DeepDive     => 3000,
        _                         => 800
    };
}
