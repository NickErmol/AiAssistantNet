using System.IO;
using System.Net.Http;
using System.Security;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Infrastructure.AI;
using AIHelperNET.Infrastructure.Common;
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
    public async Task RealHaiku_OverHoldout_ProducesReport()
    {
        var apiKey = Environment.GetEnvironmentVariable(KeyEnvVar);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            output.WriteLine($"Skipped: set {KeyEnvVar} to an Anthropic API key to run the held-out eval.");
            return;
        }
        await RunEvalAsync(apiKey, CorpusLoader.Load("boundary-holdout.json"),
            "Real Haiku — HELD-OUT (generalization)", "ai-eval-holdout");
    }

    private async Task RunEvalAsync(string apiKey, IReadOnlyList<CorpusEntry> corpus, string title, string reportPrefix)
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
