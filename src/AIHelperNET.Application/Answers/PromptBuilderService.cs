using System.Globalization;
using System.Text;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;

namespace AIHelperNET.Application.Answers;

/// <summary>Builds structured <see cref="AnswerPrompt"/> objects from session context.</summary>
public sealed class PromptBuilderService
{
    /// <summary>Constructs an <see cref="AnswerPrompt"/> from the given session context.</summary>
    public static AnswerPrompt Build(
        CodeProfile profile,
        AnswerSettings settings,
        DetectedQuestion question,
        string? screenContext = null)
    {
        var system = new StringBuilder();

        system.AppendLine(
            "You are a senior software engineer coaching a candidate through a technical job interview. " +
            "Your job is to give short, spoken-style answers the candidate can say out loud right now.");

        system.AppendLine();
        system.AppendLine("STRICT RULES:");
        system.AppendLine("1. Be concise. 3–5 sentences or 3–4 bullets max. No padding.");
        system.AppendLine("2. Answer like an experienced engineer speaking — clear, direct, no filler.");
        system.AppendLine("3. NO markdown headers (no #, ##). Use plain prose or short bullets.");
        system.AppendLine("4. CODE: include code ONLY when the question explicitly asks to write, " +
            "implement, fix, debug, show syntax, or provide a query/example. " +
            "For conceptual, design, 'what is', 'why', 'how does it work' questions — verbal answer only.");
        system.AppendLine("5. Start directly with the answer. Never say 'Great question' or restate the question.");

        AppendCodeProfile(system, profile);

        if (settings.Complexity != AnswerComplexity.Balanced)
            system.AppendLine(CultureInfo.InvariantCulture,
                $"\nTarget level: {settings.Complexity}.");

        if (!string.IsNullOrWhiteSpace(settings.OutputLanguage) &&
            !settings.OutputLanguage.Equals("English", StringComparison.OrdinalIgnoreCase))
            system.AppendLine(CultureInfo.InvariantCulture,
                $"Answer in: {settings.OutputLanguage}.");

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
        var fields = new[]
        {
            ("language", p.ProgrammingLanguage),
            ("backend",  p.BackendFramework),
            ("frontend", p.FrontendFramework),
            ("database", p.Database),
            ("cloud",    p.CloudDevOps),
            ("messaging",p.Messaging),
            ("architecture", p.ArchitectureStyle),
            ("testing",  p.TestingFramework),
        }.Where(f => !string.IsNullOrWhiteSpace(f.Item2)).ToList();

        if (fields.Count == 0) return;

        sb.AppendLine();
        sb.AppendLine("Candidate stack (use this in code examples only):");
        foreach (var (label, value) in fields)
            sb.AppendLine(CultureInfo.InvariantCulture, $"- {label}: {value}");

        if (!string.IsNullOrWhiteSpace(p.CustomNotes))
            sb.AppendLine(CultureInfo.InvariantCulture, $"- notes: {p.CustomNotes}");
    }

    private static int MapLengthToTokens(AnswerLength length) => length switch
    {
        AnswerLength.VeryShort    => 150,
        AnswerLength.ShortLength  => 300,
        AnswerLength.Medium       => 550,
        AnswerLength.Detailed     => 1000,
        AnswerLength.DeepDive     => 2000,
        _                         => 300
    };
}
