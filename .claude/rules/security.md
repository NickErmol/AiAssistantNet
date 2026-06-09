# Security Rules — AIHelperNET

Always-loaded standing rules. This is a **local Windows WPF desktop app** that talks to
LLM APIs (Claude/Ollama) — there is no server, sessions, cookies, or browser, so classic
web controls (auth/CORS/CSP/security headers) are out of scope by design. The real attack
surface is **secrets at rest, the local data root, untrusted input flowing into LLM prompts,
and dependencies.**

## Secrets & credentials
- API keys live in **Windows Credential Manager** via `WindowsCredentialSecretStore`
  (`ISecretStore`). **Never** write a secret to settings JSON, the SQLite DB, logs, or source.
- Mutate keys only through `SaveApiKeyCommand` / `DeleteApiKeyCommand`; read presence via
  `HasApiKeyQuery` — never surface the key value itself.
- Pre-commit: `git diff --cached | grep -iE "api[_-]?key|secret|token|sk-"` returns nothing.

## Data protection (local data root)
- Everything (SQLite `sessions.db`, settings JSON, logs, models) lives under the data root
  (`D:\AIHelperNET\` or `%LocalAppData%\AIHelperNET\`). Treat it as **sensitive** — it holds
  transcripts of private interviews.
- Don't log full transcripts, prompts, or answer bodies at Information level. Diagnostics
  JSONL (e.g. boundary decisions) must stay metadata-only, never raw PII.

## LLM / prompt-injection surface (the important one)
- `PromptBuilderService` interpolates **untrusted text** into the user prompt: the detected
  `questionText`, `recentTranscript` lines (interviewer speech), and `screenContext` (raw OCR).
  Any of these can contain "ignore previous instructions…"-style content.
- **Impact is bounded today**: LLM output is display-only (shown / read aloud). There is no
  tool execution, file write, or network action driven by model output — keep it that way.
  Treat *"add an agentic action that runs on LLM output"* as a security-relevant change.
- Keep untrusted content **clearly fenced and labeled as data** in prompts (it already uses
  `[Transcript]`, `On-screen context (OCR):`). Don't blur data into the instruction/system
  section. Don't echo raw model output into anything privileged.
- Cap output (`MaxTokens` via `MapLengthToTokens`) — already done; preserve the cap.

## Input validation
- Validate command/query inputs with **FluentValidation** validators (auto-discovered from
  the Application assembly). Domain operations return `DomainResult<T>` — never throw for
  expected-invalid input.

## Dependencies
- New NuGet packages: prefer well-known, maintained sources; pin versions. Native deps
  (Whisper.NET, NAudio) ship native binaries — review provenance before adding/upgrading.
- Don't add a package whose publisher/source you can't verify (see the gitnexus/typosquat
  lesson — a lookalike package name is a red flag, even when "documented").

## Pre-commit quick checks
- [ ] No secrets staged (grep above clean)
- [ ] No raw transcript/prompt/answer logged at Information
- [ ] New untrusted input into a prompt is fenced + labeled as data
- [ ] No new path where LLM output triggers a privileged action without human review
- [ ] New dependencies pinned and from a verifiable source
