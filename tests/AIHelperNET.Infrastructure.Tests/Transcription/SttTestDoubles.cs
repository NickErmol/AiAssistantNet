using System.Runtime.CompilerServices;
using AIHelperNET.Application.Abstractions;

namespace AIHelperNET.Infrastructure.Tests.Transcription;

/// <summary>ITranscriptionService driven by a delegate — lets each test script exact stream behavior.</summary>
public sealed class ScriptableStt(
    Func<IAsyncEnumerable<AudioFrame>, CancellationToken, IAsyncEnumerable<TranscriptSegment>> impl)
    : ITranscriptionService
{
    public IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> frames, TranscriptionOptions options, CancellationToken ct)
        => impl(frames, ct);
}

/// <summary>Records every frame it consumes, then completes without emitting segments.</summary>
public sealed class FrameRecordingStt : ITranscriptionService
{
    public List<AudioFrame> Consumed { get; } = [];

    public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> frames, TranscriptionOptions options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var f in frames.WithCancellation(ct)) Consumed.Add(f);
        yield break;
    }
}
