---
description: Full E2E checklist: UI controls, session lifecycle, audio, screen capture, settings
---

# E2E Test

Run the full end-to-end verification suite for AIHelperNET. Always run from the `develop` branch
(or a feature branch rebased on it). Delete stale `sessions.db` after schema changes before running.

## Real-audio E2E (Tier C)

Automated tests that drive the real SessionRunner + real Whisper transcription over committed
TTS-generated WAV fixtures (Me = mic channel, Other = system channel), asserting per-speaker
transcripts and answer-card outcomes. Tests live in
`tests/AIHelperNET.Integration.Tests/E2E/RealAudioE2ETests.cs` (Trait `Category=RealAudio`).

- **Fixtures:** TTS-generated WAVs under `tests/AIHelperNET.Integration.Tests/Fixtures/audio/`
  (16 kHz mono + trailing silence so the Silero VAD flushes each window). Regenerate after editing
  `manifest.json`:
  `pwsh -File tools/generate-audio-fixtures.ps1`
- **Whisper runtime in tests:** pinned to CPU via a ModuleInitializer
  (`tests/AIHelperNET.Integration.Tests/E2E/WhisperRuntimeInit.cs`) — production keeps its Vulkan
  runtime. Real-audio tests use the Whisper **Small** model.
- **Fast run (default — excludes slow real-audio + live LLM):**
  `dotnet test --filter "Category!=RealAudio&Category!=LiveLlm"`
- **Real-audio scenarios (real Whisper, slow, needs the model present):**
  `dotnet test tests/AIHelperNET.Integration.Tests --filter "Category=RealAudio"`
- **Ollama live-answer smoke (self-skips if Ollama / the configured model is unavailable):**
  `dotnet test tests/AIHelperNET.Integration.Tests --filter "Category=LiveLlm"`
