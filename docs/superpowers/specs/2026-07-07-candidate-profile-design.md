# Candidate Profile (resume + JD condensed injection) — Design

Backlog #9 from `improvement-proposal-2026-07-06.md`, redesigned. The proposal sketched chunk-level RAG (ONNX embeddings + sqlite-vec); exploration showed answer prompts run 500–1600 input tokens and one resume + one JD total ~1500–2500 tokens — retrieval infrastructure solves a problem this data size doesn't have. Chosen approach: **one-time LLM condensation, whole-card injection**.

## Decisions (user-approved 2026-07-07)

- Approach A: condensed profile card, not full-text injection, not RAG.
- Ingestion: file upload (PDF via PdfPig, DOCX via DocumentFormat.OpenXml, txt/md passthrough) **and** paste.
- Injection: all four prompt builders.

## Data flow

Settings UI → (extract or paste) raw text → one Claude Sonnet condensation call → ~550-token markdown card stored in `settings.json` → injected into the system prompt of all four answer builders at generation time.

## Components

1. **Extraction** — Infrastructure `IDocumentTextExtractor` (Application abstraction): `.pdf` via PdfPig (Apache-2.0), `.docx` via DocumentFormat.OpenXml (MIT), `.txt`/`.md` passthrough. Returns `Result<string>` plain text. Paste bypasses it.
2. **Condensation** — Application port `IProfileCondenser`; Infrastructure impl copies the `SessionReviewAnalyzer` non-streaming HTTP pattern (ISecretStore key, ClaudeOptions, fail-fast without key). One Sonnet call; input = raw resume + JD fenced with the established `--- BEGIN/END UNTRUSTED DATA ---` markers; output = two markdown blocks:
   - `CANDIDATE PROFILE` (~400 tokens): summary line, key roles with companies/years, notable projects + tech, skills.
   - `TARGET ROLE` (~150 tokens): role title, must-have skills, domain. Omitted when no JD provided.
3. **Storage** — `AppSettingsDto` gains `ResumeRawText`, `JobDescriptionRawText`, `CandidateProfileCard`. Raw text kept so re-condensing never needs re-upload. No DB migration; lives in `settings.json` via existing `JsonSettingsStore`/`ISettingsStore`. Answer command handlers read the card through `ISettingsStore` at generation time (mid-session updates take effect immediately; no Session snapshot).
4. **Settings UI** — new "Profile" tab in SettingsWindow: Resume section and Job Description section, each = multiline paste textbox + "Load from file…" (OpenFileDialog, filter pdf/docx/txt/md); one "Condense" button with progress indicator and error display; resulting card in an **editable** preview textbox (user trims/fixes before Save).
5. **Injection** — `PromptBuilderService`: optional `string? candidateProfileCard` parameter on `Build`, `BuildFollowUp`, `BuildWithScreenMode`, `BuildScreenFollowUp`; appended in the system prompt directly after the CodeProfile block, wrapped in the untrusted-data fence, prefixed with: use to personalize experience-based answers; never claim experience beyond it.
6. **Failure modes** — no card → prompts byte-identical to today; extraction failure → UI error, paste still available; condensation failure → `Result.Fail` surfaced, raw text preserved; no API key → same fail-fast message as session review.
7. **Tests** — extractor unit tests with fixture files; condenser HTTP-mock tests (model, fencing, no-key, HTTP failure); prompt-builder injection tests (present/absent/fenced/all four builders); settings round-trip; SettingsViewModel tests (extract command, condense command, error paths); one opt-in `[Trait("Category","LiveLlm")]` eval: condense a fixture resume+JD → assert both card sections present and a skill absent from the resume does not appear in the card.

## Why this fixes the observed failure

In the 2026-07-06 real interview, "your profile mentions secure auth journeys…" produced a generic answer because the app knew only the 9-field CodeProfile. The condensed card carries actual resume experience, so experience-based questions get grounded, personalized answers — with an explicit instruction not to invent beyond the card.
