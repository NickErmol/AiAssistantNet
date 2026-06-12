using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using Microsoft.Extensions.Options;
using Serilog;

namespace AIHelperNET.Infrastructure.AI;

/// <summary>
/// Classifies transcript boundary context using Claude Haiku via the Anthropic Messages API,
/// returning a structured <see cref="BoundaryClassificationResult"/>.
/// </summary>
public sealed class QuestionBoundaryClassifier(
    HttpClient http,
    ISecretStore secrets,
    IOptions<ClaudeOptions> options) : IQuestionBoundaryClassifier
{
    private const string HaikuModel = "claude-haiku-4-5-20251001";

    private const string SystemPrompt =
        """
        You are classifying one speech segment from a live technical interview to detect question boundaries.
        Return VALID JSON ONLY — no prose, no markdown, no code fences.

        You are given: active_turn_status (state of the question currently tracked, or null),
        speaker_of_latest (Other = interviewer, Me = candidate), up to 5 recent_items for context,
        and text_to_classify (the latest item). Classify text_to_classify.

        Labels — read these definitions carefully; the names alone are NOT enough:
        - QuestionComplete: text_to_classify is itself a complete, answerable QUESTION — a direct
          interrogative such as "what is dependency injection?", "how do the SOLID principles apply?",
          "when would you add an index?". A direct question is QuestionComplete, NOT QuestionStarted.
        - TaskComplete: text_to_classify is a complete, answerable imperative/coding TASK — an instruction
          verb such as "write...", "design...", "implement...", "explain...", "reverse a linked list".
          An imperative task is TaskComplete, NOT QuestionStarted.
        - QuestionStarted: ONLY an INCOMPLETE scenario setup not yet answerable on its own — "suppose we
          have a payment service...", "imagine thousands of IoT devices...". If the text is already a full
          question or task, use QuestionComplete/TaskComplete, never QuestionStarted.
        - QuestionContinued: the interviewer (Other) extends, refines, or adds a follow-up QUESTION on the
          SAME topic/scenario already in progress — even if phrased like a standalone question ("and how
          would it handle bursts?", "what about rolling back?"). Valid whether the turn is still being
          collected (CollectingQuestion) or already answered (PreliminaryReady). While still collecting
          (CollectingQuestion), even an "also ..." addition is QuestionContinued, not AdditionalRequirement.
          Default for an Other follow-up on the same subject.
        - AdditionalRequirement: the interviewer (Other) adds a new CONSTRAINT to a question that was already
          asked AND answered (active_turn_status = PreliminaryReady) — "also it must be idempotent", "keep it
          under 100ms", "assume three regions". If the turn is still being collected (CollectingQuestion), the
          same "also ..." phrasing is QuestionContinued instead.
        - ClarificationOfCurrentQuestion: the CANDIDATE (speaker_of_latest = Me) asks the interviewer to
          clarify the current question's scope ("do you mean reads or writes?"). Use ONLY when
          speaker_of_latest is Me. An Other-speaker follow-up is NEVER a clarification — it is
          QuestionContinued or AdditionalRequirement.
        - NewQuestion: the interviewer moves to a genuinely DIFFERENT topic the prior turn did not cover,
          usually flagged by an explicit shift marker ("moving on", "next question", "different topic",
          "let's switch gears").
        - Unrelated: social filler, acknowledgements, or logistics ("thanks for sharing", "can you hear me?",
          "give me a second to share my screen", "take your time"). Use Unrelated for filler, NOT NoQuestion.
        - NoQuestion: reserved for empty/meaningless audio; for human filler prefer Unrelated.

        Tie-breaker (avoids over-splitting one question into two cards):
        When active_turn_status is CollectingQuestion or PreliminaryReady (a live or just-answered turn) and
        text_to_classify stays on the SAME subject as recent_items, prefer QuestionContinued or
        AdditionalRequirement over NewQuestion. Choose
        NewQuestion only when the topic clearly changes OR there is an explicit topic-shift marker.

        Examples (input -> correct label):
        - latest:"what is dependency injection?" status:null -> QuestionComplete (a direct question, not a setup)
        - latest:"write a function to reverse a linked list" status:null -> TaskComplete (imperative task, not a setup)
        - latest:"suppose we're building an ecommerce checkout flow" status:null -> QuestionStarted (incomplete setup)
        - recent:["design a rate limiter for our gateway"] latest(Other):"and how would it handle sudden bursts" status:PreliminaryReady -> QuestionContinued (Other extends same topic; not a clarification, not new)
        - recent:["design the notification service"] latest(Other):"also it needs to stay under 100ms p99" status:PreliminaryReady -> AdditionalRequirement (new constraint on an answered task)
        - recent:["design a url shortener"] latest(Other):"also it should support custom aliases" status:CollectingQuestion -> QuestionContinued (still collecting, so an addition continues the question; not AdditionalRequirement)
        - recent:["how would you scale the database?"] latest(Me):"do you mean the read path or the write path?" status:CollectingQuestion -> ClarificationOfCurrentQuestion (candidate Me clarifies scope)
        - recent:["explain how dependency injection works"] latest(Other):"completely different topic, what's your experience with kubernetes?" status:PreliminaryReady -> NewQuestion (explicit shift + new topic)
        - latest:"give me a second to share my screen" status:null -> Unrelated (logistics filler)

        JSON schema (return exactly this shape):
        {"classification":"<one label above>","confidence":<0.0-1.0>,"normalized_text":"<trimmed text_to_classify>","reason":"<one short sentence>"}
        """;

    /// <inheritdoc/>
    public async Task<BoundaryClassificationResult> ClassifyAsync(
        ConversationTurnStatus? activeTurnStatus,
        IReadOnlyList<TranscriptItem> recentItems,
        TranscriptItem latestItem,
        Speaker speaker,
        CancellationToken ct)
    {
        var keyResult = secrets.GetApiKey();
        if (keyResult.IsFailed)
        {
            Log.Warning("QuestionBoundaryClassifier: no API key configured, returning Ambiguous");
            return BoundaryClassificationResult.Ambiguous(latestItem.Text);
        }

        var opts = options.Value;

        var activeTurnStatusStr = activeTurnStatus?.ToString() ?? "null";
        var recentItemsList = recentItems
            .TakeLast(5)
            .Select(item => new { speaker = item.Speaker.ToString(), text = item.Text })
            .ToList();
        var textToClassify = latestItem.Text;

        var userMessage = JsonSerializer.Serialize(new
        {
            active_turn_status = activeTurnStatusStr,
            speaker_of_latest = speaker.ToString(),
            recent_items = recentItemsList,
            text_to_classify = textToClassify
        });

        var body = JsonSerializer.Serialize(new
        {
            model = HaikuModel,
            max_tokens = 200,
            stream = false,
            system = SystemPrompt,
            messages = new[] { new { role = "user", content = userMessage } }
        });

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{opts.BaseUrl}/v1/messages");

        var apiKey = SecureStringToString(keyResult.Value);
        try
        {
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", opts.Version);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("QuestionBoundaryClassifier: API error {Status} — {Body}",
                    (int)response.StatusCode, json[..Math.Min(200, json.Length)]);
                return BoundaryClassificationResult.Ambiguous(latestItem.Text);
            }

            var result = ParseResponse(json, latestItem.Text);
            Log.Debug("QuestionBoundaryClassifier: {Label} ({Confidence:F2}) — {Reason}",
                result.Classification, result.Confidence, result.Reason);
            return result;
        }
        finally
        {
            // SecureStringToString already zeroes via ZeroFreeBSTR;
            // the managed string copy is GC-collected naturally.
            _ = apiKey.Length; // suppress unused-variable warning
        }
    }

    private static BoundaryClassificationResult ParseResponse(string json, string originalText)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var content = root.GetProperty("content")[0].GetProperty("text").GetString()?.Trim() ?? "";
            using var resultDoc = JsonDocument.Parse(StripCodeFence(content));
            var r = resultDoc.RootElement;
            var label = ParseLabel(r.GetProperty("classification").GetString() ?? "");
            var confidence = r.GetProperty("confidence").GetDouble();
            var normalizedText = r.TryGetProperty("normalized_text", out var nt) ? nt.GetString() ?? originalText : originalText;
            var reason = r.TryGetProperty("reason", out var rs) ? rs.GetString() ?? "" : "";
            return new BoundaryClassificationResult(
                label, confidence,
                label is BoundaryLabel.QuestionComplete or BoundaryLabel.TaskComplete or BoundaryLabel.NewQuestion,
                label is BoundaryLabel.AdditionalRequirement or BoundaryLabel.ClarificationOfCurrentQuestion,
                label is BoundaryLabel.QuestionStarted or BoundaryLabel.QuestionComplete or BoundaryLabel.TaskComplete or BoundaryLabel.NewQuestion,
                normalizedText, reason);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "QuestionBoundaryClassifier: failed to parse response");
            return BoundaryClassificationResult.Ambiguous(originalText);
        }
    }

    private static BoundaryLabel ParseLabel(string s) => s switch
    {
        "NoQuestion"                     => BoundaryLabel.NoQuestion,
        "QuestionStarted"                => BoundaryLabel.QuestionStarted,
        "QuestionContinued"              => BoundaryLabel.QuestionContinued,
        "QuestionComplete"               => BoundaryLabel.QuestionComplete,
        "TaskComplete"                   => BoundaryLabel.TaskComplete,
        "ClarificationOfCurrentQuestion" => BoundaryLabel.ClarificationOfCurrentQuestion,
        "AdditionalRequirement"          => BoundaryLabel.AdditionalRequirement,
        "NewQuestion"                    => BoundaryLabel.NewQuestion,
        "Unrelated"                      => BoundaryLabel.Unrelated,
        _                                => BoundaryLabel.NoQuestion
    };

    /// <summary>Strips a Markdown code fence (<c>```json … ```</c>) the model may wrap JSON in
    /// despite instructions, leaving the bare JSON for parsing.</summary>
    private static string StripCodeFence(string s)
    {
        s = s.Trim();
        if (!s.StartsWith("```", StringComparison.Ordinal)) return s;
        var firstNewline = s.IndexOf('\n');
        if (firstNewline >= 0) s = s[(firstNewline + 1)..];       // drop the ```/```json opener line
        if (s.EndsWith("```", StringComparison.Ordinal)) s = s[..^3];
        return s.Trim();
    }

    private static string SecureStringToString(SecureString ss)
    {
        var ptr = Marshal.SecureStringToBSTR(ss);
        try { return Marshal.PtrToStringBSTR(ptr) ?? string.Empty; }
        finally { Marshal.ZeroFreeBSTR(ptr); }
    }
}
