using System.IO;
using System.Net.Http;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Infrastructure.Common;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace AIHelperNET.Infrastructure.Transcription;

public sealed class WhisperModelProvider : IAsyncDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Dictionary<WhisperModelSize, WhisperFactory> _factories = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Use CPU runtime exclusively. The Vulkan backend loads on machines where vulkan-1.dll is
    // present but may fail at CreateBuilder() time if no supported GPU is available, and
    // Whisper.net does not fall back automatically. CPU is reliable across all environments
    // (dev, CI, test runners). Add RuntimeLibrary.Vulkan at the front if GPU acceleration is
    // needed AND Vulkan drivers are confirmed working on the target machine.
    private static readonly List<RuntimeLibrary> RuntimeOrder = [RuntimeLibrary.Cpu];

    public WhisperModelProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
        RuntimeOptions.RuntimeLibraryOrder = RuntimeOrder;
    }

    public async Task<WhisperFactory> GetFactoryAsync(WhisperModelSize size, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_factories.TryGetValue(size, out var existing)) return existing;

            var path = ModelPath(size);
            if (!File.Exists(path))
                await DownloadAsync(size, path, ct);

            var factory = WhisperFactory.FromPath(path);
            _factories[size] = factory;
            return factory;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string ModelPath(WhisperModelSize size)
    {
        var name = size switch
        {
            WhisperModelSize.Tiny       => "ggml-tiny.bin",
            WhisperModelSize.Base       => "ggml-base.bin",
            WhisperModelSize.Small      => "ggml-small.bin",
            WhisperModelSize.Medium     => "ggml-medium.bin",
            WhisperModelSize.LargeTurbo => "ggml-large-v3-turbo.bin",
            WhisperModelSize.Large      => "ggml-large-v3.bin",
            _ => throw new ArgumentOutOfRangeException(nameof(size))
        };
        return Path.Combine(AppPaths.ModelsDir, name);
    }

    private async Task DownloadAsync(WhisperModelSize size, string targetPath, CancellationToken ct)
    {
        var ggmlType = size switch
        {
            WhisperModelSize.Tiny       => GgmlType.Tiny,
            WhisperModelSize.Base       => GgmlType.Base,
            WhisperModelSize.Small      => GgmlType.Small,
            WhisperModelSize.Medium     => GgmlType.Medium,
            WhisperModelSize.LargeTurbo => GgmlType.LargeV3Turbo,
            WhisperModelSize.Large      => GgmlType.LargeV3,
            _ => throw new ArgumentOutOfRangeException(nameof(size))
        };

        AppPaths.EnsureDirectoriesExist();
        using var httpClient = _httpClientFactory.CreateClient(nameof(WhisperModelProvider));
        await using var modelStream = await new WhisperGgmlDownloader(httpClient).GetGgmlModelAsync(ggmlType, cancellationToken: ct);
        await using var fileStream = File.Create(targetPath);
        await modelStream.CopyToAsync(fileStream, ct);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var f in _factories.Values) f.Dispose();
        _factories.Clear();
        _lock.Dispose();
        await ValueTask.CompletedTask;
    }
}
