using System.IO;
using System.Text.Json;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Infrastructure.Diagnostics;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Diagnostics;

public class JsonlBoundaryDecisionRecorderTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "aih-diag-" + Guid.NewGuid().ToString("N"));

    private static BoundaryDecisionRecord Sample() => new(
        Timestamp: DateTimeOffset.UnixEpoch,
        SessionId: Guid.NewGuid(),
        TurnId: Guid.NewGuid(),
        Speaker: Speaker.Other,
        StaleTurnStatus: ConversationTurnStatus.PreliminaryReady,
        HeuristicLabel: BoundaryLabel.QuestionContinued,
        HeuristicConfidence: 0.30,
        AiLabel: BoundaryLabel.NewQuestion,
        AiConfidence: 0.80,
        Agreed: false,
        EffectiveConfidence: 0.40,
        Route: "AppendToActiveTurn",
        FinalLabel: BoundaryLabel.NewQuestion,
        TextClip: "how would you handle invalidation");

    [Fact]
    public void Record_WritesOneJsonLinePerCall()
    {
        var recorder = new JsonlBoundaryDecisionRecorder(_dir);

        recorder.Record(Sample());
        recorder.Record(Sample());

        var file = Directory.GetFiles(_dir, "boundary-decisions-*.jsonl").Single();
        var lines = File.ReadAllLines(file);
        lines.Should().HaveCount(2);

        using var doc = JsonDocument.Parse(lines[0]);
        doc.RootElement.GetProperty("FinalLabel").GetString().Should().Be("NewQuestion");
        doc.RootElement.GetProperty("Route").GetString().Should().Be("AppendToActiveTurn");
    }

    [Fact]
    public void Record_DoesNotThrow_WhenDirectoryPathIsInvalid()
    {
        var recorder = new JsonlBoundaryDecisionRecorder("\0:invalid");

        var act = () => recorder.Record(Sample());

        act.Should().NotThrow();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }
}
