using System.Globalization;
using System.Text;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;

namespace AIHelperNET.Application.Answers;

/// <summary>Builds structured <see cref="AnswerPrompt"/> objects from session context.</summary>
public sealed class PromptBuilderService
{
    /// <summary>Constructs an <see cref="AnswerPrompt"/> from the given session context.</summary>
    /// <remarks>Delegates to <see cref="Build(CodeProfile, AnswerSettings, string, string?, IReadOnlyList{TranscriptItem}?, IReadOnlyList{ValueTuple{string,string}}?)"/> using <paramref name="question"/>.Text.</remarks>
    /// <param name="profile">Candidate's code profile used to tailor code examples.</param>
    /// <param name="settings">Answer settings controlling complexity, language, and length.</param>
    /// <param name="question">The detected question whose <c>Text</c> is forwarded.</param>
    /// <param name="screenContext">Optional OCR text captured from the screen.</param>
    /// <param name="recentTranscript">Optional recent transcript items to include as conversation context.</param>
    /// <param name="recentQA">Optional recent Q&amp;A pairs to include as conversation context. Answers are capped at 400 characters.</param>
    public static AnswerPrompt Build(
        CodeProfile profile,
        AnswerSettings settings,
        DetectedQuestion question,
        string? screenContext = null,
        IReadOnlyList<TranscriptItem>? recentTranscript = null,
        IReadOnlyList<(string Question, string Answer)>? recentQA = null)
        => Build(profile, settings, question.Text, screenContext, recentTranscript, recentQA);

    /// <summary>Constructs an <see cref="AnswerPrompt"/> using an explicit question text.</summary>
    /// <param name="profile">Candidate's code profile used to tailor code examples.</param>
    /// <param name="settings">Answer settings controlling complexity, language, and length.</param>
    /// <param name="questionText">The full question text to answer.</param>
    /// <param name="screenContext">Optional OCR text captured from the screen.</param>
    /// <param name="recentTranscript">Optional recent transcript items to include as conversation context.</param>
    /// <param name="recentQA">Optional recent Q&amp;A pairs to include as conversation context. Answers are capped at 400 characters.</param>
    public static AnswerPrompt Build(
        CodeProfile profile,
        AnswerSettings settings,
        string questionText,
        string? screenContext = null,
        IReadOnlyList<TranscriptItem>? recentTranscript = null,
        IReadOnlyList<(string Question, string Answer)>? recentQA = null)
    {
        var system = new StringBuilder();

        system.AppendLine(
            "You are a senior software engineer coaching a candidate through a technical job interview. " +
            "Your job is to give short, spoken-style answers the candidate can say out loud right now.");

        system.AppendLine();
        system.AppendLine("STRICT RULES:");
        system.AppendLine("1. Answer like an experienced engineer speaking — first person, spoken, direct, no filler.");
        system.AppendLine("2. Structure the answer as: (a) a 1–2 sentence definition or reframe to open; " +
            "(b) a first-person cue line (e.g. \"I would focus on:\") followed by terse \"- \" bullets; " +
            "(c) a single closing principle line.");
        AppendStructureGuidance(system, settings.Length);
        system.AppendLine("3. FORMATTING: use **bold** for emphasis and sub-labels, and \"- \" for bullets. " +
            "NO headers (no #, ##). Put any code in fenced ```language blocks.");
        system.AppendLine("4. CODE: include code ONLY when the question explicitly asks to write, " +
            "implement, fix, debug, show syntax, or provide a query/example. " +
            "For conceptual, design, 'what is', 'why', 'how does it work' questions — verbal answer only.");
        system.AppendLine("5. Start directly with the answer. Never say 'Great question' or restate the question.");
        system.AppendLine("6. Give the answer only — no 'why this is a good answer' commentary or meta-notes.");

        AppendCodeProfile(system, profile);

        if (settings.Complexity != AnswerComplexity.Balanced)
            system.AppendLine(CultureInfo.InvariantCulture,
                $"\nTarget level: {settings.Complexity}.");

        if (!string.IsNullOrWhiteSpace(settings.OutputLanguage) &&
            !settings.OutputLanguage.Equals("English", StringComparison.OrdinalIgnoreCase))
            system.AppendLine(CultureInfo.InvariantCulture,
                $"Answer in: {settings.OutputLanguage}.");

        var user = new StringBuilder();

        var hasTranscript = recentTranscript is { Count: > 0 };
        var hasQA         = recentQA         is { Count: > 0 };

        if (hasTranscript || hasQA)
        {
            user.AppendLine("Conversation context (recent discussion):");

            if (hasTranscript)
            {
                foreach (var item in recentTranscript!)
                {
                    var speaker = item.Speaker == Speaker.Me ? "Me" : "Interviewer";
                    user.AppendLine(CultureInfo.InvariantCulture,
                        $"[Transcript] {speaker}: {item.Text}");
                }
            }

            if (hasQA)
            {
                foreach (var (q, a) in recentQA!)
                {
                    var cappedAnswer = a.Length > 400 ? a[..400] + "…" : a;
                    user.AppendLine(CultureInfo.InvariantCulture,
                        $"[Q&A] Q: {q}  A: {cappedAnswer}");
                }
            }

            user.AppendLine();
        }

        user.AppendLine(CultureInfo.InvariantCulture, $"Question: {questionText}");
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
        system.AppendLine(SharedMarkdownRule);

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
        system.AppendLine(SharedMarkdownRule);

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
            MaxTokens: Math.Max(MapLengthToTokens(settings.Length), 500));
    }

    private static string ModeSystemPrompt(ScreenAnalysisMode mode) => mode switch
    {
        ScreenAnalysisMode.SolveCodingTask =>
            "You are a senior software engineer. Given the coding task shown on screen, " +
            "provide the solution FIRST (working code), then a brief explanation after. " +
            "Do not restate the task or repeat code from the screen.",
        ScreenAnalysisMode.DebugError =>
            "You are a senior software engineer. " +
            "State the root cause and fix FIRST, then explain why. Be concise. " +
            "Do not repeat the error message or stack trace.",
        ScreenAnalysisMode.ExplainCode =>
            "You are a senior software engineer. " +
            "State what the code does in one sentence FIRST, then explain patterns and notable decisions. " +
            "3–5 sentences total, spoken style. Do not repeat the code.",
        ScreenAnalysisMode.SystemDesign =>
            "You are a senior software engineer. " +
            "State the recommended approach FIRST, then cover components, data flow, and trade-offs. " +
            "Be concise. Do not restate the requirements.",
        ScreenAnalysisMode.MultipleChoice =>
            "You are a senior software engineer answering a multiple-choice question. " +
            "State the answer letter FIRST (e.g. 'Answer: A'), then explain why in one sentence. " +
            "Then one sentence per wrong option saying why it fails.\n" +
            "RULES — no exceptions:\n" +
            "- Only the DEFAULT output of each option as written matters. Ignore what is possible with extra syntax.\n" +
            "- Best-practice potential does NOT make an option correct.\n" +
            "IMPORTANT for SQL FOR XML with SELECT * and no column aliases:\n" +
            "  FOR XML RAW  → <row col=\"val\"/>  (attributes, element name = 'row') ✓ matches attribute format\n" +
            "  FOR XML AUTO → <TableName col=\"val\"/>  (attributes, element name = table name)\n" +
            "  FOR XML PATH → <row><col>val</col></row>  (child elements, NOT attributes)\n" +
            "Do not restate the question or options.",
        _ =>
            "You are a senior software engineer coaching a candidate through a technical interview. " +
            "Give the answer or conclusion FIRST, then explain. " +
            "Do not restate or repeat the on-screen content."
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

    private const string SharedMarkdownRule =
        "Formatting: use \"- \" for bullets and **bold** for emphasis; " +
        "put any code in fenced ```language blocks; no headers (#).";

    private static void AppendStructureGuidance(StringBuilder sb, AnswerLength length)
    {
        switch (length)
        {
            case AnswerLength.VeryShort:
            case AnswerLength.ShortLength:
                sb.AppendLine("   Keep it flat: the opening definition, then 4–6 terse bullets, then the principle. " +
                    "Do NOT group bullets under sub-labels and do NOT include a worked example.");
                break;
            case AnswerLength.Medium:
                sb.AppendLine("   Bullets may be grouped under **bold sub-labels:** when the topic has natural dimensions.");
                break;
            case AnswerLength.Detailed:
            case AnswerLength.DeepDive:
                sb.AppendLine("   Group bullets under **bold sub-labels:** for each dimension, and include one short " +
                    "concrete example (a brief scenario or ordered steps) before the closing principle.");
                break;
            default:
                break;
        }
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
