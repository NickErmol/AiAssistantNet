using AIHelperNET.Application.Abstractions;
using AIHelperNET.Infrastructure.Transcription.Deepgram;
using Serilog;

namespace AIHelperNET.Infrastructure.Transcription;

/// <summary>
/// Whisper resolves directly; Deepgram resolves to the resilient wrapper — or straight to
/// Whisper (with a notice) when no key is stored, so session start never blocks on a missing key.
/// Resolve() also clears any stale fallback notice from a previous session.
/// </summary>
public sealed class SttResolver(
    ITranscriptionService whisper,
    ITranscriptionService deepgram,
    ISecretStore secrets,
    IOverlayStatusNotifier? notifier) : ISttResolver
{
    /// <inheritdoc />
    public ITranscriptionService Resolve(SttProvider provider)
    {
        notifier?.Notify(string.Empty);

        if (provider == SttProvider.Whisper) return whisper;

        if (!secrets.HasApiKey(SecretKind.Deepgram))
        {
            Log.Warning("Deepgram selected but no API key stored — using local Whisper");
            notifier?.Notify("STT: using local Whisper (no Deepgram key)");
            return whisper;
        }

        return new ResilientTranscriptionService(deepgram, whisper, notifier);
    }
}
