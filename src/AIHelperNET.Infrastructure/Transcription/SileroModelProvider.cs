using System.IO;
using System.Net.Http;
using AIHelperNET.Infrastructure.Common;
using Microsoft.ML.OnnxRuntime;

namespace AIHelperNET.Infrastructure.Transcription;

public sealed class SileroModelProvider : IAsyncDisposable
{
    // Silero VAD v4 ONNX model from official GitHub release.
    private const string ModelUrl = "https://github.com/snakers4/silero-vad/raw/v4.0/files/silero_vad.onnx";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private InferenceSession? _session;

    public SileroModelProvider(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    public async Task<InferenceSession> GetSessionAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_session is not null) return _session;
            var path = Path.Combine(AppPaths.ModelsDir, "silero_vad.onnx");
            if (!File.Exists(path)) await DownloadAsync(path, ct);
            _session = new InferenceSession(path);
            return _session;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task DownloadAsync(string targetPath, CancellationToken ct)
    {
        AppPaths.EnsureDirectoriesExist();
        using var httpClient = _httpClientFactory.CreateClient(nameof(SileroModelProvider));
        using var response   = await httpClient.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var tmp = targetPath + ".tmp";
        try
        {
            await using var file = File.Create(tmp);
            await stream.CopyToAsync(file, ct);
            File.Move(tmp, targetPath, overwrite: true);
        }
        catch
        {
            File.Delete(tmp);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _session?.Dispose();
        _lock.Dispose();
        await ValueTask.CompletedTask;
    }
}
