using System.Runtime.CompilerServices;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Infrastructure.Transcription;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Serilog;

namespace AIHelperNET.Infrastructure.Audio;

public static class SileroVadDetector
{
    private const int ChunkSize = 512; // 32 ms at 16 kHz

    // Static shape arrays to satisfy CA1861 (no repeated inline array allocations).
    private static readonly int[]  InputShape = { 1, ChunkSize };
    private static readonly int[]  HcShape    = { 2, 1, 64 };
    private static readonly int[]  SrShape    = { 1 };
    private static readonly long[] SrData     = { 16000L };

    public static async IAsyncEnumerable<SpeechWindow> AccumulateSpeechWindows(
        IAsyncEnumerable<AudioFrame> frames,
        SileroModelProvider modelProvider,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var session  = await modelProvider.GetSessionAsync(ct);
        var acc      = new VadWindowAccumulator();
        var leftover = new List<float>();

        // LSTM state tensors — reset after each emitted window.
        var h = new float[2 * 1 * 64]; // [2,1,64]
        var c = new float[2 * 1 * 64]; // [2,1,64]

        int totalChunks = 0;

        await foreach (var frame in frames.WithCancellation(ct))
        {
            leftover.AddRange(frame.Samples);

            while (leftover.Count >= ChunkSize)
            {
                var chunk = leftover.Take(ChunkSize).ToArray();
                leftover.RemoveRange(0, ChunkSize);
                totalChunks++;

                var prob = RunInference(session, chunk, h, c);

                var window = acc.Feed(prob, chunk, frame.Speaker);
                if (window is not null)
                {
                    // Reset LSTM so the next window starts with a clean state.
                    Array.Clear(h);
                    Array.Clear(c);
                    Log.Information("SileroVAD: emitting SpeechWindow speaker={Speaker} samples={SampleCount}",
                        window.Speaker, window.Samples.Length);
                    yield return window;
                }
            }
        }

        // Flush any remaining buffered speech.
        var final = acc.Flush();
        if (final is not null)
        {
            Log.Information("SileroVAD: flushing final SpeechWindow speaker={Speaker} samples={SampleCount}",
                final.Speaker, final.Samples.Length);
            yield return final;
        }

        Log.Information("SileroVAD: done — totalChunks={TotalChunks}", totalChunks);
    }

    private static float RunInference(InferenceSession session, float[] chunk, float[] h, float[] c)
    {
        var inputTensor = new DenseTensor<float>(chunk, InputShape);
        var hTensor     = new DenseTensor<float>(h,    HcShape);
        var cTensor     = new DenseTensor<float>(c,    HcShape);
        var srTensor    = new DenseTensor<long>(SrData, SrShape);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", inputTensor),
            NamedOnnxValue.CreateFromTensor("sr",    srTensor),
            NamedOnnxValue.CreateFromTensor("h",     hTensor),
            NamedOnnxValue.CreateFromTensor("c",     cTensor),
        };

        using var results = session.Run(inputs);

        float prob = results.First(r => r.Name == "output").AsTensor<float>()[0, 0];
        results.First(r => r.Name == "hn").AsTensor<float>().ToArray().CopyTo(h, 0);
        results.First(r => r.Name == "cn").AsTensor<float>().ToArray().CopyTo(c, 0);
        return prob;
    }
}
