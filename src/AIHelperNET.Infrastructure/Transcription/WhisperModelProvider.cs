using System.IO;
using System.Net.Http;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Infrastructure.Common;
using Serilog;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace AIHelperNET.Infrastructure.Transcription;

public sealed class WhisperModelProvider : IAsyncDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Dictionary<WhisperModelSize, WhisperFactory> _factories = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public WhisperModelProvider(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    public async Task<WhisperFactory> GetFactoryAsync(WhisperModelSize size, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_factories.TryGetValue(size, out var existing)) return existing;

            var path = ModelPath(size);
            if (!File.Exists(path))
                await DownloadAsync(size, path, ct);

            var factory = BuildFactoryWithFallback(path);
            _factories[size] = factory;
            return factory;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Creates a <see cref="WhisperFactory"/> for the given model path, falling back to the CPU
    /// runtime if the preferred GPU backend (Vulkan/CUDA) loads its native library successfully
    /// but then fails to build a processor — which happens on machines that have vulkan-1.dll
    /// present but no supported GPU.
    /// </summary>
    /// <remarks>
    /// Whisper.net's native-library loader is a one-shot mechanism: the first successful
    /// <c>FromPath</c> call commits a backend and caches it in <see cref="RuntimeOptions.LoadedLibrary"/>.
    /// Resetting <see cref="RuntimeOptions.LoadedLibrary"/> to <c>null</c> before the retry causes
    /// the loader to re-run its selection logic, picking the next candidate in
    /// <see cref="RuntimeOptions.RuntimeLibraryOrder"/>.  Because the CPU runtime ships as a
    /// managed P/Invoke wrapper that does not depend on the native DLL that Vulkan loaded, the
    /// reset unblocks CPU usage even within the same process.
    /// </remarks>
    private static WhisperFactory BuildFactoryWithFallback(string modelPath)
    {
        var factory = WhisperFactory.FromPath(modelPath);
        try
        {
            // Probe: attempt to build a processor to verify the GPU backend actually works.
            // On machines with vulkan-1.dll but no supported GPU, FromPath succeeds (the
            // Vulkan DLL loads) but Build() throws WhisperModelLoadException.
            using var probe = factory.CreateBuilder().Build();
            return factory;
        }
        catch (WhisperModelLoadException ex)
        {
            // GPU backend loaded but is not functional on this machine.
            // Reset the loader state and retry with CPU only.
            Log.Warning(ex,
                "Whisper GPU backend unavailable (LoadedLibrary={Backend}); falling back to CPU runtime.",
                RuntimeOptions.LoadedLibrary?.ToString() ?? "unknown");

            factory.Dispose();
            RuntimeOptions.LoadedLibrary        = null;
            RuntimeOptions.RuntimeLibraryOrder  = [RuntimeLibrary.Cpu];

            return WhisperFactory.FromPath(modelPath);
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
