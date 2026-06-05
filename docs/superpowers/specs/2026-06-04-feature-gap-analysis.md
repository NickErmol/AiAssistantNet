# Feature Gap Analysis: InterviewHelper vs AIHelperNET

**Date:** 2026-06-04  
**Analyst:** Claude Code (automated codebase survey)  
**Baseline:** `D:\work\AIHelperNET` (WPF/.NET 10, Onion+CQRS)  
**Reference:** `D:\work\InterviewHelper` (Electron 33 + Python/FastAPI backend)

---

## 1. Executive Summary

**AIHelperNET** is a stealth WPF overlay for live technical interviews. It captures mic and system audio in parallel, transcribes speech with local Whisper, detects questions, and streams AI-generated answers into an always-on-top semi-transparent panel. Its architecture is production-grade (DDD+CQRS+Clean) but the user-facing surface is narrow: one main overlay window, one settings dialog (API key only), and five global hotkeys.

**InterviewHelper** (internally "Live Context Assistant") is a functionally broader Electron + Python application targeting the same interview-assistance workflow. It exposes 21 sidebar navigation sections, configures every parameter through UI panels, supports four session states (start/pause/resume/stop), includes screen-capture with eight AI analysis modes, and layers in history, replay, statistics, notes, bookmarks, presets, answer ratings, clipboard history, full-text search, and a compact always-on-top strip overlay.

**Most important functional differences:**
- AIHelperNET has no history UI, export, or session browsing.
- AIHelperNET has no compact overlay strip (the main window is always full-size).
- AIHelperNET cannot pause/resume a session — only start/stop.
- AIHelperNET has no audio device selection UI (device IDs exist in the settings model but no panel to choose them).
- AIHelperNET has no screen capture analysis modes — only single on-demand OCR.
- AIHelperNET has no rebindable hotkeys, answer rating, follow-up questions, presets, or session notes.

**Confirmed Missing from AIHelperNET:** 22 features  
**Partially Available in AIHelperNET:** 8 features

**Top 5 most valuable additions:**

| Rank | Feature | Why |
|------|---------|-----|
| 1 | Audio device selection UI | Users cannot change mic/loopback without editing JSON; blocks core capture setup |
| 2 | Session history + export | Zero visibility into past sessions; no way to review or share output |
| 3 | Session pause/resume | No way to temporarily halt capture; requires full stop/restart |
| 4 | Compact overlay strip | Main window always occupies significant screen space; no lightweight mode |
| 5 | Answer rating + follow-up questions | No feedback loop; no way to continue a line of reasoning inside the app |

---

## 2. Application 1 — AIHelperNET: Confirmed User Capabilities

Source path: `D:\work\AIHelperNET`

| Category | Feature / Capability | What the User Can Do | Evidence in Project | Confidence |
|----------|---------------------|---------------------|---------------------|------------|
| Session | Start / Stop session | Toggle capture pipeline on/off with Ctrl+Shift+Space or button | `SessionControlViewModel.ToggleSessionCommand`; `MainOverlayWindow.xaml` toolbar | Confirmed |
| Session | Change capture mode during session | Switch AudioOnly / ScreenOnly / AudioAndScreen mid-session | `SessionControlViewModel.ChangeModeCommand`; sidebar radio buttons in `MainOverlayWindow.xaml` | Confirmed |
| Audio | Microphone capture | Capture user speech labeled as "Me" | `NAudioCaptureService`; `SessionRunner`; `Speaker.Me` | Confirmed |
| Audio | System audio (loopback) capture | Capture interviewer audio from system speakers labeled as "Other" | `NAudioCaptureService`; `Speaker.Other`; `LoopbackDeviceId` in `AppSettingsDto` | Confirmed |
| Audio | Both audio sources simultaneously | Run mic and loopback pipelines in parallel | `SessionRunner` dual-channel fan-out | Confirmed |
| Transcription | Real-time transcription | See live speech-to-text for both speakers as session runs | `WhisperTranscriptionService`; `TranscriptViewModel`; `MainOverlayWindow.xaml` transcript panel | Confirmed |
| Transcription | Speaker labeling | Transcript items show "Me" or "Other" with distinct colors | `TranscriptItemVm.SpeakerLabel`; `SpeakerColor` (`#FFAA44` / `#44AAFF`) | Confirmed |
| Transcription | Whisper model size selection | Choose Tiny/Base/Small/Medium/Large in settings model | `AppSettingsDto.WhisperModel`; `WhisperModelSize` enum | Partially Confirmed (model stored, but no settings UI panel to change it) |
| AI | Automatic question detection | System detects questions from transcript and creates turns | `QuestionDetector`; `TranscriptPipelineService` | Confirmed |
| AI | Streaming AI answer | See answer text appear token-by-token | `ClaudeAnswerProvider` / `OllamaAnswerProvider`; `IAnswerStreamSink`; `ConversationTurnViewModel.OnChunk` | Confirmed |
| AI | Answer versions | Preliminary → refined / screen-updated / manual regeneration with history | `AnswerVersionVm`; `TurnVm.AnswerVersions`; `GenerateAnswerCommand` version types | Confirmed |
| AI | Regenerate answer | Re-trigger answer generation for a turn | `ConversationTurnViewModel.RegenerateCommand` | Confirmed |
| AI | Copy answer to clipboard | Copy latest answer text with Ctrl+Shift+C or button | `ConversationTurnViewModel.CopyLatestCommand` | Confirmed |
| AI | Dismiss turn | Remove a detected question/answer from the list | `ConversationTurnViewModel.DismissCommand` | Confirmed |
| AI | Resolve turn | Mark turn as used and remove it | `ConversationTurnViewModel.ResolveCommand` | Confirmed |
| AI | Answer settings | Configure length, complexity, style, tone, format, output language | `AppSettingsDto.AnswerSettings`; `AnswerSettings` value object | Partially Confirmed (stored in settings model, UI binding unverified) |
| AI | Code profile settings | Configure tech stack for context-aware answers | `AppSettingsDto.CodeProfile`; `CodeProfile` value object | Partially Confirmed (stored in settings model, no confirmed UI) |
| AI | Claude backend | Use Anthropic Claude API for answers | `ClaudeAnswerProvider`; API key via Windows Credential Manager | Confirmed |
| AI | Ollama backend | Use local Ollama model for answers | `OllamaAnswerProvider`; `OllamaSharp` | Confirmed |
| Screen | On-demand screen OCR | Capture and OCR current screen to update answer context with Ctrl+Shift+S | `CaptureScreenCommand`; `ConversationTurnViewModel.CaptureScreenCommand` | Confirmed |
| Overlay | Always-on-top window | Overlay stays above all other windows | `MainOverlayWindow.xaml` `Topmost="True"` | Confirmed |
| Overlay | Semi-transparent window | Window shown at 0.75 opacity | `MainOverlayWindow.xaml` `Opacity="0.75"` | Confirmed |
| Overlay | Stealth / content protection | Window excluded from screen recordings | `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` via P/Invoke; toggle button in title bar | Confirmed |
| Overlay | Draggable title bar | Reposition window by dragging title area | `MainOverlayWindow.xaml.cs` drag handler | Confirmed |
| Overlay | Resizable window | Drag corner grip to resize; min 320×260 | `MainOverlayWindow.xaml` resize grip | Confirmed |
| Overlay | Collapsible sidebar | Show/hide left panel with mode/source selectors | `SessionControlViewModel.ToggleSidebarCommand` | Confirmed |
| Overlay | Minimize / restore | Minimize window; Ctrl+Shift+H to restore | Minimize button; global hotkey | Confirmed |
| Settings | API key management | Enter, save, or delete Claude API key | `SettingsWindow.xaml`; `SettingsViewModel`; `WindowsCredentialSecretStore` | Confirmed |
| Theme | Light / Dark theme toggle | Switch between dark and light color schemes | `DarkTheme.xaml`; `LightTheme.xaml`; theme toggle button in title bar | Confirmed |
| Hotkeys | 5 global keyboard shortcuts | Control session, screen capture, answer copy, overlay toggle from any app | `App.OnStartup` hotkey registration: Ctrl+Shift+Space/S/Q/C/H | Confirmed |
| Persistence | Session data persistence | Sessions, transcripts, turns, and answers saved to SQLite | `AppDbContext`; `SessionRepository`; `D:\AIHelperNET\` or `%LocalAppData%\AIHelperNET\` | Confirmed |

---

## 3. Application 2 — InterviewHelper: Confirmed User Capabilities

Source path: `D:\work\InterviewHelper`

| Category | Feature / Capability | What the User Can Do | Evidence in Project | Confidence |
|----------|---------------------|---------------------|---------------------|------------|
| Session | Start / Pause / Resume / Stop | Full session lifecycle with pause support | `SessionControls.ts`; `session.start/pause/resume/stop` WS commands; `SessionManager` in `websocket_server.py` | Confirmed |
| Session | Session status pill | See current state: Idle / Listening / Reading screen / Generating / Paused / Error | `Header.ts` status pill; `state.ts` `status` field | Confirmed |
| Audio | Microphone capture | Capture user speech as "me" speaker | `microphone_capture.py`; `AudioSettingsPanel.ts` | Confirmed |
| Audio | System audio (loopback) capture | Capture interviewer audio from speakers | `loopback_capture.py`; PyAudioWPatch WASAPI | Confirmed |
| Audio | Audio device selection UI | Choose specific mic and loopback device from enumerated list | `AudioSettingsPanel.ts` device dropdowns; backend device enumeration | Confirmed |
| Audio | Live audio level meters | See real-time mic and system audio level bars | `AudioSettingsPanel.ts` level bars; `audio.level.updated` WS events | Confirmed |
| Audio | Audio test buttons | Test mic / system audio capture with signal report (peak, RMS) | `AudioSettingsPanel.ts` test buttons; backend test handlers | Confirmed |
| Audio | Source mode selection UI | Toggle mic-only / system-only / both with UI control | `AudioSettingsPanel.ts` source mode selector | Confirmed |
| Transcription | faster-whisper with model selection UI | Choose Tiny→Large-v3 model from UI; 4× faster than reference Whisper | `AudioSettingsPanel.ts` model dropdown; `whisper_transcriber.py` | Confirmed |
| Transcription | Compute device selection | Choose CPU / CUDA / auto for transcription | `AudioSettingsPanel.ts` compute device selector | Confirmed |
| Transcription | Compute type selection | Choose int8 / float16 / float32 | `AudioSettingsPanel.ts` compute type selector | Confirmed |
| Transcription | Language selection | Set input language (auto, en, ru, pl, custom) | `AudioSettingsPanel.ts` language dropdown; `AppConfig.input_language` | Confirmed |
| Transcription | VAD toggle | Enable/disable voice activity detection to skip silence | `AppConfig.vad_filter`; audio settings | Confirmed |
| Transcription | Transcript search / filter | Search within current session transcript | `TranscriptPanel.ts` search input | Confirmed |
| Transcription | Transcript bookmarks | Bookmark specific transcript lines; click to scroll/highlight | `TranscriptPanel.ts`; `BookmarksPanel.ts` | Confirmed |
| AI | Question detection | Heuristic detection with confidence score and deduplication | `question_detector.py`; `transcript_deduplicator.py` | Confirmed |
| AI | Auto-generate answers toggle | Optionally auto-trigger answer on question detect | `SettingsPanel.ts` auto_generate checkbox; `AppConfig.auto_generate_answers` | Confirmed |
| AI | Streaming answer with confidence badge | See token-by-token answer with 1–5 confidence score | `AnswersPanel.ts`; `answer_generator.py` hedging scan | Confirmed |
| AI | Answer expand / collapse | Toggle full/truncated view per answer | `AnswersPanel.ts` expand/collapse | Confirmed |
| AI | Answer pin | Pin important answers to keep them prominent | `AnswersPanel.ts` pin button | Confirmed |
| AI | Answer rating (thumbs) | Rate each answer thumbs up or thumbs down | `AnswersPanel.ts` rating buttons; `answerRatings` in state | Confirmed |
| AI | Follow-up question | Ask a follow-up tied to a specific answer with context injection | `AnswersPanel.ts` follow-up input; `answer_generator.py` follow-up prompt | Confirmed |
| AI | Copy answer | Copy answer text to clipboard | `AnswersPanel.ts` copy button | Confirmed |
| AI | Answer settings UI | Configure length, complexity, style, tone, language, format through dedicated panel | `AnswerSettingsPanel.ts` | Confirmed |
| AI | Code / tech stack settings UI | Configure programming language, frameworks, DB, cloud, messaging, versions, custom notes | `CodeSettingsPanel.ts` | Confirmed |
| AI | Max tokens / temperature / system prompt suffix | Fine-tune generation parameters | `SettingsPanel.ts`; `AppConfig` | Confirmed |
| AI | Response format selection | Choose verbal-only / explanation+code / code-only / explanation+notes | `AnswerSettingsPanel.ts`; `AppConfig.response_format` | Confirmed |
| AI | Clipboard history | Browse last 10 copied answers with re-copy button | `ClipboardHistoryPanel.ts` | Confirmed |
| AI | Answer full-text search | Search all generated answers across all sessions | `AnswerSearchPanel.ts` | Confirmed |
| AI | Detached answer windows | Open specific answers in separate floating windows | `WindowsPanel.ts`; Electron multi-window | Confirmed |
| Screen | Screen capture with source selection | Choose monitor / window / region for capture | `ScreenReaderPanel.ts`; `screen_capture.py` | Confirmed |
| Screen | 8 AI screen analysis modes | Summarize / explain requirements / solve coding task / generate code / debug error / explain code / system design / documentation | `ScreenReaderPanel.ts` analysis mode selector; `screen_analyzer.py` | Confirmed |
| Screen | Periodic background screen capture | Automatically screenshot at configured interval with optional auto-analysis | `AppConfig.periodic_capture`; `SessionManager._periodic_capture_loop` | Confirmed |
| Screen | URL extraction from screenshots | Detect and list URLs found in captured screen content | `url_extractor.py`; `ScreenReaderPanel.ts` URLs display | Confirmed |
| Screen | Paste text / URL for analysis | Manually paste task text or URL to analyze as context | `ScreenReaderPanel.ts` paste text area | Confirmed |
| Screen | Screenshot preview | See thumbnail of captured screenshot inline | `ScreenshotPreview.ts` | Confirmed |
| Overlay | Compact overlay strip | Always-on-top single-line strip showing latest answer (120 chars); separate from main window | `overlay-compact.ts`; `OverlaySettingsPanel.ts` | Confirmed |
| Overlay | Compact overlay opacity control | Adjust strip transparency (20%–100%) | `OverlaySettingsPanel.ts` opacity slider; `AppConfig.overlay_opacity` | Confirmed |
| Overlay | Click-through toggle | Make overlay pass mouse events to windows beneath | `OverlaySettingsPanel.ts`; Electron `setIgnoreMouseEvents` | Confirmed |
| Overlay | Content protection toggle | User-controlled opt-in for screen-capture exclusion | `OverlaySettingsPanel.ts` content protection checkbox; default OFF | Confirmed |
| Overlay | Overlay monitor selection | Place overlay on primary / secondary monitor | `OverlaySettingsPanel.ts`; `AppConfig.overlay_monitor` | Confirmed |
| Hotkeys | 7 rebindable global hotkeys | Customize all shortcuts (session, capture, analyze, generate, copy, overlay, mode) | `HotkeysPanel.ts`; `AppConfig.hotkeys` | Confirmed |
| Hotkeys | Hotkey registration status | See which shortcuts are active / failed to register | `HotkeysPanel.ts` status indicators | Confirmed |
| History | Session history browser | List all past sessions with start time, mode, word count, tags | `HistoryPanel.ts`; `history_store.py` | Confirmed |
| History | Session search by tag / word | Filter session list | `HistoryPanel.ts` search input | Confirmed |
| History | Session tagging | Add comma-separated tags to any session | `HistoryPanel.ts` tag editor | Confirmed |
| History | Session notes | Add/edit freeform notes per session | `HistoryPanel.ts`; `NotesPanel.ts` | Confirmed |
| History | Session comparison | Compare two sessions side by side | `HistoryPanel.ts` compare mode | Confirmed |
| History | Session replay | Load a past session and play back transcript + answers with timeline | `ReplayPanel.ts`; `session_store.py` replay data | Confirmed |
| Export | Export transcript / answers | Export to TXT / MD / JSON for any session | `ExportPanel.ts`; `export_service.py` | Confirmed |
| Export | Auto-save | Session auto-saved to disk at configurable interval | `AppConfig.auto_save_on_stop`; `SessionManager._autosave_loop` | Confirmed |
| Statistics | Current session stats | Duration, transcript items, questions, answers, words/min | `StatsPanel.ts` | Confirmed |
| Statistics | Lifetime stats | Total sessions, words, answers across all time | `StatsPanel.ts` lifetime section | Confirmed |
| Statistics | Word frequency table | See most-used words in current session | `StatsPanel.ts` word frequency | Confirmed |
| Templates | Named setting templates | Save current settings as template; load / delete | `TemplatesPanel.ts` | Confirmed |
| Presets | Built-in presets | 6 pre-configured presets for common interview types | `PresetsPanel.ts` built-in presets | Confirmed |
| Whisper | Model downloader with progress | Download Whisper models directly from Hugging Face with size info and progress bar | `ModelDownloaderPanel.ts` | Confirmed |
| Onboarding | 4-step first-run wizard | Welcome → API key → model → confirmation | `OnboardingModal.ts` | Confirmed |
| Onboarding | Keyboard help modal | Hotkey reference shown on first run | `KeyboardHelpModal.ts` | Confirmed |
| Onboarding | Privacy consent banner | Ethics/privacy warning on first run | `app.ts` consent banner | Confirmed |
| Recovery | Draft recovery after crash | Offer to restore partial session after unexpected exit | `RecoveryModal.ts`; `draft_recovery_service.py` | Confirmed |
| Diagnostics | Logs / diagnostics panel | View backend + frontend logs, OCR status, device info, errors | `LogsPanel.ts` | Confirmed |
| Theme | Three-way theme | Dark / Light / High-contrast | `SettingsPanel.ts`; `AppConfig.theme` | Confirmed |
| Theme | Locale selection | Switch UI language (en / fr) | `SettingsPanel.ts`; `AppConfig.locale` | Confirmed |
| Notifications | Answer notifications with sound | Configurable notification on answer ready (with optional sound) | `AppConfig.notifications_*`; `SettingsPanel.ts` | Confirmed |
| Plugin | Plugin API | Extend the app with a custom JS plugin (`%APPDATA%\lca\plugin.js`) | `PLUGIN_API.md`; `window.__lcaPlugin` | Confirmed |

---

## 4. Full Comparison Matrix

| Category | Feature in InterviewHelper | Status in AIHelperNET | Difference | Evidence — InterviewHelper | Evidence — AIHelperNET | Priority |
|----------|--------------------------|-----------------------|------------|---------------------------|------------------------|----------|
| Session | Start session | Available | Both support it | `SessionControls.ts` | `ToggleSessionCommand` | — |
| Session | Stop session | Available | Both support it | `SessionControls.ts` | `ToggleSessionCommand` | — |
| Session | Pause / Resume session | Missing | AIHelperNET only has start/stop; no way to temporarily halt | `SessionControls.ts`; `session.pause/resume` | `ToggleSessionCommand` (start/stop only) | High |
| Session | Session status pill (5 states) | Partially Available | AIHelperNET shows "Listening"/"Stopped" in title bar; missing "Reading screen", "Generating", "Paused", "Error" states | `Header.ts` status pill | `MainOverlayWindow.xaml` title bar label | Medium |
| Audio | Microphone capture | Available | Both support it | `microphone_capture.py` | `NAudioCaptureService` | — |
| Audio | System audio (loopback) capture | Available | Both support it | `loopback_capture.py` | `NAudioCaptureService` | — |
| Audio | Both sources simultaneously | Available | Both support it | `AudioSettingsPanel.ts` both mode | `SessionRunner` dual-channel | — |
| Audio | Audio device selection UI | Missing | AIHelperNET stores `MicDeviceId`/`LoopbackDeviceId` in settings model but has no UI to enumerate or select devices | `AudioSettingsPanel.ts` device dropdowns | `AppSettingsDto.MicDeviceId` (no UI) | High |
| Audio | Live audio level meters | Missing | No real-time level display in AIHelperNET | `AudioSettingsPanel.ts` level bars | (not found) | Medium |
| Audio | Audio test buttons | Missing | No way to verify mic/loopback is working before session | `AudioSettingsPanel.ts` test mic/system buttons | (not found) | Medium |
| Audio | Source mode selection UI | Available | Both have UI mode selector in sidebar | `AudioSettingsPanel.ts` | Sidebar radio buttons in `MainOverlayWindow.xaml` | — |
| Transcription | Real-time transcription display | Available | Both show live transcript | `TranscriptPanel.ts` | `TranscriptViewModel`; `MainOverlayWindow.xaml` | — |
| Transcription | Speaker labeling (Me / Other) | Available | Both label by speaker | `TranscriptPanel.ts`; `Speaker.me/other_speaker` | `TranscriptItemVm.SpeakerLabel` | — |
| Transcription | Whisper model size selection UI | Partially Available | AIHelperNET stores model size but has no UI panel to change it | `AudioSettingsPanel.ts` model dropdown | `AppSettingsDto.WhisperModel` (no UI) | Medium |
| Transcription | Compute device / type selection | Missing | AIHelperNET uses CPU only; no compute device selector | `AudioSettingsPanel.ts` compute selectors | (not found) | Low |
| Transcription | Input language selection | Missing | AIHelperNET always uses Whisper auto-detect; no language selector | `AudioSettingsPanel.ts` language dropdown | (not found) | Medium |
| Transcription | VAD toggle | Available | Both use VAD; AIHelperNET does not expose the toggle | `AppConfig.vad_filter` | `VoiceActivityDetector` (always on) | Low |
| Transcription | Transcript search / filter | Missing | No search within current transcript in AIHelperNET | `TranscriptPanel.ts` search | (not found) | Medium |
| Transcription | Transcript bookmarks | Missing | No bookmarking of transcript lines | `TranscriptPanel.ts` bookmark; `BookmarksPanel.ts` | (not found) | Low |
| Transcription | Model downloader with progress | Missing | No in-app Whisper model download UI; AIHelperNET silently pre-downloads Base model | `ModelDownloaderPanel.ts` | `WhisperModelProvider` background download (no UI) | Medium |
| AI | Question detection | Available | Both auto-detect questions | `question_detector.py` | `QuestionDetector` | — |
| AI | Auto-generate answers toggle | Missing | AIHelperNET always auto-generates; no toggle | `SettingsPanel.ts` auto_generate | (not found; always on) | Medium |
| AI | Streaming AI answer | Available | Both stream tokens | `answer_generator.py`; WS `answer.token` | `IAnswerStreamSink`; `OnChunk` | — |
| AI | Answer confidence badge | Missing | No confidence indicator in AIHelperNET | `AnswersPanel.ts` confidence circles; `answer_generator.py` hedging scan | (not found) | Low |
| AI | Answer expand / collapse | Missing | AIHelperNET always shows full answer; no toggle | `AnswersPanel.ts` expand/collapse | (not found) | Low |
| AI | Answer pin | Missing | No answer pinning in AIHelperNET | `AnswersPanel.ts` pin | (not found) | Low |
| AI | Answer rating (thumbs) | Missing | No way to rate answers | `AnswersPanel.ts` thumbs up/down | (not found) | Medium |
| AI | Follow-up question | Missing | No in-app follow-up tied to an answer | `AnswersPanel.ts` follow-up input; `answer_generator.py` | (not found) | High |
| AI | Answer versions | Available | AIHelperNET actually has richer answer versioning (preliminary/refined/screen/manual) | `AnswerVersionVm`; version type enum | `AnswerVersionVm` with 4 types | — |
| AI | Copy answer | Available | Both support copy to clipboard | `AnswersPanel.ts` copy | `CopyLatestCommand` | — |
| AI | Dismiss / remove turn | Available | Both support it | `QuestionsPanel.ts` dismiss | `DismissCommand` | — |
| AI | Answer settings UI panel | Partially Available | AIHelperNET stores answer settings but no confirmed dedicated settings UI panel (only in SettingsWindow for API key) | `AnswerSettingsPanel.ts` full panel | `AppSettingsDto.AnswerSettings` (UI binding unconfirmed) | High |
| AI | Code / tech stack settings UI panel | Partially Available | Same — stored in model, UI binding unconfirmed | `CodeSettingsPanel.ts` full panel | `AppSettingsDto.CodeProfile` (UI binding unconfirmed) | High |
| AI | Response format selection | Available | Both support verbal/code/mixed formats (slightly different options) | `AnswerSettingsPanel.ts` format picker | `AnswerSettings.Format` | — |
| AI | Max tokens / temperature / system prompt suffix | Missing | AIHelperNET hard-codes token budget by length; no UI to adjust temperature or suffix | `SettingsPanel.ts`; `AppConfig` | `PromptBuilderService` (hardcoded values) | Medium |
| AI | Auto-generate on question detect | Missing | No toggle; AIHelperNET always auto-generates | `SettingsPanel.ts`; `AppConfig.auto_generate_answers` | (not found) | Medium |
| AI | Clipboard history | Missing | No clipboard history panel | `ClipboardHistoryPanel.ts` | (not found) | Low |
| AI | Answer full-text search | Missing | No cross-session answer search | `AnswerSearchPanel.ts` | (not found) | Medium |
| AI | Detached answer windows | Missing | No floating answer windows | `WindowsPanel.ts` | (not found) | Low |
| Screen | On-demand screen OCR | Available | Both support Ctrl+Shift+S OCR capture | `ScreenReaderPanel.ts`; `screen_capture.py` | `CaptureScreenCommand` | — |
| Screen | 8 AI screen analysis modes | Missing | AIHelperNET sends raw OCR text to answer re-generation; no distinct analysis mode selection | `ScreenReaderPanel.ts` 8 modes | `CaptureScreenCommand` (single mode) | High |
| Screen | Periodic background screen capture | Missing | No automatic periodic capture | `AppConfig.periodic_capture`; `_periodic_capture_loop` | (not found) | Medium |
| Screen | Capture source selection (monitor/window/region) | Missing | AIHelperNET always captures full screen; no source selector | `ScreenReaderPanel.ts` source selector | `CaptureScreenCommand` (full screen only) | Medium |
| Screen | URL extraction from screenshots | Missing | AIHelperNET extracts no URLs from screen content | `url_extractor.py` | (not found) | Low |
| Screen | Paste text / URL for analysis | Missing | No manual paste-to-analyze in AIHelperNET | `ScreenReaderPanel.ts` paste areas | (not found) | Medium |
| Screen | Screenshot preview | Missing | No screenshot thumbnail in UI | `ScreenshotPreview.ts` | (not found) | Low |
| Overlay | Main overlay window | Available | Both have always-on-top transparent overlay | `overlay.ts` | `MainOverlayWindow.xaml` | — |
| Overlay | Compact overlay strip | Missing | AIHelperNET has no lightweight single-line strip mode | `overlay-compact.ts` | (not found) | High |
| Overlay | Overlay opacity control | Partially Available | AIHelperNET has fixed 0.75 opacity; no UI slider | `OverlaySettingsPanel.ts` opacity slider | `Opacity="0.75"` hardcoded | Medium |
| Overlay | Click-through toggle | Missing | No click-through mode in AIHelperNET | `OverlaySettingsPanel.ts`; `setIgnoreMouseEvents` | (not found) | Medium |
| Overlay | Content protection toggle | Partially Available | AIHelperNET has stealth ON by default via title bar button; InterviewHelper is OFF by default, user opt-in | `OverlaySettingsPanel.ts` checkbox | `MainOverlayWindow.xaml` stealth button | Low |
| Overlay | Overlay monitor selection | Missing | AIHelperNET places overlay on current monitor only | `OverlaySettingsPanel.ts`; `AppConfig.overlay_monitor` | (not found) | Low |
| Hotkeys | Global hotkeys | Available | Both register global shortcuts | `main.ts` hotkey setup | `App.OnStartup` | — |
| Hotkeys | 7 hotkeys vs 5 | Partially Available | AIHelperNET missing Ctrl+Shift+A (analyze screen) and Ctrl+Shift+M (switch mode via hotkey) | `HotkeysPanel.ts` 7 actions | `App.OnStartup` 5 actions | Low |
| Hotkeys | Rebindable hotkeys UI | Missing | AIHelperNET hotkeys are hardcoded | `HotkeysPanel.ts` | `App.OnStartup` (hardcoded) | Medium |
| Hotkeys | Hotkey registration status | Missing | No feedback on which hotkeys are active | `HotkeysPanel.ts` status | (not found) | Low |
| History | Session history browser | Missing | No UI to browse past sessions | `HistoryPanel.ts` | (data in SQLite, no UI) | High |
| History | Session search / tagging / notes | Missing | No tagging or notes in AIHelperNET | `HistoryPanel.ts` | (not found) | Medium |
| History | Session comparison | Missing | No compare mode | `HistoryPanel.ts` compare | (not found) | Low |
| History | Session replay | Missing | No replay of past sessions | `ReplayPanel.ts` | (not found) | Low |
| Export | Export transcript / answers | Missing | No export in AIHelperNET | `ExportPanel.ts`; `export_service.py` | (not found) | High |
| Export | Auto-save | Available | AIHelperNET auto-persists to SQLite each turn; InterviewHelper saves JSON to disk | `AppConfig.auto_save_on_stop` | `SessionRepository`; `AppDbContext` | — |
| Statistics | Session stats | Missing | No stats panel | `StatsPanel.ts` | (not found) | Low |
| Statistics | Lifetime stats | Missing | No lifetime aggregates | `StatsPanel.ts` lifetime | (not found) | Low |
| Notes | Session-scoped notes | Missing | No notes panel | `NotesPanel.ts` | (not found) | Medium |
| Bookmarks | Transcript bookmarks | Missing | No bookmarks | `BookmarksPanel.ts` | (not found) | Low |
| Templates | Named setting templates | Missing | No presets or templates | `TemplatesPanel.ts`; `PresetsPanel.ts` | (not found) | Medium |
| Onboarding | First-run wizard | Missing | No onboarding in AIHelperNET | `OnboardingModal.ts` | (not found) | Medium |
| Onboarding | Keyboard help modal | Missing | No built-in hotkey reference | `KeyboardHelpModal.ts` | (not found) | Low |
| Onboarding | Privacy consent banner | Missing | No consent UI | `app.ts` consent | (not found) | Low |
| Recovery | Draft recovery after crash | Missing | No crash recovery | `RecoveryModal.ts`; `draft_recovery_service.py` | (not found) | Medium |
| Diagnostics | Logs / diagnostics panel | Missing | No in-app log viewer | `LogsPanel.ts` | (not found) | Low |
| Theme | High-contrast theme | Missing | AIHelperNET has dark/light only | `SettingsPanel.ts` high-contrast | `DarkTheme.xaml`; `LightTheme.xaml` | Low |
| Theme | UI locale selection | Missing | No locale/language switcher in AIHelperNET | `SettingsPanel.ts` locale | (not found) | Low |
| Notifications | Answer-ready notification with sound | Missing | No notification system | `AppConfig.notifications_*` | (not found) | Low |
| Plugin | Plugin / extension API | Missing | No plugin API in AIHelperNET | `PLUGIN_API.md`; `window.__lcaPlugin` | (not found) | Low |

---

## 5. Features Available in InterviewHelper but Missing from AIHelperNET

### Audio Device Selection UI

- **Category:** Audio
- **Status in AIHelperNET:** Missing
- **Priority:** High
- **What the user can do in InterviewHelper:** Open the Audio settings panel and choose specific microphone and system audio loopback devices from a dropdown populated by the OS device list. The panel also shows live audio level bars for both inputs.
- **Current state in AIHelperNET:** `AppSettingsDto` stores `MicDeviceId` and `LoopbackDeviceId` as optional strings, but there is no UI panel to enumerate available devices or switch between them. The user cannot select their headset versus built-in mic without editing JSON.
- **User value:** Essential for users with multiple audio devices (headset + webcam mic, or HDMI audio loopback vs USB audio adapter). Without device selection, capture may silently use the wrong device.
- **Possible addition to AIHelperNET:** Add a "Device" section to the existing sidebar or a dedicated Audio tab in `SettingsWindow`. Enumerate available wasapi devices via `NAudio.CoreAudioApi.MMDeviceEnumerator`, bind to dropdowns for mic and loopback, and persist the selection through `ISettingsStore`.
- **Evidence from InterviewHelper:** `AudioSettingsPanel.ts` — device dropdown UI; `audio_manager.py` — `get_input_devices()` / `get_loopback_devices()` methods; WS event `audio.devices.updated`
- **Evidence from AIHelperNET:** `AppSettingsDto.MicDeviceId`, `AppSettingsDto.LoopbackDeviceId` (`Application/Sessions/Dtos/AppSettingsDto.cs`); `NAudioCaptureService` accepts device IDs but exposes no enumeration to UI
- **Confidence:** Confirmed

---

### Session History Browser and Export

- **Category:** History / Export
- **Status in AIHelperNET:** Missing
- **Priority:** High
- **What the user can do in InterviewHelper:** Open the History panel to see a searchable list of all past sessions with timestamp, mode, word count, tags, and notes. Sessions can be tagged and annotated. Answers and transcripts can be exported as TXT, Markdown, or JSON from the Export panel.
- **Current state in AIHelperNET:** All session data (transcripts, turns, answers) is persisted to SQLite via EF Core. There is no UI to browse, search, or export any of it. Past sessions are inaccessible after the app restarts.
- **User value:** Interview candidates want to review answers they received, share transcripts, and improve prompts based on past sessions. Without history, the app loses all coaching value after each session ends.
- **Possible addition to AIHelperNET:** Add a History page accessible from the sidebar showing sessions from `GetSessionHistoryQuery`. Add an export handler that serializes `TranscriptItem` and `ConversationTurn` collections to file formats.
- **Evidence from InterviewHelper:** `HistoryPanel.ts`; `ExportPanel.ts`; `export_service.py` (TXT/MD/JSON exporters); `history_store.py` SQLite FTS search
- **Evidence from AIHelperNET:** `GetSessionHistoryQuery` handler exists in Application layer; `SessionRepository`; `AppDbContext` — all data stored but no UI entry point
- **Confidence:** Confirmed

---

### Session Pause / Resume

- **Category:** Session
- **Status in AIHelperNET:** Missing
- **Priority:** High
- **What the user can do in InterviewHelper:** Press Pause during a session to halt audio capture and transcription temporarily (e.g., for a break or to focus on a question). Press Resume to continue from the same session without losing accumulated transcript data.
- **Current state in AIHelperNET:** `ToggleSessionCommand` only starts a new session or stops the current one. Stopping ends the session aggregate permanently (`StopSessionCommand` sets `EndedAt`). There is no pause/resume state in the domain model.
- **User value:** During long interviews, candidates may need to mute capture for 1–2 minutes (bio break, side conversation). Without pause, they must stop and restart, losing session continuity.
- **Possible addition to AIHelperNET:** Add a `Paused` state to the `Session` aggregate, a `PauseSessionCommand` and `ResumeSessionCommand`, and update `SessionRunner` to gate the audio pipeline on the paused flag. Expose a Pause button in the overlay title bar.
- **Evidence from InterviewHelper:** `SessionControls.ts` Pause/Resume buttons; `SessionManager.pause_session()` / `resume_session()` in `websocket_server.py`; `AppState.status` includes `"paused"`
- **Evidence from AIHelperNET:** `Session` aggregate has `Active` and `Stopped` states only (`Domain/Sessions/Session.cs`); `ToggleSessionCommand` handler
- **Confidence:** Confirmed

---

### Follow-Up Question Per Answer

- **Category:** AI
- **Status in AIHelperNET:** Missing
- **Priority:** High
- **What the user can do in InterviewHelper:** After receiving an answer, type a follow-up question in an input field attached to that answer card. The follow-up is sent with the original question + answer as context, producing a refined continuation.
- **Current state in AIHelperNET:** The only way to get another answer is via `RegenerateCommand` (no user text) or `CaptureScreenCommand` (adds OCR context). There is no inline text input for a follow-up question tied to a specific turn.
- **User value:** Interviewers often ask clarifying follow-ups ("Can you elaborate on X?"). The candidate needs to ask the AI for more depth without losing the original answer context.
- **Possible addition to AIHelperNET:** Add a text input to each `TurnVm` card in `MainOverlayWindow`. On submit, send a new `GenerateAnswerCommand` with the original question + current answer injected into the prompt alongside the follow-up text.
- **Evidence from InterviewHelper:** `AnswersPanel.ts` follow-up `<input>` per answer card; `answer_generator.py` follow-up prompt construction with `original_question + answer + followup_text` context
- **Evidence from AIHelperNET:** `ConversationTurnViewModel` has no follow-up input; `GenerateAnswerCommand` takes question text only
- **Confidence:** Confirmed

---

### Answer Settings and Code Profile UI Panels

- **Category:** AI / Settings
- **Status in AIHelperNET:** Partially Available
- **Priority:** High
- **What the user can do in InterviewHelper:** Open the "Answer Settings" panel and adjust length (very short → deep dive), complexity (simple → senior), style (7 options: natural, interview, technical, step-by-step, code-first, architecture, debugging), tone, output language, and response format — all from a dedicated sidebar panel visible during a session. Separately, the "Code & Technology" panel lets the user configure their full tech stack, framework versions, and custom notes.
- **Current state in AIHelperNET:** `AnswerSettings` and `CodeProfile` value objects exist in the domain and are passed to `PromptBuilderService`. However, only `SettingsWindow` is confirmed in the UI, and it only exposes API key management. Whether any UI actually binds to `AnswerSettings` / `CodeProfile` at runtime is unconfirmed.
- **User value:** Being able to adjust answer style mid-interview (e.g., switch to "code-first" when a coding round starts) dramatically improves answer relevance without restarting the session.
- **Possible addition to AIHelperNET:** Add sidebar panels in `MainOverlayWindow` for answer settings and code profile, binding to commands that call `UpdateSettingsCommand`. The data model already exists; only the UI binding is missing.
- **Evidence from InterviewHelper:** `AnswerSettingsPanel.ts`; `CodeSettingsPanel.ts`
- **Evidence from AIHelperNET:** `AppSettingsDto.AnswerSettings`, `AppSettingsDto.CodeProfile` (`Application/Sessions/Dtos/`); `SettingsWindow.xaml` (API key only); no XAML binding to `AnswerSettings` found
- **Confidence:** Partially Confirmed

---

### Compact Overlay Strip

- **Category:** Overlay
- **Status in AIHelperNET:** Missing
- **Priority:** High
- **What the user can do in InterviewHelper:** Enable a compact overlay — a narrow, single-line strip that floats above all windows and shows only the latest answer text (first 120 characters). It has configurable opacity, click-through mode, and always-on-top. The compact overlay is separate from the main window, so the user can minimize the main window and keep only the strip visible during screen sharing.
- **Current state in AIHelperNET:** The main `MainOverlayWindow` is always the overlay. It cannot be reduced to a compact strip. Minimizing it hides the overlay entirely.
- **User value:** During screen sharing the full overlay is too large and conspicuous. A narrow strip is much harder to notice and still delivers the answer glance-ably.
- **Possible addition to AIHelperNET:** Create a separate `CompactOverlayWindow` (a frameless, narrow WPF window) that subscribes to `IAnswerStreamSink` and shows only the latest answer line. Add a toggle button or hotkey to show/hide it.
- **Evidence from InterviewHelper:** `overlay-compact.ts` (Electron window renderer); `OverlaySettingsPanel.ts` compact overlay section; `AppConfig.compact_overlay_enabled`
- **Evidence from AIHelperNET:** Only `MainOverlayWindow.xaml` exists; no compact window file found
- **Confidence:** Confirmed

---

### Screen Analysis Modes (8 vs 1)

- **Category:** Screen Capture
- **Status in AIHelperNET:** Partially Available
- **Priority:** High
- **What the user can do in InterviewHelper:** Open the Screen Reader panel and choose from 8 analysis modes before triggering a capture: Summarize content, Explain requirements, Solve coding task, Generate code, Debug error, Explain code, System design analysis, or Documentation explanation. The mode shapes the AI prompt and response style for that capture.
- **Current state in AIHelperNET:** `CaptureScreenCommand` captures the screen and passes raw OCR text to `GenerateAnswerCommand` with a single implicit mode (answer the question using screen context). There is no mode selector.
- **User value:** A coding task screenshot needs "Generate code" mode; a stack trace needs "Debug error" mode. Using a single generic mode produces worse answers for all non-generic screen content.
- **Possible addition to AIHelperNET:** Add a mode selector (dropdown or radio group) to the sidebar or title bar actions. Pass the selected mode to `PromptBuilderService` to choose the appropriate system prompt template for screen analysis.
- **Evidence from InterviewHelper:** `ScreenReaderPanel.ts` analysis mode selector with 8 options; `screen_analyzer.py` `analyze()` method dispatches prompt by mode; `session.analyze_screen` WS command with `analysis_mode` parameter
- **Evidence from AIHelperNET:** `CaptureScreenCommand` — single code path; `PromptBuilderService` — no screen analysis mode parameter
- **Confidence:** Confirmed

---

### Rebindable Global Hotkeys UI

- **Category:** Hotkeys
- **Status in AIHelperNET:** Missing
- **Priority:** Medium
- **What the user can do in InterviewHelper:** Open the Hotkeys panel and re-assign any of the 7 global shortcuts to a different key combination. The panel shows registration status (active / failed) for each shortcut.
- **Current state in AIHelperNET:** All 5 hotkeys are hardcoded in `App.OnStartup`. There is no UI to change them and no feedback on whether registration succeeded.
- **User value:** Some users have conflicting shortcuts from other apps (IDEs, screen recorders). Hardcoded shortcuts cause silent conflicts with no recourse.
- **Possible addition to AIHelperNET:** Add a Hotkeys section to `SettingsWindow` (or a new Settings tab) allowing the user to record a new key combo per action. Store mappings via `ISettingsStore` and re-register on settings change.
- **Evidence from InterviewHelper:** `HotkeysPanel.ts` — per-action key input with record button and registration status; `AppConfig.hotkeys` map
- **Evidence from AIHelperNET:** `App.OnStartup` hardcoded registrations (Ctrl+Shift+Space/S/Q/C/H); no settings property for hotkey mappings
- **Confidence:** Confirmed

---

### Audio Level Meters and Test Buttons

- **Category:** Audio
- **Status in AIHelperNET:** Missing
- **Priority:** Medium
- **What the user can do in InterviewHelper:** See real-time animated level bars for microphone and system audio inputs while configuring devices or during a session. Press "Test microphone" or "Test system audio" to verify the device is working (reports peak level, RMS, sample count).
- **Current state in AIHelperNET:** No audio level display exists in the UI. Users cannot verify audio capture is working without starting a full session and watching for transcript items.
- **User value:** Users frequently encounter silent capture (wrong device, muted input, WASAPI permission) with no feedback. Level meters and test buttons prevent wasted sessions.
- **Possible addition to AIHelperNET:** Subscribe to an audio level event from `IAudioCaptureService` and display animated level bars in the sidebar. Add a test-capture button that runs for 3 seconds and reports whether signal was detected.
- **Evidence from InterviewHelper:** `AudioSettingsPanel.ts` level bar elements; `audio.level.updated` WS broadcast from `_broadcast_levels()` in `SessionManager`; test button handlers
- **Evidence from AIHelperNET:** `NAudioCaptureService` captures raw PCM but emits no level metrics to UI; no level display in `MainOverlayWindow.xaml`
- **Confidence:** Confirmed

---

### Whisper Model Downloader UI

- **Category:** Transcription
- **Status in AIHelperNET:** Missing
- **Priority:** Medium
- **What the user can do in InterviewHelper:** Open the Model Downloader panel and see all available Whisper models (tiny → large-v3) with their download size, current status (not downloaded / downloading / ready), and a progress bar. Download or cancel a model with a button. Use the downloaded model immediately.
- **Current state in AIHelperNET:** `WhisperModelProvider` downloads the Base model silently in the background on first launch. The user has no visibility into download progress, no ability to choose which model to download proactively, and no UI feedback that a download is occurring.
- **User value:** Users on slower connections may not realize the app is downloading a model (or failed). Larger models (Small, Medium) give significantly better transcription accuracy for non-native English speakers.
- **Possible addition to AIHelperNET:** Add a model management section to `SettingsWindow` that lists model sizes with file size, download status, and a download/delete button. Report download progress via a progress bar bound to a ViewModel.
- **Evidence from InterviewHelper:** `ModelDownloaderPanel.ts` — model list with size, status, download button, progress bar; Hugging Face Hub download in `whisper_transcriber.py`
- **Evidence from AIHelperNET:** `WhisperModelProvider` (`Infrastructure/Transcription/`) — downloads quietly; `AppSettingsDto.WhisperModel` stored but no download UI
- **Confidence:** Confirmed

---

### Input Language Selection

- **Category:** Transcription
- **Status in AIHelperNET:** Missing
- **Priority:** Medium
- **What the user can do in InterviewHelper:** Select the interview language (auto, English, Russian, Polish, or a custom code) from the Audio settings panel. This is passed to faster-whisper to constrain the language model and improve accuracy for non-English speakers.
- **Current state in AIHelperNET:** Whisper.NET runs with auto language detection. No language preference is stored or exposed. Users speaking in Russian or Polish may get poor accuracy.
- **User value:** Whisper auto-detection can fail or add latency for non-English interviews. Forcing the language eliminates guessing and improves transcription speed.
- **Possible addition to AIHelperNET:** Add a language dropdown to the Audio section of settings, map it to a `WhisperLanguage` setting stored in `AppSettingsDto`, and pass it to `WhisperTranscriptionService`.
- **Evidence from InterviewHelper:** `AudioSettingsPanel.ts` language dropdown; `AppConfig.input_language`; `whisper_transcriber.py` passes `language` param to faster-whisper
- **Evidence from AIHelperNET:** `WhisperTranscriptionService` — no language parameter; `AppSettingsDto` — no language field
- **Confidence:** Confirmed

---

### Overlay Opacity Control

- **Category:** Overlay
- **Status in AIHelperNET:** Partially Available
- **Priority:** Medium
- **What the user can do in InterviewHelper:** Drag an opacity slider in the Overlay Settings panel to set overlay transparency from 20% to 100%, allowing fine-tuned balance between readability and screen footprint.
- **Current state in AIHelperNET:** Opacity is hardcoded to `0.75` in `MainOverlayWindow.xaml`. There is no UI slider or setting to change it.
- **User value:** 0.75 opacity may be too opaque for some setups (high-DPI screens, light backgrounds) or too transparent for others. User control prevents switching apps or workarounds.
- **Possible addition to AIHelperNET:** Add an opacity slider to the sidebar or `SettingsWindow`, bind it to a `WindowOpacity` property on the ViewModel, and propagate to `MainOverlayWindow.Opacity`. Persist via `ISettingsStore`.
- **Evidence from InterviewHelper:** `OverlaySettingsPanel.ts` opacity slider (20–100%); `AppConfig.overlay_opacity`
- **Evidence from AIHelperNET:** `MainOverlayWindow.xaml` `Opacity="0.75"` (hardcoded)
- **Confidence:** Confirmed

---

### Click-Through Overlay Mode

- **Category:** Overlay
- **Status in AIHelperNET:** Missing
- **Priority:** Medium
- **What the user can do in InterviewHelper:** Toggle click-through mode so the overlay forwards all mouse events to the window beneath it. This lets the user interact with the IDE or browser while the overlay floats above without blocking clicks.
- **Current state in AIHelperNET:** The overlay receives all mouse events. It can be repositioned and resized, but it always blocks clicks in the area it covers.
- **User value:** A developer typing code in an IDE while the overlay floats in the corner needs to be able to click through the overlay without moving it.
- **Possible addition to AIHelperNET:** Add a click-through toggle button to the title bar. When active, call `SetWindowLong(hwnd, GWL_EXSTYLE, WS_EX_TRANSPARENT | WS_EX_LAYERED)` via P/Invoke. Untoggle re-adds normal event handling.
- **Evidence from InterviewHelper:** `OverlaySettingsPanel.ts` click-through toggle; `main.ts` `setIgnoreMouseEvents()`
- **Evidence from AIHelperNET:** No P/Invoke for `WS_EX_TRANSPARENT`; no click-through toggle in `MainOverlayWindow.xaml`
- **Confidence:** Confirmed

---

### Screen Capture Source Selection

- **Category:** Screen Capture
- **Status in AIHelperNET:** Missing
- **Priority:** Medium
- **What the user can do in InterviewHelper:** Choose whether to capture a specific monitor, a specific open window by title, or a manually defined screen region. This avoids capturing irrelevant content and keeps OCR focused on the interview task.
- **Current state in AIHelperNET:** `CaptureScreenCommand` always captures the full primary screen. No source selector exists.
- **User value:** A user with two monitors (code on one, video call on other) wants to capture only the code screen. Full-screen capture includes irrelevant content and degrades OCR accuracy.
- **Possible addition to AIHelperNET:** Add a capture source selector (dropdown of monitors + windows from `EnumWindows`) to the sidebar Screen section. Pass the selected region bounds to the screen capture service.
- **Evidence from InterviewHelper:** `ScreenReaderPanel.ts` source selector (monitor/window/region); `WindowsPanel.ts` open window list; `screen_capture.py` `capture_monitor()` / `capture_window()` / `capture_region()`
- **Evidence from AIHelperNET:** `CaptureScreenCommand` handler — no region parameter; full screen capture only
- **Confidence:** Confirmed

---

### Session Notes Panel

- **Category:** History
- **Status in AIHelperNET:** Missing
- **Priority:** Medium
- **What the user can do in InterviewHelper:** Open the Notes panel during or after a session and type freeform notes (preparation points, follow-up actions, observations). Notes are saved per session and automatically loaded when switching between history sessions.
- **Current state in AIHelperNET:** No notes capability exists.
- **User value:** Candidates use notes for things the AI can't see: interviewer names, gut feelings, promised follow-up items, next-step tasks. Session-scoped notes keep these linked to the correct context.
- **Possible addition to AIHelperNET:** Add a Notes tab or collapsible panel in `MainOverlayWindow`. Bind to a `NotesText` property on the SessionViewModel. Persist via `ISettingsStore` or as a field on the `Session` aggregate.
- **Evidence from InterviewHelper:** `NotesPanel.ts` — textarea auto-saved per session; `state.ts` `notesText` field; `SessionManager` persists notes to `session.json`
- **Evidence from AIHelperNET:** `Session` aggregate — no notes field; no notes ViewModel or XAML
- **Confidence:** Confirmed

---

### Named Setting Templates / Presets

- **Category:** Settings
- **Status in AIHelperNET:** Missing
- **Priority:** Medium
- **What the user can do in InterviewHelper:** Save the current combination of answer settings + code profile as a named template (e.g., "React Frontend Interview", "System Design Round"). Load a preset before an interview to configure the app for the expected domain in one click. Six built-in presets are included.
- **Current state in AIHelperNET:** Settings are a single global state. Each interview round that requires different technology context requires manual re-entry.
- **User value:** A developer who interviews for both backend and frontend roles needs different tech stack contexts. Switching between named presets replaces tedious re-configuration.
- **Possible addition to AIHelperNET:** Add a Presets section to the sidebar. Expose save/load/delete commands that serialize `AnswerSettings` + `CodeProfile` into named JSON blobs stored via `ISettingsStore`.
- **Evidence from InterviewHelper:** `TemplatesPanel.ts`; `PresetsPanel.ts` (6 built-in presets); `AppConfig.active_preset`; backend preset persistence
- **Evidence from AIHelperNET:** Single `AppSettingsDto` with no preset/template concept; `JsonSettingsStore` stores one settings file
- **Confidence:** Confirmed

---

### First-Run Onboarding Wizard

- **Category:** Onboarding
- **Status in AIHelperNET:** Missing
- **Priority:** Medium
- **What the user can do in InterviewHelper:** On first launch, a 4-step wizard guides the user through entering their API key, selecting a Whisper model, and confirming setup before the main interface is shown. A keyboard shortcut reference modal is also shown.
- **Current state in AIHelperNET:** The app launches directly into the overlay. If no API key is configured, answer generation fails silently or with a generic error. There is no guided setup.
- **User value:** First-time setup without guidance causes abandonment. The wizard ensures the API key, model, and audio device are configured before the first session.
- **Possible addition to AIHelperNET:** On first launch (detect via missing API key or a `firstRun` flag in settings), show a setup dialog sequence: welcome → API key entry → model/device selection → confirmation.
- **Evidence from InterviewHelper:** `OnboardingModal.ts` 4-step wizard; `KeyboardHelpModal.ts`; `AppState.onboardingRequired` flag
- **Evidence from AIHelperNET:** `App.OnStartup` — no first-run check; `SettingsViewModel` — loads API key status but no wizard flow
- **Confidence:** Confirmed

---

### Draft Recovery After Crash

- **Category:** Reliability
- **Status in AIHelperNET:** Missing
- **Priority:** Medium
- **What the user can do in InterviewHelper:** If the app crashes or is force-quit mid-session, on next launch a recovery modal offers to restore the partial session transcript and answers. The user can accept or discard the draft.
- **Current state in AIHelperNET:** EF Core persists turns and transcript items as they arrive, so data is not lost on crash at the database level. However, there is no modal to inform the user that a prior session was interrupted, and no concept of a "draft" session to restore.
- **User value:** Interview data from a crashed session is immediately needed — the user just had the interview. A recovery prompt prevents the user from starting fresh and losing context.
- **Possible addition to AIHelperNET:** On startup, check for sessions with no `EndedAt` timestamp (orphaned active sessions). If found, offer to restore or end them cleanly via a dialog.
- **Evidence from InterviewHelper:** `RecoveryModal.ts`; `draft_recovery_service.py` — saves draft on each transcript item, clears on clean stop
- **Evidence from AIHelperNET:** `Session` aggregate — has `StartedAt` / `EndedAt`; orphaned sessions (no `EndedAt`) detectable via repository query; no recovery modal exists
- **Confidence:** Confirmed

---

### Auto-Generate Answers Toggle

- **Category:** AI
- **Status in AIHelperNET:** Missing
- **Priority:** Medium
- **What the user can do in InterviewHelper:** Toggle whether answers are generated automatically as soon as a question is detected, or whether the user must explicitly press "Generate answer" for each question. The default is auto-generate enabled.
- **Current state in AIHelperNET:** Answer generation triggers automatically for every detected question. There is no way to opt out of auto-generation or to review a question before generating.
- **User value:** Some users prefer to screen questions first (e.g., skip small talk or obvious questions) before consuming API tokens. Manual trigger saves cost and reduces noise.
- **Possible addition to AIHelperNET:** Add a toggle in the sidebar or settings. When disabled, `QuestionsPanel` shows detected questions with a manual "Generate" button and does not auto-trigger `GenerateAnswerCommand`.
- **Evidence from InterviewHelper:** `SettingsPanel.ts` auto_generate checkbox; `AppConfig.auto_generate_answers`; `SessionManager._on_question()` checks flag before calling `generate_answer()`
- **Evidence from AIHelperNET:** `TranscriptPipelineService` → always triggers `GenerateAnswerCommand` on question detection; no conditional flag
- **Confidence:** Confirmed

---

### Answer Rating

- **Category:** AI
- **Status in AIHelperNET:** Missing
- **Priority:** Medium
- **What the user can do in InterviewHelper:** Rate each generated answer thumbs up or thumbs down. Ratings are stored with the session data and visible in history.
- **Current state in AIHelperNET:** No rating mechanism exists. All answers are treated identically.
- **User value:** Ratings let users track which answer styles or prompt configurations produce better results over time, and help identify sessions worth reviewing.
- **Possible addition to AIHelperNET:** Add thumbs up/down buttons to each `TurnVm` answer card. Store rating as a field on `AnswerVersion` in the domain and persist via repository.
- **Evidence from InterviewHelper:** `AnswersPanel.ts` thumbs buttons per answer; `state.ts` `answerRatings` map; rating persisted in `session.json`
- **Evidence from AIHelperNET:** `AnswerVersionVm` — no rating field; `ConversationTurnViewModel` — no rating command
- **Confidence:** Confirmed

---

### Paste Text / Content for Analysis

- **Category:** Screen Capture
- **Status in AIHelperNET:** Missing
- **Priority:** Medium
- **What the user can do in InterviewHelper:** Paste a block of text (e.g., a coding task copied from a browser) directly into a textarea in the Screen Reader panel and analyze it with the selected AI mode, without needing to perform a screen capture.
- **Current state in AIHelperNET:** Screen context can only be added via OCR capture (`CaptureScreenCommand`). There is no way to paste raw text as context.
- **User value:** Some interview platforms display tasks in iframes or PDF embeds that OCR cannot capture reliably. Paste-to-analyze is a reliable alternative.
- **Possible addition to AIHelperNET:** Add a text area to the sidebar or a dialog that accepts pasted content and injects it as screen context into `GenerateAnswerCommand` / `CaptureScreenCommand` without performing actual OCR.
- **Evidence from InterviewHelper:** `ScreenReaderPanel.ts` paste textarea; `SessionManager` accepts `pasted_text` parameter in `analyze_screen` command
- **Evidence from AIHelperNET:** `CaptureScreenCommand` — no text parameter; only captures screen via P/Invoke
- **Confidence:** Confirmed

---

### Session Status with Detailed States

- **Category:** Session
- **Status in AIHelperNET:** Partially Available
- **Priority:** Medium
- **What the user can do in InterviewHelper:** See a status pill in the header showing exactly what the app is doing: Idle / Listening / Reading screen / Generating / Paused / Error.
- **Current state in AIHelperNET:** Title bar shows "Listening" when active and "Stopped" when not. The states "Reading screen", "Generating", "Paused", and "Error" are not surfaced separately.
- **User value:** Knowing whether the app is generating, reading the screen, or paused versus simply listening helps users understand why output is delayed.
- **Possible addition to AIHelperNET:** Extend `SessionControlViewModel` with a `DetailedStatus` enum covering: Stopped, Listening, ReadingScreen, Generating, Error. Bind to a colored status pill in the title bar.
- **Evidence from InterviewHelper:** `Header.ts` status pill; `state.ts` `status` field with 6 values
- **Evidence from AIHelperNET:** `MainOverlayWindow.xaml` title bar label bound to `IsSessionActive` (binary); no mid-session state breakdown
- **Confidence:** Confirmed

---

### Answer Full-Text Search

- **Category:** History
- **Status in AIHelperNET:** Missing
- **Priority:** Medium
- **What the user can do in InterviewHelper:** Open the Answer Search panel and search all generated answers across all sessions with a text query. Results show matching answer snippets with highlighting.
- **Current state in AIHelperNET:** No search capability. Past answers are in SQLite but no query interface exists.
- **User value:** Developers want to re-find a good answer they got two sessions ago. Without search, all historical value is locked away.
- **Possible addition to AIHelperNET:** Add an `AnswerSearchQuery` handler that uses EF Core full-text or LIKE queries on `AnswerVersion.Text`. Expose a search panel accessible from the sidebar.
- **Evidence from InterviewHelper:** `AnswerSearchPanel.ts`; `history_store.py` SQLite FTS `answer_search()` method
- **Evidence from AIHelperNET:** `AppDbContext` has `AnswerVersion` table — no search handler exists
- **Confidence:** Confirmed

---

### In-App Diagnostics / Logs Panel

- **Category:** Diagnostics
- **Status in AIHelperNET:** Missing
- **Priority:** Low
- **What the user can do in InterviewHelper:** Open the Diagnostics panel to view backend and frontend logs, OCR status (Tesseract version, config), audio device details, and error messages — all in-app without opening a terminal or log file.
- **Current state in AIHelperNET:** Logs go to `Serilog` files at `AppPaths.LogsDirectory`. Users must find and open the log file manually.
- **User value:** When audio or transcription breaks, users cannot diagnose the issue without developer tools. An in-app log tail reduces support burden.
- **Possible addition to AIHelperNET:** Add a diagnostic panel with a tail of recent Serilog messages via an in-memory sink. Show AI connection status, audio device state, and Whisper model load status.
- **Evidence from InterviewHelper:** `LogsPanel.ts` — scrollable log display updated via WS; backend sends `log.entry` events
- **Evidence from AIHelperNET:** `Serilog` configured in `Program.cs`; no in-app log surface
- **Confidence:** Confirmed

---

### Session Statistics

- **Category:** Statistics
- **Status in AIHelperNET:** Missing
- **Priority:** Low
- **What the user can do in InterviewHelper:** View current session statistics: duration, number of transcript items, questions detected, answers generated, and words per minute. A word frequency table shows the most-discussed topics.
- **Current state in AIHelperNET:** No statistics display. The data exists in the domain model but is never aggregated for the user.
- **Possible addition to AIHelperNET:** Add a stats section in the sidebar derived from `GetCurrentSessionQuery` — count turns, answers, transcript items, and elapsed time. Bind to a `SessionStatsViewModel`.
- **Evidence from InterviewHelper:** `StatsPanel.ts`; `state.ts` `statsData` field; backend `get_session_stats()` handler
- **Evidence from AIHelperNET:** `Session` aggregate tracks transcript items and turns; no stats query or ViewModel
- **Confidence:** Confirmed

---

### Transcript Bookmarks

- **Category:** Transcription
- **Status in AIHelperNET:** Missing
- **Priority:** Low
- **What the user can do in InterviewHelper:** Click a bookmark icon on any transcript line to mark it for later reference. Bookmarks are collected in the Bookmarks panel with scroll-to-highlight action.
- **Current state in AIHelperNET:** No bookmarking capability.
- **Possible addition to AIHelperNET:** Add a bookmark toggle per `TranscriptItemVm`. Collect bookmarked IDs and expose a Bookmarks panel that scrolls the transcript to the selected item.
- **Evidence from InterviewHelper:** `TranscriptPanel.ts` bookmark icon per item; `BookmarksPanel.ts`; `state.ts` `bookmarks[]`
- **Evidence from AIHelperNET:** `TranscriptItemVm` — no bookmark field
- **Confidence:** Confirmed

---

### Answer Confidence Badge

- **Category:** AI
- **Status in AIHelperNET:** Missing
- **Priority:** Low
- **What the user can do in InterviewHelper:** See a 1–5 filled-circle confidence indicator next to each answer, estimated by scanning the answer text for hedging phrases ("I think", "probably", "I'm not sure").
- **Current state in AIHelperNET:** No confidence indicator.
- **Possible addition to AIHelperNET:** Add a simple hedge-phrase scan to `AnswerVersionVm` or the answer handler. Display as colored dots or a label on the answer card.
- **Evidence from InterviewHelper:** `AnswersPanel.ts` confidence circles; `answer_generator.py` `_calculate_confidence()` method
- **Evidence from AIHelperNET:** No confidence scan or display in any ViewModel
- **Confidence:** Confirmed

---

### Clipboard History

- **Category:** AI
- **Status in AIHelperNET:** Missing
- **Priority:** Low
- **What the user can do in InterviewHelper:** Open the Clipboard History panel to see the last 10 answers copied to the clipboard, with timestamp, question snippet, and a re-copy button.
- **Current state in AIHelperNET:** `CopyLatestCommand` copies to clipboard but no history is tracked.
- **Possible addition to AIHelperNET:** Maintain an in-memory `ObservableCollection` of the last 10 copied answer texts in a `ClipboardHistoryViewModel`. Add a history panel accessible from the sidebar.
- **Evidence from InterviewHelper:** `ClipboardHistoryPanel.ts`; `state.ts` `clipboardHistory[]`
- **Evidence from AIHelperNET:** `CopyLatestCommand` in `ConversationTurnViewModel` — no history kept
- **Confidence:** Confirmed

---

### Answer Expand / Collapse and Pin

- **Category:** AI
- **Status in AIHelperNET:** Missing
- **Priority:** Low
- **What the user can do in InterviewHelper:** Collapse a long answer to a few lines to save space, or expand it to see the full text. Pin an answer to keep it from scrolling away when new answers arrive.
- **Current state in AIHelperNET:** All answers are always fully displayed; no collapse or pinning. The answers panel scrolls as a list.
- **Possible addition to AIHelperNET:** Add `IsExpanded` and `IsPinned` bool properties to `TurnVm`. In the XAML, bind the answer text block's max-height to `IsExpanded` and add a toggle button. Pinned turns sort to the top of the list.
- **Evidence from InterviewHelper:** `AnswersPanel.ts` expand/collapse button; pin button per answer
- **Evidence from AIHelperNET:** `TurnVm` — no expansion/pin state; `MainOverlayWindow.xaml` — full text always visible
- **Confidence:** Confirmed

---

### Overlay Monitor Selection

- **Category:** Overlay
- **Status in AIHelperNET:** Missing
- **Priority:** Low
- **What the user can do in InterviewHelper:** Choose which physical monitor the overlay appears on (primary, secondary, etc.) from a dropdown in Overlay Settings.
- **Current state in AIHelperNET:** The overlay appears on whichever monitor it was last positioned on. No explicit monitor selection exists.
- **Possible addition to AIHelperNET:** Enumerate monitors via `Screen.AllScreens` (WinForms) or WPF interop. Add a monitor selector to settings. On selection change, set `MainOverlayWindow.Left` / `Top` to center on the selected monitor.
- **Evidence from InterviewHelper:** `OverlaySettingsPanel.ts` monitor dropdown; `AppConfig.overlay_monitor`
- **Evidence from AIHelperNET:** No monitor enumeration or selection
- **Confidence:** Confirmed

---

### Notification System

- **Category:** UX
- **Status in AIHelperNET:** Missing
- **Priority:** Low
- **What the user can do in InterviewHelper:** Receive a system notification (and optional sound) when an answer is ready. This lets the user focus on the interviewer while waiting, without watching the overlay.
- **Current state in AIHelperNET:** No notification when answers complete. User must watch the overlay for answer text to appear.
- **Possible addition to AIHelperNET:** On `IAnswerStreamSink.OnComplete`, call Windows toast notification API. Add a sound/notification toggle to settings.
- **Evidence from InterviewHelper:** `AppConfig.notifications_answers_enabled`; `AppConfig.notifications_sound_enabled`; backend sends notification trigger
- **Evidence from AIHelperNET:** `IAnswerStreamSink.OnComplete` — no notification call
- **Confidence:** Confirmed

---

### Privacy Consent Banner

- **Category:** Onboarding / Privacy
- **Status in AIHelperNET:** Missing
- **Priority:** Low
- **What the user can do in InterviewHelper:** See a dismissible ethics/privacy banner on first launch that explains permitted and prohibited use of the tool.
- **Current state in AIHelperNET:** No consent or privacy notice.
- **Evidence from InterviewHelper:** `app.ts` consent banner rendered before main UI; `state.ts` consent dismissed flag
- **Evidence from AIHelperNET:** No consent UI in any view
- **Confidence:** Confirmed

---

## 6. Settings and Customization Gaps

| Category | Setting in InterviewHelper | What It Configures | Status in AIHelperNET | Recommended Addition | Evidence | Priority |
|----------|---------------------------|-------------------|----------------------|---------------------|----------|----------|
| Audio | Microphone device selector | Pick specific mic from system device list | Missing | Add device dropdown in sidebar Audio section | `AudioSettingsPanel.ts`; `AppConfig.microphone_device` | High |
| Audio | Loopback device selector | Pick specific system audio device | Missing | Add loopback dropdown alongside mic selector | `AudioSettingsPanel.ts`; `AppConfig.loopback_device` | High |
| Audio | Input language | Set Whisper transcription language (auto/en/ru/pl/custom) | Missing | Add language dropdown to audio settings | `AudioSettingsPanel.ts`; `AppConfig.input_language` | Medium |
| Audio | Compute device (CPU/CUDA/auto) | Select GPU or CPU for transcription | Missing | Add compute selector to settings; relevant when GPU available | `AudioSettingsPanel.ts`; `AppConfig.compute_device` | Low |
| Audio | Compute type (int8/float16/float32) | Trade off accuracy vs speed | Missing | Add compute type selector; default int8 | `AudioSettingsPanel.ts`; `AppConfig.compute_type` | Low |
| Audio | VAD toggle | Enable/disable voice activity detection | Unconfirmed (always on) | Add toggle if not already exposed | `AppConfig.vad_filter` | Low |
| Screen | Capture source (monitor/window/region) | Choose what to capture for OCR | Missing | Add source selector in Screen section of sidebar | `ScreenReaderPanel.ts`; `AppConfig.capture_source` | Medium |
| Screen | Periodic capture interval | Auto-screenshot at N seconds | Missing | Add interval selector + enable toggle to Screen settings | `AppConfig.periodic_capture_interval` | Medium |
| Screen | Save screenshots to disk | Persist PNG screenshots per session | Missing | Add toggle to settings; save alongside SQLite data | `AppConfig.save_screenshots` | Low |
| Answer | Response format (verbal/code/mixed) | Control whether answer includes code blocks | Available | Already in `AnswerSettings.Format` | `AnswerSettingsPanel.ts` | — |
| Answer | Max tokens (per length) | Override token budget | Missing | Add max tokens slider to advanced settings | `SettingsPanel.ts`; `AppConfig.max_tokens` | Medium |
| Answer | Temperature | Control answer randomness/creativity | Missing | Add temperature slider to advanced settings | `SettingsPanel.ts`; `AppConfig.temperature` | Medium |
| Answer | System prompt suffix | Append custom instructions to system prompt | Missing | Add text area in advanced settings | `SettingsPanel.ts`; `AppConfig.system_prompt_suffix` | Medium |
| Answer | Auto-generate on question | Toggle automatic answer generation | Missing | Add toggle to sidebar or settings | `SettingsPanel.ts`; `AppConfig.auto_generate_answers` | Medium |
| Code | Tech stack version fields | Specify exact framework/DB/Node versions | Partially Available | `CodeProfile` has some fields; version fields not confirmed | `CodeSettingsPanel.ts` (version inputs per tech) | Low |
| Code | Payment provider | Add payment platform to code context | Unconfirmed | Check if `CodeProfile` includes payment provider field | `CodeSettingsPanel.ts` payment selector | Low |
| Overlay | Opacity slider | Set overlay transparency (20%–100%) | Missing (hardcoded 0.75) | Add opacity slider to sidebar or settings | `OverlaySettingsPanel.ts`; `AppConfig.overlay_opacity` | Medium |
| Overlay | Click-through toggle | Pass mouse events through overlay | Missing | Add P/Invoke toggle button to title bar | `OverlaySettingsPanel.ts` | Medium |
| Overlay | Monitor selection | Pin overlay to specific monitor | Missing | Add monitor dropdown to overlay settings | `OverlaySettingsPanel.ts`; `AppConfig.overlay_monitor` | Low |
| Overlay | Content protection (default off) | User opt-in for screen-capture exclusion | Partially Available (default on in AIHelperNET, not user-configurable as a setting) | Expose as toggle in settings with default on | `OverlaySettingsPanel.ts` (default off) | Low |
| Hotkeys | Rebindable shortcuts | Customize all 7 global hotkey combos | Missing | Add hotkey editor to settings panel | `HotkeysPanel.ts`; `AppConfig.hotkeys` | Medium |
| Theme | High-contrast theme | Accessibility theme | Missing | Add third theme option | `SettingsPanel.ts` `theme: dark/light/high-contrast` | Low |
| Theme | Locale / UI language | Switch UI language (en/fr) | Missing | Add locale selector to settings | `SettingsPanel.ts`; `AppConfig.locale` | Low |
| Notifications | Answer-ready notification | System notification when answer complete | Missing | Add toggle + sound toggle to settings | `AppConfig.notifications_*` | Low |
| Export | Output directory | Choose where session files are saved | Unconfirmed | Add path picker to settings | `AppConfig.output_dir` | Low |
| Export | Auto-save on stop | Save session automatically on stop | Available (SQLite, always on) | Already auto-persists | `AppConfig.auto_save_on_stop` | — |
| AI | Whisper model (UI selection) | Choose Tiny→Large model | Partially Available (stored, no UI) | Add model selector dropdown | `AudioSettingsPanel.ts`; `AppSettingsDto.WhisperModel` | Medium |

---

## 7. User Workflow Gaps

### Workflow: Setting Up Audio Devices Before a Session

**InterviewHelper user flow:**
1. Open sidebar → Audio panel.
2. See enumerated mic and loopback device lists.
3. Select the headset mic and the correct WASAPI loopback.
4. Press "Test microphone" — app captures for 3 seconds and shows peak level and RMS.
5. Press "Test system audio" — verify interviewer audio is captured.
6. Observe live level bars during test.
7. Confident setup is correct → start session.

**AIHelperNET equivalent flow:**
1. Launch app.
2. Start a session.
3. Speak — if transcript appears, mic is working. If not, no feedback.
4. No way to select a different device from the UI.

**Gap identified:** The user has no way to verify or change audio device configuration before starting a live session. Any device misconfiguration is only discovered after capture fails silently.

**Recommended improvement:** Add audio device selection dropdowns and test-capture buttons to the sidebar or Settings dialog.

**Evidence:** `AudioSettingsPanel.ts`; `audio_manager.py` `get_input_devices()`; `NAudioCaptureService` (accepts device IDs but no enumeration UI)

---

### Workflow: Reviewing Past Sessions

**InterviewHelper user flow:**
1. Open sidebar → History.
2. See list of sessions with date, mode, word count, and tags.
3. Search by keyword or tag.
4. Open a session → see transcript and answers inline.
5. Open sidebar → Export → export this session's answers as Markdown.

**AIHelperNET equivalent flow:**
1. All session data stored in SQLite.
2. No history UI exists.
3. User cannot access past sessions from the app.

**Gap identified:** All historical session value is locked behind the SQLite file. The user cannot review, search, or export any past content without a database tool.

**Recommended improvement:** Add a History panel showing past sessions from `GetSessionHistoryQuery`. Add an Export button that calls a serializer for transcript + answers.

**Evidence:** `HistoryPanel.ts`; `ExportPanel.ts`; `GetSessionHistoryQuery` (Application layer, no UI binding); `AppDbContext`

---

### Workflow: Getting an Answer for a Coding Task on Screen

**InterviewHelper user flow:**
1. Interviewer shares a coding task on screen.
2. User presses Ctrl+Shift+S → Screen Reader panel opens.
3. User selects "Solve coding task" analysis mode.
4. App captures screen, OCRs text, sends to Claude with coding task prompt.
5. Answer with full code solution appears in Answers panel.

**AIHelperNET equivalent flow:**
1. User presses Ctrl+Shift+S.
2. App captures full primary screen via OCR.
3. OCR text is injected into the existing detected question prompt as extra context.
4. A new answer version is generated (Updated with screen).
5. No mode selection — the prompt is generic.

**Gap identified:** AIHelperNET uses the same generic prompt regardless of screen content type. A coding task, a stack trace, and a system design diagram all get the same treatment, producing suboptimal responses.

**Recommended improvement:** Add a screen analysis mode selector with at least 3 options (Solve coding task, Debug error, Explain code) that choose the appropriate `PromptBuilderService` template.

**Evidence:** `ScreenReaderPanel.ts` 8-mode selector; `screen_analyzer.py` prompt dispatch by mode; `CaptureScreenCommand` single code path; `PromptBuilderService` no mode parameter

---

### Workflow: Pausing During an Interview Break

**InterviewHelper user flow:**
1. Interviewer says "Take 5 minutes."
2. User clicks Pause.
3. Audio capture halts; transcript and answers preserved.
4. User takes break.
5. User clicks Resume.
6. Capture restarts; session continues with same context.

**AIHelperNET equivalent flow:**
1. User must Stop the session (ends session permanently).
2. After break, Start a new session.
3. New session has no context from the previous one.

**Gap identified:** Stopping a session in AIHelperNET is irreversible. A pause mid-interview forces a context break.

**Recommended improvement:** Add Pause/Resume states to the `Session` aggregate and expose Pause/Resume buttons in the overlay title bar, gating the audio pipeline on the paused flag.

**Evidence:** `SessionControls.ts` pause/resume; `Session` aggregate states (Active/Stopped only) in `Domain/Sessions/Session.cs`

---

### Workflow: Switching to a Different Interview Round Type

**InterviewHelper user flow:**
1. First round: frontend React interview. User loads "React Frontend" preset → code settings pre-filled.
2. Second round: system design. User selects "System Design" preset → answer style changes to architecture mode.
3. Each round takes 2 seconds to configure.

**AIHelperNET equivalent flow:**
1. User must manually re-enter code profile and answer settings for each round.
2. No preset concept exists.
3. Settings changes not confirmed reachable from UI at all.

**Gap identified:** No preset/template system means every session requires full manual reconfiguration for different interview types.

**Recommended improvement:** Add presets stored in `ISettingsStore` as named snapshots of `AnswerSettings` + `CodeProfile`. Expose load/save buttons in the sidebar.

**Evidence:** `PresetsPanel.ts` built-in presets; `TemplatesPanel.ts` user presets; `AppSettingsDto` single global settings object

---

## 8. Recommended Feature Backlog for AIHelperNET

| Priority | Feature to Add or Improve | Current Gap | Expected User Benefit | Rough Complexity | Evidence Source |
|----------|--------------------------|------------|----------------------|-----------------|-----------------|
| High | Audio device selection UI | No way to choose mic or loopback device from UI | Correct device capture on first attempt; no silent miscapture | Small | `AudioSettingsPanel.ts`; `AppSettingsDto.MicDeviceId` |
| High | Session history browser | No UI to access past sessions | Review, search, and learn from past interview sessions | Medium | `HistoryPanel.ts`; `GetSessionHistoryQuery` |
| High | Export transcript and answers | No export capability | Share and archive session content as TXT/MD/JSON | Small | `ExportPanel.ts`; `export_service.py` |
| High | Session pause / resume | Only start/stop; stop is permanent | Survive interview breaks without losing session context | Medium | `SessionControls.ts`; `Session` aggregate |
| High | Compact overlay strip | No lightweight overlay mode | Use a non-intrusive overlay during screen sharing | Medium | `overlay-compact.ts`; `OverlaySettingsPanel.ts` |
| High | Follow-up question per answer | No inline follow-up tied to an answer | Continue a reasoning chain without losing context | Small | `AnswersPanel.ts` follow-up input |
| High | Answer settings + code profile UI panels | Settings stored but UI binding unconfirmed | Change answer style and tech stack mid-interview | Small | `AnswerSettingsPanel.ts`; `CodeSettingsPanel.ts` |
| High | Screen analysis modes (8 options) | Single generic OCR mode | Context-appropriate prompts for coding tasks, debug, design | Medium | `ScreenReaderPanel.ts`; `screen_analyzer.py` |
| Medium | Live audio level meters | No level display; silent failures undetectable | Verify capture is working before session | Small | `AudioSettingsPanel.ts`; `_broadcast_levels()` |
| Medium | Audio test buttons | No pre-session device verification | Eliminate wasted sessions from bad device config | Small | `AudioSettingsPanel.ts` test handlers |
| Medium | Whisper model downloader UI | Silent background download; no progress or control | User can download larger models knowingly | Small | `ModelDownloaderPanel.ts` |
| Medium | Input language selection | Auto-detect only; poor accuracy for non-English | Correct transcription for non-native English speakers | Small | `AudioSettingsPanel.ts`; `AppConfig.input_language` |
| Medium | Overlay opacity slider | Hardcoded 0.75 opacity | Fine-tune visibility for different screen setups | Small | `OverlaySettingsPanel.ts` |
| Medium | Click-through overlay mode | Overlay always blocks clicks underneath | Type in IDE while overlay floats above without blocking | Small | `OverlaySettingsPanel.ts`; `setIgnoreMouseEvents` |
| Medium | Screen capture source selection | Always captures full primary screen | Capture only relevant monitor or window | Small | `ScreenReaderPanel.ts`; `screen_capture.py` |
| Medium | Paste text for analysis | Cannot analyze pasted text without OCR | Analyze content from iframes or clipboard | Small | `ScreenReaderPanel.ts` paste textarea |
| Medium | Rebindable hotkeys UI | Hardcoded shortcuts; no conflict resolution | Users with IDE shortcut conflicts can remap | Medium | `HotkeysPanel.ts`; `AppConfig.hotkeys` |
| Medium | Session notes panel | No notes capability | Attach human observations to interview sessions | Small | `NotesPanel.ts` |
| Medium | Named setting templates / presets | Single global settings; no preset concept | Switch between interview types in one click | Medium | `TemplatesPanel.ts`; `PresetsPanel.ts` |
| Medium | First-run onboarding wizard | No guided setup; silent failure if API key missing | Users set up correctly on first launch | Small | `OnboardingModal.ts` |
| Medium | Draft recovery after crash | No recovery for interrupted sessions | Recover data from unexpected app exit | Medium | `RecoveryModal.ts`; `draft_recovery_service.py` |
| Medium | Auto-generate answers toggle | Always auto-generates; no way to opt out | Screen questions before spending API tokens | Small | `SettingsPanel.ts`; `AppConfig.auto_generate_answers` |
| Medium | Answer rating (thumbs) | No answer quality feedback | Track which answers were useful; inform prompt tuning | Small | `AnswersPanel.ts` thumbs buttons |
| Medium | Answer full-text search | No cross-session answer search | Re-find useful answers from past sessions | Medium | `AnswerSearchPanel.ts`; `history_store.py` |
| Medium | Detailed session status states | Binary Active/Stopped only | Know exactly what app is doing at any moment | Small | `Header.ts` status pill |
| Low | Session comparison | No compare mode | Contrast two interview sessions | Large | `HistoryPanel.ts` compare |
| Low | Session replay | No replay | Replay a past session step-by-step | Large | `ReplayPanel.ts` |
| Low | Session statistics | No stats | See session duration, word rate, question count | Small | `StatsPanel.ts` |
| Low | Transcript bookmarks | No bookmarks | Mark important transcript moments | Small | `BookmarksPanel.ts`; `TranscriptPanel.ts` |
| Low | Transcript search | No in-session transcript search | Find specific spoken content quickly | Small | `TranscriptPanel.ts` search |
| Low | Answer expand / collapse | Always full display | Save screen space when answers are long | Small | `AnswersPanel.ts` expand/collapse |
| Low | Answer pin | No pinning | Keep critical answers visible as new ones arrive | Small | `AnswersPanel.ts` pin |
| Low | Answer confidence badge | No confidence indicator | Gauge answer certainty at a glance | Small | `AnswersPanel.ts`; `answer_generator.py` |
| Low | Clipboard history | No copy history | Re-copy a previous answer without scrolling | Small | `ClipboardHistoryPanel.ts` |
| Low | In-app diagnostics / logs panel | Log file only | Diagnose audio/transcription issues without a terminal | Small | `LogsPanel.ts` |
| Low | Notification when answer ready | No notification | Focus on interviewer; get notified when answer appears | Small | `AppConfig.notifications_*` |
| Low | High-contrast theme | Dark/light only | Accessibility for high-contrast display users | Small | `SettingsPanel.ts` theme enum |
| Low | URL extraction from screenshots | No URL detection | Quickly access links shown in interview task | Small | `url_extractor.py` |
| Low | Overlay monitor selection | No monitor picker | Pin overlay to secondary monitor | Small | `OverlaySettingsPanel.ts` monitor selector |

---

## 9. Unknowns and Required Verification

| Potential Feature | What Is Unclear | Why It Cannot Be Confirmed | What Must Be Checked Next |
|------------------|----------------|---------------------------|--------------------------|
| Answer Settings UI | Whether `AnswerSettings` (length, complexity, style, tone, format, language) is actually accessible to the user from any UI panel | `SettingsWindow.xaml` was confirmed to show only the API key section. No XAML binding to `AnswerSettings` fields was found. The ViewModel and model exist but the UI binding is unconfirmed. | Run the app, open Settings dialog, and verify whether answer style controls appear. Check `MainOverlayWindow.xaml` and `SettingsWindow.xaml` for hidden panels. |
| Code Profile UI | Whether `CodeProfile` fields (programming language, frameworks, etc.) are configurable from any UI | Same as above — the model exists in `AppSettingsDto` but no XAML or ViewModel command binding to these fields was found in the settings dialog | Run the app and inspect the full Settings dialog. Search `MainOverlayWindow.xaml` for any code profile bindings. |
| Loopback device working state | Whether `LoopbackDeviceId` from `AppSettingsDto` is actually passed to `NAudioCaptureService` at session start | Code paths are complex; the service accepts a device ID parameter, but whether `AppSettingsDto.LoopbackDeviceId` is wired through `StartSessionCommand` → `SessionRunner` → `NAudioCaptureService` has not been fully traced | Trace `StartSessionCommand` handler through `IoCContainer` wiring to confirm device ID flows into `NAudioCaptureService` constructor |
| Whisper model selection active | Whether the user's `WhisperModel` setting is actually used by `WhisperModelProvider` to select the correct model file, or if a hardcoded default is used | `WhisperModelProvider` was not read in full detail during the survey | Read `WhisperModelProvider.cs` fully and trace how `AppSettingsDto.WhisperModel` reaches the Whisper factory |
| Stealth default state | Whether stealth (content protection) is ON by default at launch or only after the user toggles it | The survey notes "Default: Stealth ON" but this should be verified against the initialization code | Read `MainOverlayWindow.xaml.cs` `OnLoaded` or `OnInitialized` to confirm `SetWindowDisplayAffinity` is called at startup |
| Screen OCR implementation | Whether `CaptureScreenCommand` uses OCR infrastructure that already ships or requires `Tesseract` installed separately | Survey found `IInfrastructure` with OCR references but did not confirm whether OCR is self-contained or requires a third-party install | Check `OcrService` implementation and any prerequisites in `Infrastructure/OCR/` |
| Periodic screen capture | Whether AIHelperNET has any periodic background capture wired up (not found in survey, but the domain model has screen capture support) | `SessionRunner` was surveyed for audio pipeline; its screen-related scheduling was not confirmed absent | Search `SessionRunner.cs` for any `Task.Delay`/`Timer` loop involving screen capture |
| Hotkey conflict feedback | Whether global hotkey registration in `App.OnStartup` reports success/failure anywhere visible to the user | The registration code was identified but its error handling was not read | Read hotkey registration code and check for any error logging or dialog on registration failure |
