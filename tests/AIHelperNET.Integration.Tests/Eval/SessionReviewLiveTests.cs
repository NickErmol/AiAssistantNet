using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using AIHelperNET.Application.Reviews;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using AIHelperNET.Infrastructure.AI;
using AIHelperNET.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace AIHelperNET.Integration.Tests.Eval;

/// <summary>
/// Opt-in live eval for the post-session review report feature. Builds an in-memory
/// <see cref="Session"/> that simulates a short interview with three questions
/// (two detected, one missed), then calls the real Claude API via
/// <see cref="SessionReviewAnalyzer"/> and asserts the four required report sections
/// are present and semantically correct.
///
/// <para>Self-skips (passes trivially) when no Anthropic API key is stored in Windows
/// Credential Manager (target <c>AIHelperNET:ClaudeApiKey</c>), so CI and offline
/// runs stay green.</para>
/// </summary>
[Trait("Category", "LiveLlm")]
public class SessionReviewLiveTests(ITestOutputHelper output)
{
    // Session starts at Unix epoch for deterministic timestamps.
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    /// <summary>
    /// Required section headings, in the order the prompt demands.
    /// </summary>
    private static readonly string[] RequiredHeadings =
    [
        "## Questions Asked",
        "## Answer Grades",
        "## Detection Audit",
        "## Study Topics",
    ];

    [Fact]
    public async Task SessionReview_LiveReport_ContainsAllSectionsAndSemanticallySoundGrades()
    {
        // ── Guard: skip if no API key ──────────────────────────────────────────────
        var secrets = new WindowsCredentialSecretStore();
        if (!secrets.HasApiKey())
        {
            output.WriteLine("Skipped: no Claude API key in Windows Credential Manager " +
                "(target 'AIHelperNET:ClaudeApiKey').");
            return;
        }

        // ── Build in-memory session (no DB) ───────────────────────────────────────
        var session = BuildInterviewSession();

        // ── Build the review prompt ───────────────────────────────────────────────
        var prompt = SessionReviewPromptBuilder.Build(session);

        output.WriteLine("=== PROMPT USER MESSAGE (first 500 chars) ===");
        output.WriteLine(prompt.User.Length > 500 ? prompt.User[..500] + "..." : prompt.User);
        output.WriteLine("");

        // ── Call the real analyzer ────────────────────────────────────────────────
        var opts = new ClaudeOptions();                  // BaseUrl / Version from defaults
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        IOptions<ClaudeOptions> optionsWrapper = Options.Create(opts);

        var analyzer = new SessionReviewAnalyzer(http, secrets, optionsWrapper);
        var result = await analyzer.AnalyzeAsync(prompt, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(
            "the analyzer should succeed; errors: {0}",
            string.Join("; ", result.Errors.Select(e => e.Message)));

        var review = result.Value;
        var markdown = review.Markdown;

        output.WriteLine("=== REVIEW MARKDOWN ===");
        output.WriteLine(markdown);
        output.WriteLine("");
        output.WriteLine($"ModelUsed: {review.ModelUsed}");

        // ── Assertion 1: all four headings present ────────────────────────────────
        foreach (var heading in RequiredHeadings)
        {
            markdown.Should().Contain(heading,
                because: $"the prompt requires the exact heading '{heading}'");
        }

        // ── Assertion 2: each section is non-empty ────────────────────────────────
        AssertSectionsNonEmpty(markdown);

        // ── Assertion 3: HTTPS answer graded ≤ 3/5 ───────────────────────────────
        // The candidate deliberately gave a wrong answer about HTTPS (private-key myth).
        // We find lines in the Answer Grades section that mention "HTTPS" (case-insensitive)
        // and extract a digit score.  We parse "X/5" patterns first; if absent, we accept
        // any lone digit 1–5 on that line as the score.
        AssertHttpsGradeLow(markdown);

        // ── Assertion 4: Detection Audit mentions missed volatile question ─────────
        var auditSection = ExtractSection(markdown, "## Detection Audit");
        auditSection.ToLowerInvariant().Should().Contain("volatile",
            because: "Q3 ('volatile keyword') had no ConversationTurn, so it must appear " +
                     "as a missed detection in the audit section");

        // ── Assertion 5: ModelUsed matches Sonnet ────────────────────────────────
        review.ModelUsed.Should().Be("claude-sonnet-4-6",
            because: "SessionReviewPromptBuilder sets AnswerModel.Sonnet which resolves to claude-sonnet-4-6");
    }

    // ─── Session factory ──────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a synthetic session that exercises the three main detection scenarios:
    /// <list type="number">
    ///   <item>Q1 – abstract class vs interface → correct answer → detected</item>
    ///   <item>Q2 – HTTPS handshake → clearly WRONG answer → detected</item>
    ///   <item>Q3 – volatile keyword → brief answer → NOT detected (detector miss)</item>
    /// </list>
    /// A fragmented-turn ConversationTurn is also added for Q1 to exercise the
    /// fragments rendering path in <see cref="SessionReviewPromptBuilder"/>.
    /// Small-talk lines are interleaved for realism.
    /// </summary>
    private static Session BuildInterviewSession()
    {
        var session = Session.Create(
            AnswerSettings.Default, CodeProfile.Empty, Start).Value;

        var clock = Start;

        // ── Small-talk opener ─────────────────────────────────────────────────────
        AddTranscript(session, Speaker.Other, "Hi, welcome! Ready to get started?", ref clock, 5);
        AddTranscript(session, Speaker.Me,    "Yes, absolutely, thanks.",           ref clock, 3);

        // ── Q1: abstract class vs interface ──────────────────────────────────────
        AddTranscript(session, Speaker.Other,
            "What is the difference between an abstract class and an interface in C#?",
            ref clock, 6);

        // Correct candidate answer
        AddTranscript(session, Speaker.Me,
            "An abstract class can have state fields and method implementations, " +
            "while an interface only declares contracts. " +
            "In C# 8+ interfaces can have default implementations, but you still cannot " +
            "store instance fields in an interface. " +
            "A class can implement multiple interfaces but inherit from only one abstract class.",
            ref clock, 15);

        // Q1 is detected as a fragmented turn (two fragments merged)
        var q1 = DetectedQuestion.Create(
            "What is the difference between an abstract class and an interface in C#?",
            QuestionSource.Audio, clock);
        session.AddDetectedQuestion(q1).IsSuccess.Should().BeTrue();

        var turn1Result = session.StartCollectingTurn(
            q1.Id,
            "What is the difference between an abstract class",
            clock);
        turn1Result.IsSuccess.Should().BeTrue();
        var turn1 = turn1Result.Value;
        turn1.AddFragment("and an interface in C#?").IsSuccess.Should().BeTrue();
        turn1.CompleteQuestion().IsSuccess.Should().BeTrue();
        // Question text should now be the two fragments joined
        turn1.InitialQuestionText.Should().Contain("abstract class");

        // ── Interviewer small-talk ────────────────────────────────────────────────
        AddTranscript(session, Speaker.Other, "Good, nice explanation.", ref clock, 3);
        AddTranscript(session, Speaker.Me,    "Thank you.",              ref clock, 2);

        // ── Q2: HTTPS handshake — candidate gives clearly WRONG answer ────────────
        AddTranscript(session, Speaker.Other,
            "How does HTTPS establish a secure connection?",
            ref clock, 5);

        // Deliberately wrong answer (private-key myth)
        AddTranscript(session, Speaker.Me,
            "HTTPS encrypts with the server's private key which the browser downloads " +
            "and both sides just use that same private key for everything. " +
            "There is no handshake needed, the key is just shared directly.",
            ref clock, 12);

        var q2 = DetectedQuestion.Create(
            "How does HTTPS establish a secure connection?",
            QuestionSource.Audio, clock);
        session.AddDetectedQuestion(q2).IsSuccess.Should().BeTrue();

        var turn2 = session.AddConversationTurn(
            q2.Id, "How does HTTPS establish a secure connection?", clock).Value;
        _ = turn2; // Turn 2: simple non-fragmented detection

        // ── Interviewer small-talk ────────────────────────────────────────────────
        AddTranscript(session, Speaker.Other, "Okay, interesting.", ref clock, 3);

        // ── Q3: volatile keyword — no ConversationTurn (detector miss) ───────────
        AddTranscript(session, Speaker.Other,
            "What does the volatile keyword do in C#?",
            ref clock, 5);

        // Brief candidate answer — the detector misses this question entirely
        AddTranscript(session, Speaker.Me,
            "It prevents caching of the variable by the CPU, I think.",
            ref clock, 5);

        // NOTE: no DetectedQuestion and no ConversationTurn added for Q3 — this is
        // the intentional detector miss that the audit section must report.

        // ── Closing small-talk ────────────────────────────────────────────────────
        AddTranscript(session, Speaker.Other, "Alright, that's all for today, thanks.",
            ref clock, 4);
        AddTranscript(session, Speaker.Me,    "Thank you, great chatting with you.", ref clock, 3);

        session.Stop(clock).IsSuccess.Should().BeTrue();

        return session;
    }

    // ─── Assertion helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that every required section has non-whitespace content between its heading
    /// and the next heading (or end of document).
    /// </summary>
    private static void AssertSectionsNonEmpty(string markdown)
    {
        for (var i = 0; i < RequiredHeadings.Length; i++)
        {
            var sectionText = ExtractSection(markdown, RequiredHeadings[i]);
            sectionText.Trim().Should().NotBeEmpty(
                because: $"section '{RequiredHeadings[i]}' must contain text, not just the heading");
        }
    }

    /// <summary>
    /// Extracts the text body of a section (everything after the heading line until the
    /// next <c>## </c> heading or end of string).
    /// </summary>
    private static string ExtractSection(string markdown, string heading)
    {
        var headingIndex = markdown.IndexOf(heading, StringComparison.OrdinalIgnoreCase);
        if (headingIndex < 0)
            return string.Empty;

        var bodyStart = headingIndex + heading.Length;
        // Find the next ## heading after this one
        var nextHeading = markdown.IndexOf("\n## ", bodyStart, StringComparison.Ordinal);
        return nextHeading >= 0
            ? markdown[bodyStart..nextHeading]
            : markdown[bodyStart..];
    }

    /// <summary>
    /// Finds lines in the <c>## Answer Grades</c> section that mention "HTTPS"
    /// (case-insensitive) and asserts the grade is ≤ 3.
    ///
    /// <para>Parsing strategy (most-to-least specific):</para>
    /// <list type="number">
    ///   <item>Pattern <c>N/5</c> on a line mentioning "HTTPS" → use N.</item>
    ///   <item>Lone digit 1–5 on such a line (no /5) → use that digit.</item>
    ///   <item>If neither matches, assert that the word "1", "2", or "3" appears
    ///         somewhere in the grades section as a soft fallback.</item>
    /// </list>
    ///
    /// This approach is intentionally lenient about exact formatting while still
    /// catching a clearly wrong grade of 4 or 5 for a factually incorrect answer.
    /// </summary>
    private static void AssertHttpsGradeLow(string markdown)
    {
        var gradesSection = ExtractSection(markdown, "## Answer Grades");
        gradesSection.Should().NotBeEmpty(because: "## Answer Grades must have content");

        // Find lines in the grades section that mention HTTPS
        var httpsLines = gradesSection
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Contains("HTTPS", StringComparison.OrdinalIgnoreCase)
                     || l.Contains("https", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (httpsLines.Count == 0)
        {
            // No HTTPS-specific line found — look for it across a broader window
            // (grade and justification may span two lines; check the whole section
            //  for a low-score pattern within ~200 chars of "HTTPS")
            var httpsPos = gradesSection.IndexOf("HTTPS", StringComparison.OrdinalIgnoreCase);
            httpsPos.Should().BeGreaterThanOrEqualTo(0,
                because: "the HTTPS question should be graded somewhere in ## Answer Grades");

            var window = gradesSection[Math.Max(0, httpsPos - 50)..
                Math.Min(gradesSection.Length, httpsPos + 200)];
            // Must contain a grade of 1-3 within that window
            var anyLowGrade = Regex.IsMatch(window, @"[123]\s*/\s*5") ||
                              Regex.IsMatch(window, @"\b[123]\b");
            anyLowGrade.Should().BeTrue(
                because: $"the deliberately wrong HTTPS answer should receive a low grade (1–3); " +
                         $"window around 'HTTPS': «{window}»");
            return;
        }

        // We have at least one HTTPS-mentioning line — check each for a grade
        // We assert that at least one line has a grade ≤ 3.
        var gradeFound = false;
        var highestGrade = -1;

        foreach (var line in httpsLines)
        {
            // Try X/5 pattern first
            var slashFiveMatch = Regex.Match(line, @"(\d)\s*/\s*5");
            if (slashFiveMatch.Success)
            {
                var grade = int.Parse(slashFiveMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                highestGrade = Math.Max(highestGrade, grade);
                if (grade <= 3) gradeFound = true;
                continue;
            }

            // Try lone digit 1–5
            var loneDigitMatch = Regex.Match(line, @"\b([1-5])\b");
            if (loneDigitMatch.Success)
            {
                var grade = int.Parse(loneDigitMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                highestGrade = Math.Max(highestGrade, grade);
                if (grade <= 3) gradeFound = true;
            }
        }

        if (gradeFound)
            return; // at least one grade was ≤ 3 — assertion passes

        // If we parsed grades but all were > 3, that's a test failure
        if (highestGrade > 3)
        {
            highestGrade.Should().BeLessThanOrEqualTo(3,
                because: "the candidate's HTTPS answer contained factual errors (private-key myth, " +
                         $"no-handshake claim) and should score ≤ 3/5; lines: [{string.Join("; ", httpsLines)}]");
        }
        else
        {
            // Could not parse a grade at all — assert the word "incorrect" or "wrong" or
            // a score word is present near "HTTPS" as the fallback signal.
            var jointLines = string.Join(" ", httpsLines);
            var hasWeakSignal =
                jointLines.Contains("incorrect", StringComparison.OrdinalIgnoreCase) ||
                jointLines.Contains("wrong",     StringComparison.OrdinalIgnoreCase) ||
                jointLines.Contains("inaccurate", StringComparison.OrdinalIgnoreCase) ||
                jointLines.Contains("poor",       StringComparison.OrdinalIgnoreCase) ||
                jointLines.Contains("weak",       StringComparison.OrdinalIgnoreCase) ||
                jointLines.Contains("incomplete", StringComparison.OrdinalIgnoreCase);

            hasWeakSignal.Should().BeTrue(
                because: "even without a parseable numeric grade, the HTTPS answer lines should " +
                         $"contain a negative qualifier; lines: [{jointLines}]");
        }
    }

    // ─── Transcript helper ────────────────────────────────────────────────────────

    private static void AddTranscript(
        Session session, Speaker speaker, string text,
        ref DateTimeOffset clock, int addSeconds)
    {
        clock = clock.AddSeconds(addSeconds);
        var item = TranscriptItem.Create(speaker, text, clock, 0.90f);
        session.AddTranscriptItem(item).IsSuccess.Should().BeTrue();
    }
}
