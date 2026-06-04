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

    /// <summary>Builds a follow-up prompt with the original Q+A injected as context.</summary>
    public static AnswerPrompt BuildFollowUp(
        CodeProfile profile,
        AnswerSettings settings,
        string originalQuestion,
        string previousAnswer,
        string followUpText)
    {
        var system = new StringBuilder();
        system.AppendLine(
            "You are a senior software engineer coaching a candidate through a technical job interview. " +
            "You previously answered a question. Now the candidate asks a follow-up. " +
            "Be concise — 2–4 sentences or bullets. No restating the prior answer.");
        AppendCodeProfile(system, profile);

        var user = new StringBuilder();
        user.AppendLine(CultureInfo.InvariantCulture, $"Original question: {originalQuestion}");
        user.AppendLine(CultureInfo.InvariantCulture, $"Your previous answer: {previousAnswer}");
        user.AppendLine(CultureInfo.InvariantCulture, $"Follow-up: {followUpText}");

        return new AnswerPrompt(
            System: system.ToString(),
            User: user.ToString(),
            OutputLanguage: settings.OutputLanguage,
            MaxTokens: MapLengthToTokens(settings.Length));
    }

    /// <summary>Builds a prompt for screen-based analysis with mode-specific instructions.</summary>
    public static AnswerPrompt BuildWithScreenMode(
        CodeProfile profile,
        AnswerSettings settings,
        string screenContext,
        IEnumerable<string> interviewerLines,
        ScreenAnalysisMode mode)
    {
        var system = new StringBuilder();
        system.AppendLine(ModeSystemPrompt(mode));
        AppendCodeProfile(system, profile);

        var user = new StringBuilder();
        var lines = interviewerLines.ToList();
        if (lines.Count > 0)
        {
            user.AppendLine("Interviewer context (recent speech):");
            foreach (var line in lines) user.AppendLine(CultureInfo.InvariantCulture, $"- {line}");
            user.AppendLine();
        }
        user.AppendLine("On-screen content (OCR):");
        user.AppendLine(screenContext);

        return new AnswerPrompt(
            System: system.ToString(),
            User: user.ToString(),
            OutputLanguage: settings.OutputLanguage,
            MaxTokens: MapLengthToTokens(settings.Length));
    }

    private static string ModeSystemPrompt(ScreenAnalysisMode mode) => mode switch
    {
        ScreenAnalysisMode.SolveCodingTask =>
            "You are a senior software engineer. Given the coding task shown on screen, provide a complete, " +
            "working solution in the candidate's stack. Include a brief explanation before the code.",
        ScreenAnalysisMode.DebugError =>
            "You are a senior software engineer. Analyze the error or stack trace shown on screen. " +
            "Identify the root cause and provide a clear fix. Be concise.",
        ScreenAnalysisMode.ExplainCode =>
            "You are a senior software engineer. Explain what the code on screen does, its design patterns, " +
            "and any notable decisions. 3–5 sentences, spoken style.",
        ScreenAnalysisMode.SystemDesign =>
            "You are a senior software engineer. Provide a high-level system design approach for the " +
            "requirements shown. Cover components, data flow, and key trade-offs. Be concise.",
        _ =>
            "You are a senior software engineer coaching a candidate through a technical interview. " +
            "Analyze the content on screen and provide a helpful, concise response the candidate can use."
    };

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
