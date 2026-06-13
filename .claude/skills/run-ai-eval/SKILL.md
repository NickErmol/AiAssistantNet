---
name: run-ai-eval
description: Use when running the opt-in live-LLM evals in AIHelperNET (real-Haiku boundary classifier eval or Spec B live answer-depth), capturing baseline accuracy, checking a prompt/threshold change against the held-out floor, or when an eval "Skipped:" instead of calling the API. Covers feeding the Anthropic key from Windows Credential Manager without re-pasting it.
---

# Run AIHelperNET Live-LLM Evals

## Overview

Two test families hit the **real Anthropic API** and **self-skip (pass trivially) when no key is
present**, so CI/offline runs stay green. To actually exercise the model you must supply the key —
and the two families take the key by *different* mechanisms. Picking the wrong one is the #1 reason
a run prints `Skipped:` and makes no network call.

**Never echo, log, paste into a file, or commit the key.** It lives only in Windows Credential
Manager (target `AIHelperNET:ClaudeApiKey`). The bridge below reads it in-process and never prints it.

## Which eval do you want?

| Family | Class | Key source | How to enable |
|---|---|---|---|
| Boundary classifier (Haiku) | `BoundaryClassifierAiEvalTests` | env var `AIHELPER_AI_EVAL_KEY` | export the var in the **same** command |
| Spec B answer-depth (live Claude) | `SpecBAnswerDepthLiveTests` | reads Credential Manager directly | just have the key stored — **no env var** |
| Screen-answer card (Haiku judge) | `ScreenAnswerCardLiveTests` | reads Credential Manager directly | just have the key stored — **no env var** |

The boundary family has 3 facts: `RealHaiku_OverCorpus` (report), `RealHaiku_OverGarbled`
(**report-only — no accuracy assertion**, by design), `RealHaiku_OverHoldout` (**asserts ≥ 0.90**,
the generalization floor — this one *fails* a regressed prompt wherever a key exists).

## Run it — no re-pasting the key

The key is already in Credential Manager. Read it into the env var and run `dotnet test` in **one**
process so the var is in scope (see Common Mistakes). Use the **PowerShell tool** — the `CredRead`
P/Invoke compiles cleanly there; do NOT try to embed this C# through the Bash tool (the `\"`
escaping mangles and you get `len=0`).

```powershell
$sig = @'
using System;
using System.Runtime.InteropServices;
public static class Cred {
  [DllImport("advapi32", CharSet=CharSet.Unicode, SetLastError=true)]
  static extern bool CredRead(string target, int type, int flags, out IntPtr cred);
  [DllImport("advapi32")] static extern void CredFree(IntPtr cred);
  [StructLayout(LayoutKind.Sequential)] struct CREDENTIAL {
    public int Flags; public int Type; public IntPtr TargetName; public IntPtr Comment;
    public long LastWritten; public int CredentialBlobSize; public IntPtr CredentialBlob;
    public int Persist; public int AttributeCount; public IntPtr Attributes;
    public IntPtr TargetAlias; public IntPtr UserName;
  }
  public static string Read(string target) {
    IntPtr p; if (!CredRead(target, 1, 0, out p)) return null;
    try { var c=(CREDENTIAL)Marshal.PtrToStructure(p,typeof(CREDENTIAL));
      return Marshal.PtrToStringUni(c.CredentialBlob, c.CredentialBlobSize/2); }
    finally { CredFree(p); }
  }
}
'@
Add-Type -TypeDefinition $sig
$env:AIHELPER_AI_EVAL_KEY = [Cred]::Read("AIHelperNET:ClaudeApiKey")   # never echoed
dotnet test tests/AIHelperNET.Integration.Tests `
  --filter "FullyQualifiedName~BoundaryClassifierAiEvalTests" `
  --nologo -l "console;verbosity=detailed"
$env:AIHELPER_AI_EVAL_KEY = $null   # clear it back out
```

If you already hold the key as plaintext, the **Bash tool** one-liner is simplest (no CredRead):
`AIHELPER_AI_EVAL_KEY="sk-ant-..." dotnet test ... --filter "..."` — the `VAR="..." cmd` prefix
keeps it scoped to that one process.

**Spec B** needs no key plumbing — the key is read from Credential Manager by the test itself:
```bash
dotnet test tests/AIHelperNET.Integration.Tests \
  --filter "FullyQualifiedName~SpecBAnswerDepthLiveTests" \
  --nologo -l "console;verbosity=detailed"
```

**Screen-answer card** also reads the key from Credential Manager itself (no env plumbing):
```bash
dotnet test tests/AIHelperNET.Integration.Tests \
  --filter "FullyQualifiedName~ScreenAnswerCardLiveTests" \
  --nologo -l "console;verbosity=detailed"
```
Generates each card with the production model and grades it with Haiku. Both tiers are enforced:
the deterministic gates (no truncation, code present where required, required substrings) **and**
the Haiku judge mean against a held-out floor (**asserts ≥ 0.80**; observed 0.91–0.95 over the
screen-task scenarios — the floor sits below that to absorb the judge's run-to-run variance on
free-form cards). See the `screen-answer-eval-*.txt` report in the diagnostics dir for the per-turn
verdicts. Like any LLM-judged floor, a failure means "investigate the report," not necessarily a
code bug.

## Reading the result

- Reports are written to the diagnostics dir under the data root (`...\AIHelperNET\diagnostics\`,
  e.g. `ai-eval-*.txt`, `ai-eval-holdout-*.txt`) **and** echoed via `console;verbosity=detailed`.
- A genuine run shows the confusion matrix + `Misses:` list. A `Skipped: set AIHELPER_AI_EVAL_KEY ...`
  line means the key never reached the test — fix the shell scoping, don't re-run blindly.
- Boundary labels are the `BoundaryLabel` enum exactly: `QuestionContinued`, `AdditionalRequirement`,
  `NewQuestion`, `QuestionComplete`, `TaskComplete`, `QuestionStarted`,
  `ClarificationOfCurrentQuestion`, `Unrelated`, `NoQuestion`.

## Common Mistakes

| Symptom | Cause / fix |
|---|---|
| Prints `Skipped:` despite "setting" the key | Var set in a *different* shell/tool call than `dotnet test`. Set + run in **one** command. |
| `$env:AIHELPER_AI_EVAL_KEY=...; dotnet test` in the **Bash** tool does nothing | That's PowerShell syntax. In Bash use the `VAR="..." dotnet test` prefix; `$env:` only works in the PowerShell tool. |
| Background eval "disappears" | `Start-Job` does **not** survive across tool calls. For a long run use the tool's own `run_in_background`, not a PS job. |
| Garbled eval "fails to flag garbage" | Expected. `RealHaiku_OverGarbled` is **report-only** — there's no reliable text-only label for garble; the deterministic `AsrConfidenceGate` is the real guard. Never add an accuracy assertion there. |
| Holdout test fails after a prompt edit | Real signal: generalization dropped below the 0.90 floor. Investigate the prompt, don't lower the floor to make it pass. |
| Non-determinism between runs | Haiku is probabilistic. One red run isn't proof; these are human checkpoints, never CI gates. |

## Security

- The key is `sk-ant-...`, length ~108. The bridge reads it via `advapi32 CredRead` and never prints it.
- Pre-commit, confirm no key leaked: `git diff --cached` should contain no `sk-ant-`/`AIHELPER_AI_EVAL_KEY="sk-...`.
- Do not write the key into a plan/spec/doc — past docs only ever held an `sk-ant-...` **placeholder**.
