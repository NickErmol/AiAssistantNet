using System.IO;
using System.Net.Http;
using System.Security;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Infrastructure.AI;
using AIHelperNET.Infrastructure.Common;
using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace AIHelperNET.Integration.Tests.Eval;

public class BoundaryClassifierAiEvalTests(ITestOutputHelper output)
{
    private const string KeyEnvVar = "AIHELPER_AI_EVAL_KEY";

    [Fact]
    public async Task RealHaiku_OverCorpus_ProducesReport()
    {
        var apiKey = Environment.GetEnvironmentVariable(KeyEnvVar);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            output.WriteLine($"Skipped: set {KeyEnvVar} to an Anthropic API key to run the real-Haiku eval.");
            return; // CI path — no network, passes trivially
        }
        await RunEvalAsync(apiKey, CorpusLoader.Load(), "Real Haiku (QuestionBoundaryClassifier)", "ai-eval");
    }

    [Fact]
    public async Task RealHaiku_OverGarbled_ProducesReport()
    {
        var apiKey = Environment.GetEnvironmentVariable(KeyEnvVar);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            output.WriteLine($"Skipped: set {KeyEnvVar} to an Anthropic API key to run the garbled eval.");
            return;
        }
        // Report-only — NO accuracy/label assertion. The 2026-06-12 run showed there is no
        // reliable text-only "correct" label for garbled input: Haiku recovers intent from some
        // garble (e.g. mangled "schema for a movie booking system" -> TaskComplete, correct) and
        // mislabels others, INCLUDING emitting a fold label (g-rate-limit -> QuestionContinued)
        // with no way to know the transcript was garbage. That inconsistency is precisely why the
        // deterministic AsrConfidenceGate (which reads ASR confidence, not the words) is the real
        // protection — see AsrConfidenceGateTests + the pipeline drop tests. This corpus documents
        // the classifier's unreliability on degraded input; it is not a model-quality guard.
        await RunEvalAsync(apiKey, CorpusLoader.Load("boundary-garbled.json"),
            "Real Haiku — GARBLED (ASR degradation, report-only)", "ai-eval-garbled");
    }

    [Fact]
    public async Task RealHaiku_OverHoldout_ProducesReport()
    {
        var apiKey = Environment.GetEnvironmentVariable(KeyEnvVar);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            output.WriteLine($"Skipped: set {KeyEnvVar} to an Anthropic API key to run the held-out eval.");
            return;
        }
        await RunEvalAsync(apiKey, CorpusLoader.Load("boundary-holdout.json"),
            "Real Haiku — HELD-OUT (generalization)", "ai-eval-holdout", minAccuracy: 0.90);
    }

    private async Task RunEvalAsync(
        string apiKey, IReadOnlyList<CorpusEntry> corpus, string title, string reportPrefix,
        double minAccuracy = 0.0)
    {
        var classifier = new QuestionBoundaryClassifier(
            new HttpClient(),
            new EnvSecretStore(apiKey),
            Options.Create(new ClaudeOptions()));

        var matrix = new ConfusionMatrix();
        var failures = 0;
        var misses = new List<string>();

        foreach (var entry in corpus)
        {
            var recentItems = entry.RecentItems
                .Select(i => TranscriptItem.Create(i.Speaker, i.Text, DateTimeOffset.UnixEpoch, 1.0f))
                .ToList();
            var latest = TranscriptItem.Create(
                entry.LatestItem.Speaker, entry.LatestItem.Text, DateTimeOffset.UnixEpoch, 1.0f);

            try
            {
                var result = await classifier.ClassifyAsync(
                    entry.ActiveTurnStatus, recentItems, latest, entry.LatestItem.Speaker, CancellationToken.None);
                matrix.Record(entry.ExpectedLabel, result.Classification);
                if (result.Classification != entry.ExpectedLabel)
                    misses.Add($"{entry.Id}: expected {entry.ExpectedLabel}, got {result.Classification}");
            }
            catch (Exception ex)
            {
                failures++;
                output.WriteLine($"API failure for {entry.Id}: {ex.Message}");
            }
        }

        var report = EvalReport.ToText(matrix, title)
            + $"\nAPI failures: {failures}\nMisses:\n  " + string.Join("\n  ", misses);
        output.WriteLine(report);

        Directory.CreateDirectory(AppPaths.DiagnosticsDir);
        var reportPath = Path.Combine(AppPaths.DiagnosticsDir, $"{reportPrefix}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");
        await File.WriteAllTextAsync(reportPath, report);
        output.WriteLine($"Report written to {reportPath}");

        if (minAccuracy > 0.0)
            matrix.Accuracy.Should().BeGreaterThanOrEqualTo(minAccuracy,
                $"{title}: Haiku accuracy regressed below the guarded floor");
    }

    /// <summary>Minimal <see cref="ISecretStore"/> that yields a fixed key from the environment.</summary>
    private sealed class EnvSecretStore(string key) : ISecretStore
    {
        public Result<SecureString> GetApiKey()
        {
            var ss = new SecureString();
            foreach (var c in key) ss.AppendChar(c);
            ss.MakeReadOnly();
            return Result.Ok(ss);
        }

        public Result SaveApiKey(SecureString key) => Result.Ok();
        public Result DeleteApiKey() => Result.Ok();
        public bool HasApiKey() => true;
    }
}
