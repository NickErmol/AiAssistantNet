using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AIHelperNET.Infrastructure.Audio;

public static class Resampler
{
    public static float[] To16kMonoFloat(byte[] buffer, int bytesRecorded, WaveFormat sourceFormat)
    {
        using var raw = new RawSourceWaveStream(buffer, 0, bytesRecorded, sourceFormat);

        ISampleProvider samples = raw.ToSampleProvider();

        ISampleProvider mono = sourceFormat.Channels == 1
            ? samples
            : new StereoToMonoSampleProvider(samples);

        var resampled = sourceFormat.SampleRate == 16000
            ? mono
            : new WdlResamplingSampleProvider(mono, 16000);

        var outputLength = (int)(bytesRecorded / (double)sourceFormat.BlockAlign
                                 * 16000 / sourceFormat.SampleRate * 2) + 1024;

        var output = new float[outputLength];
        var read = resampled.Read(output, 0, outputLength);
        return output[..read];
    }
}
