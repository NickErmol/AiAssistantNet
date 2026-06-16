# Configurable Topic-Adaptive Transcription Glossary — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bias Whisper transcription with a configurable, topic-adaptive, multi-domain tech glossary so domain terms (N+1, Key Vault, AsNoTracking, …) stop garbling, and fix the latent bug where `lastEmitted` drops the domain prompt mid-session.

**Architecture:** A baked-in JSON glossary (9 domains) loaded by an Infrastructure provider; a pure Application `GlossarySelector` picks ~110 words of terms relevant to the recent transcript; `WhisperTranscriptionService` injects the provider, keeps a recent-segment ring buffer, and combines preamble + glossary + rolling context into the Whisper prompt. Per-domain toggles live in `AppSettingsDto` and the Settings UI, threaded through `TranscribeAsync` the same way `model`/`language` already flow.

**Tech Stack:** .NET 10, C#, Whisper.net, System.Text.Json, CommunityToolkit.Mvvm (WPF), xUnit + FluentAssertions + NSubstitute.

**Spec:** `docs/superpowers/specs/2026-06-16-transcription-glossary-design.md`

**Worktree:** `D:\work\AIHelperNET-glossary` on `feature/configurable-transcription-glossary`.

**Standing gotchas:** Stop any running overlay before building the App project (MSB3027 DLL lock). `TreatWarningsAsErrors` + XML docs required on public members. Domain operations never throw. Commit on this feature branch only — never develop/master.

---

## File structure

| File | Responsibility | Task |
|---|---|---|
| `src/AIHelperNET.Infrastructure/Transcription/Glossary/glossary.json` | Baked term data, 9 domains | 1 |
| `src/AIHelperNET.Application/Sessions/Glossary/GlossaryDomain.cs` | Domain data model (record) | 2 |
| `src/AIHelperNET.Application/Sessions/Glossary/GlossarySelector.cs` | Pure term-selection algorithm | 3 |
| `tests/AIHelperNET.Application.Tests/Glossary/GlossarySelectorTests.cs` | Selector unit tests | 3 |
| `src/AIHelperNET.Application/Sessions/Dtos/AppSettingsDto.cs` | `GlossaryEnabled`, `EnabledGlossaryDomains`, `Normalized()` | 4 |
| `tests/AIHelperNET.Application.Tests/Dtos/AppSettingsDtoGlossaryTests.cs` | Settings normalize tests | 4 |
| `src/AIHelperNET.Application/Abstractions/ITranscriptionGlossaryProvider.cs` | Port | 5 |
| `src/AIHelperNET.Infrastructure/Transcription/JsonTranscriptionGlossaryProvider.cs` | Loads JSON + delegates to selector | 5 |
| `tests/AIHelperNET.Infrastructure.Tests/Transcription/GlossaryProviderTests.cs` | Provider/JSON tests | 5 |
| `src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj` | Embed glossary.json | 5 |
| `src/AIHelperNET.Infrastructure/DependencyInjection.cs:52` | Register provider | 5 |
| `src/AIHelperNET.Application/Abstractions/ITranscriptionService.cs` | Add `glossaryDomains` param | 6 |
| `src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs` | Ring buffer + prompt build + bug fix | 6 |
| `src/AIHelperNET.App/Services/SessionRunner.cs` | Thread `glossaryDomains` through | 6 |
| `src/AIHelperNET.App/ViewModels/SessionControlViewModel.cs:92` | Compute effective domains from settings | 6 |
| `src/AIHelperNET.App/ViewModels/SettingsViewModel.cs` | Toggle props + load/save | 7 |
| `src/AIHelperNET.App/Windows/SettingsWindow.xaml` | Master toggle + 9 checkboxes | 7 |
| `tests/AIHelperNET.Infrastructure.Tests/Transcription/WhisperGlossaryPromptTests.cs` (or Tier-C) | Regression | 8 |

---

## Task 1: Glossary data file (web-researched)

**Files:**
- Create: `src/AIHelperNET.Infrastructure/Transcription/Glossary/glossary.json`

**This task is fulfilled by web research** (a research subagent gathers real interview vocabulary per domain, then curates). Output must conform exactly to this schema and acceptance criteria.

- [ ] **Step 1: Author `glossary.json`** with this exact shape:

```json
{
  "domains": [
    {
      "key": "dotnet",
      "displayName": ".NET / C#",
      "core": ["N plus one query", "Entity Framework", "dependency injection", "async await", "LINQ"],
      "terms": ["AsNoTracking", "DbContext", "transient", "scoped", "singleton", "captive dependency",
                "MediatR", "CQRS", "middleware", "minimal API", "IEnumerable", "IQueryable",
                "SemaphoreSlim", "CancellationToken", "record struct", "nullable reference types",
                "garbage collection", "span", "ValueTask", "Polly", "circuit breaker"]
    }
  ]
}
```

**Acceptance criteria (verified in Task 5 tests):**
- Exactly these 9 domain keys: `dotnet`, `angular`, `react`, `sql`, `mssql`, `postgres`, `azure`, `aws`, `cloud`.
- Each domain: non-empty `displayName`, `core` length 3–6, `terms` length ≥ 20.
- No duplicate keys; no duplicate terms within a domain.
- Terms written as Whisper would need to *hear* them — spell out symbol-heavy terms phonetically where the literal token is unspeakable (e.g. `"N plus one query"`, not `"N+1"`), since the prompt biases acoustic decoding. Keep a literal variant too where natural.
- Domain coverage:
  - `dotnet`: C#/.NET/EF Core/ASP.NET/DI/async (see example above).
  - `angular`: change detection, OnPush, RxJS, observable, signal, NgRx, BehaviorSubject, HttpInterceptor, standalone component, dependency injection, zone.js, async pipe, lifecycle hooks, etc.
  - `react`: hooks, useState, useEffect, useMemo, useCallback, context, reducer, virtual DOM, reconciliation, JSX, Redux, memoization, suspense, server components, etc.
  - `sql`: join, inner join, left join, index, primary key, foreign key, normalization, transaction, ACID, window function, CTE, group by, having, stored procedure, etc.
  - `mssql`: T-SQL, clustered index, non-clustered index, execution plan, SQL Server, temp table, table variable, MERGE, isolation level, deadlock, SQL Profiler, etc.
  - `postgres`: PostgreSQL, PL/pgSQL, JSONB, GIN index, VACUUM, MVCC, sequence, materialized view, partitioning, etc.
  - `azure`: Azure Key Vault, Azure Service Bus, App Service, Azure SQL, managed identity, Cosmos DB, Functions, Blob Storage, Application Insights, ARM template, etc.
  - `aws`: EC2, S3, Lambda, RDS, DynamoDB, SQS, SNS, CloudFormation, IAM, CloudWatch, ECS, EKS, etc.
  - `cloud`: microservices, message broker, RabbitMQ, Kafka, eventual consistency, saga, CQRS, circuit breaker, load balancer, horizontal scaling, idempotency, distributed cache, Redis, Kubernetes, Docker, etc.

- [ ] **Step 2: Validate JSON parses** locally:

Run: `cd /d/work/AIHelperNET-glossary && node -e "const d=require('./src/AIHelperNET.Infrastructure/Transcription/Glossary/glossary.json'); console.log(d.domains.map(x=>x.key+':'+x.core.length+'/'+x.terms.length).join('\n'))"`
Expected: 9 lines, each `core` 3–6, `terms` ≥ 20. (If `node` unavailable, skip — Task 5 test enforces this.)

- [ ] **Step 3: Commit**

```bash
cd /d/work/AIHelperNET-glossary
git add src/AIHelperNET.Infrastructure/Transcription/Glossary/glossary.json
git commit -m "feat(transcription): add web-researched 9-domain ASR glossary data"
```

---

## Task 2: GlossaryDomain model

**Files:**
- Create: `src/AIHelperNET.Application/Sessions/Glossary/GlossaryDomain.cs`

- [ ] **Step 1: Write the model**

```csharp
namespace AIHelperNET.Application.Sessions.Glossary;

/// <summary>A named set of transcription-bias terms for one technology domain.</summary>
/// <param name="Key">Stable lowercase identifier (e.g. "dotnet"). Used in settings.</param>
/// <param name="DisplayName">Human-readable name for the Settings UI.</param>
/// <param name="Core">Always-injected high-value terms when the domain is enabled.</param>
/// <param name="Terms">Topic-adaptive candidate terms.</param>
public sealed record GlossaryDomain(
    string Key,
    string DisplayName,
    IReadOnlyList<string> Core,
    IReadOnlyList<string> Terms);
```

- [ ] **Step 2: Build the Application project**

Run: `cd /d/work/AIHelperNET-glossary && dotnet build src/AIHelperNET.Application/AIHelperNET.Application.csproj`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add src/AIHelperNET.Application/Sessions/Glossary/GlossaryDomain.cs
git commit -m "feat(transcription): add GlossaryDomain model"
```

---

## Task 3: GlossarySelector (pure algorithm, TDD)

**Files:**
- Create: `src/AIHelperNET.Application/Sessions/Glossary/GlossarySelector.cs`
- Test: `tests/AIHelperNET.Application.Tests/Glossary/GlossarySelectorTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using AIHelperNET.Application.Sessions.Glossary;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Glossary;

public class GlossarySelectorTests
{
    private static readonly GlossaryDomain Dotnet = new(
        "dotnet", ".NET",
        Core: ["Entity Framework", "dependency injection"],
        Terms: ["AsNoTracking", "captive dependency", "MediatR", "Polly", "SemaphoreSlim"]);

    private static readonly GlossaryDomain Azure = new(
        "azure", "Azure",
        Core: ["Azure Key Vault"],
        Terms: ["managed identity", "Service Bus", "Cosmos DB"]);

    private static readonly IReadOnlyList<GlossaryDomain> All = [Dotnet, Azure];

    [Fact]
    public void Excludes_disabled_domains()
    {
        var result = GlossarySelector.Select(All, new HashSet<string> { "dotnet" }, recentContext: "", wordBudget: 100);
        result.Should().Contain("Entity Framework");
        result.Should().NotContain("Azure Key Vault");
    }

    [Fact]
    public void Always_includes_core_when_domain_enabled()
    {
        var result = GlossarySelector.Select(All, new HashSet<string> { "dotnet", "azure" }, recentContext: "", wordBudget: 100);
        result.Should().Contain("Entity Framework").And.Contain("dependency injection").And.Contain("Azure Key Vault");
    }

    [Fact]
    public void Context_matching_terms_rank_above_unrelated()
    {
        // Tiny budget so only the highest-scored non-core term survives.
        var result = GlossarySelector.Select(
            All, new HashSet<string> { "dotnet" },
            recentContext: "we kept hitting a captive dependency problem with our singleton",
            wordBudget: 5);
        result.Should().Contain("captive dependency");
    }

    [Fact]
    public void Respects_word_budget()
    {
        var result = GlossarySelector.Select(All, new HashSet<string> { "dotnet", "azure" }, recentContext: "", wordBudget: 4);
        result.Sum(t => t.Split(' ').Length).Should().BeLessThanOrEqualTo(4);
    }

    [Fact]
    public void Empty_enabled_set_returns_empty()
    {
        GlossarySelector.Select(All, new HashSet<string>(), recentContext: "x", wordBudget: 100).Should().BeEmpty();
    }

    [Fact]
    public void No_duplicate_terms()
    {
        var result = GlossarySelector.Select(All, new HashSet<string> { "dotnet", "azure" }, recentContext: "", wordBudget: 100);
        result.Should().OnlyHaveUniqueItems();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd /d/work/AIHelperNET-glossary && dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~GlossarySelectorTests"`
Expected: FAIL — `GlossarySelector` does not exist.

- [ ] **Step 3: Implement the selector**

```csharp
namespace AIHelperNET.Application.Sessions.Glossary;

/// <summary>Pure, deterministic selection of transcription-bias terms under a word budget.</summary>
public static class GlossarySelector
{
    /// <summary>Selects terms from the enabled domains, prioritising core terms then terms most
    /// lexically relevant to <paramref name="recentContext"/>, never exceeding <paramref name="wordBudget"/> words.</summary>
    /// <param name="domains">All known glossary domains.</param>
    /// <param name="enabledKeys">Keys of the domains the user has enabled.</param>
    /// <param name="recentContext">Recently transcribed text used to bias term relevance; may be empty.</param>
    /// <param name="wordBudget">Maximum total words across the returned terms.</param>
    /// <returns>Ordered, de-duplicated terms: core first, then context-ranked, within budget.</returns>
    public static IReadOnlyList<string> Select(
        IReadOnlyList<GlossaryDomain> domains,
        IReadOnlySet<string> enabledKeys,
        string recentContext,
        int wordBudget)
    {
        if (domains.Count == 0 || enabledKeys.Count == 0 || wordBudget <= 0)
            return [];

        var enabled = domains.Where(d => enabledKeys.Contains(d.Key)).ToList();
        if (enabled.Count == 0) return [];

        var contextTokens = Tokenize(recentContext);
        var selected = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int usedWords = 0;

        bool TryAdd(string term)
        {
            if (!seen.Add(term)) return true;                 // already have it; keep scanning
            int w = WordCount(term);
            if (usedWords + w > wordBudget) return false;     // would overflow
            selected.Add(term);
            usedWords += w;
            return true;
        }

        // 1. Core terms first (the floor).
        foreach (var term in enabled.SelectMany(d => d.Core))
            if (!TryAdd(term) && usedWords >= wordBudget) return selected;

        // 2. Remaining terms ranked by relevance to recent context (desc), then original order.
        var ranked = enabled
            .SelectMany(d => d.Terms)
            .Where(t => !seen.Contains(t))
            .Select((t, i) => (Term: t, Score: Relevance(t, contextTokens, recentContext), Order: i))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Order);

        foreach (var x in ranked)
            if (!TryAdd(x.Term) && usedWords >= wordBudget) break;

        return selected;
    }

    private static int Relevance(string term, IReadOnlySet<string> contextTokens, string recentContext)
    {
        if (contextTokens.Count == 0) return 0;
        int score = 0;
        foreach (var tok in Tokenize(term))
            if (contextTokens.Contains(tok)) score += 2;       // shared word
        if (recentContext.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 5; // whole-term hit
        return score;
    }

    private static int WordCount(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    private static HashSet<string> Tokenize(string text) =>
        [.. text.ToLowerInvariant()
            .Split([' ', '.', ',', '?', '!', ';', ':', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2)];
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd /d/work/AIHelperNET-glossary && dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~GlossarySelectorTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Application/Sessions/Glossary/GlossarySelector.cs tests/AIHelperNET.Application.Tests/Glossary/GlossarySelectorTests.cs
git commit -m "feat(transcription): add topic-adaptive GlossarySelector with tests"
```

---

## Task 4: Settings fields + Normalized()

**Files:**
- Modify: `src/AIHelperNET.Application/Sessions/Dtos/AppSettingsDto.cs`
- Test: `tests/AIHelperNET.Application.Tests/Dtos/AppSettingsDtoGlossaryTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using AIHelperNET.Application.Sessions.Dtos;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Dtos;

public class AppSettingsDtoGlossaryTests
{
    private static AppSettingsDto Make(string[] domains, bool enabled = true) =>
        new(default, default, null!, null!, null, null) { EnabledGlossaryDomains = domains, GlossaryEnabled = enabled };

    [Fact]
    public void Normalized_lowercases_and_dedupes_domains()
    {
        var n = Make(["DotNet", "dotnet", "AZURE"]).Normalized();
        n.EnabledGlossaryDomains.Should().BeEquivalentTo(["dotnet", "azure"]);
    }

    [Fact]
    public void Normalized_drops_blank_domains()
    {
        var n = Make(["dotnet", "", "  "]).Normalized();
        n.EnabledGlossaryDomains.Should().BeEquivalentTo(["dotnet"]);
    }

    [Fact]
    public void Default_glossary_enabled_is_true()
    {
        var dto = new AppSettingsDto(default, default, null!, null!, null, null);
        dto.GlossaryEnabled.Should().BeTrue();
    }
}
```

Note: `GlossaryEnabled` must be a positional optional parameter (default `true`); `EnabledGlossaryDomains` an `init` property (collection, like `Presets`).

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd /d/work/AIHelperNET-glossary && dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~AppSettingsDtoGlossaryTests"`
Expected: FAIL — members do not exist.

- [ ] **Step 3: Add the fields and normalization**

Add `bool GlossaryEnabled = true` as the last positional parameter (after `OverlayMode`):

```csharp
    OverlayMode OverlayMode = OverlayMode.Stealth,
    bool GlossaryEnabled = true)
```

Add the init property near `Presets`/`HotkeyOverrides`:

```csharp
    /// <summary>Glossary domain keys whose terms bias transcription. Empty ⇒ none.</summary>
    public IReadOnlyList<string> EnabledGlossaryDomains { get; init; } = [];
```

Extend `Normalized()` to add:

```csharp
        EnabledGlossaryDomains = NormalizeGlossaryDomains(EnabledGlossaryDomains),
```

And add the helper:

```csharp
    private static IReadOnlyList<string> NormalizeGlossaryDomains(IReadOnlyList<string> raw)
    {
        if (raw is null or { Count: 0 }) return [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(raw.Count);
        foreach (var d in raw)
        {
            if (string.IsNullOrWhiteSpace(d)) continue;
            var key = d.Trim().ToLowerInvariant();
            if (seen.Add(key)) result.Add(key);
        }
        return result;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd /d/work/AIHelperNET-glossary && dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~AppSettingsDtoGlossaryTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Build full solution to catch positional-record break sites**

Run: `cd /d/work/AIHelperNET-glossary && dotnet build`
Expected: Build succeeded. (If any `new AppSettingsDto(...)` call site breaks, it won't — the new param is optional. The `SessionMapper` Mapperly partial maps by name, unaffected.)

- [ ] **Step 6: Commit**

```bash
git add src/AIHelperNET.Application/Sessions/Dtos/AppSettingsDto.cs tests/AIHelperNET.Application.Tests/Dtos/AppSettingsDtoGlossaryTests.cs
git commit -m "feat(settings): add GlossaryEnabled + EnabledGlossaryDomains with normalization"
```

---

## Task 5: Port + provider + embed + DI

**Files:**
- Create: `src/AIHelperNET.Application/Abstractions/ITranscriptionGlossaryProvider.cs`
- Create: `src/AIHelperNET.Infrastructure/Transcription/JsonTranscriptionGlossaryProvider.cs`
- Modify: `src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj` (embed JSON)
- Modify: `src/AIHelperNET.Infrastructure/DependencyInjection.cs:52`
- Test: `tests/AIHelperNET.Infrastructure.Tests/Transcription/GlossaryProviderTests.cs`

- [ ] **Step 1: Write the port**

```csharp
using AIHelperNET.Application.Sessions.Glossary;

namespace AIHelperNET.Application.Abstractions;

/// <summary>Supplies the transcription glossary and builds a Whisper prompt suffix from it.</summary>
public interface ITranscriptionGlossaryProvider
{
    /// <summary>All known glossary domains (for the Settings UI and selection).</summary>
    IReadOnlyList<GlossaryDomain> Domains { get; }

    /// <summary>Builds a space-joined glossary suffix for the enabled domains, biased toward
    /// <paramref name="recentContext"/>, capped at <paramref name="wordBudget"/> words.
    /// Returns empty string when nothing is selected.</summary>
    string BuildPromptSuffix(IReadOnlySet<string> enabledKeys, string recentContext, int wordBudget);
}
```

- [ ] **Step 2: Write the failing provider tests**

```csharp
using AIHelperNET.Infrastructure.Transcription;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Transcription;

public class GlossaryProviderTests
{
    private static readonly JsonTranscriptionGlossaryProvider Provider = new();

    [Fact]
    public void Loads_all_nine_domains()
    {
        Provider.Domains.Select(d => d.Key).Should().BeEquivalentTo(
            ["dotnet", "angular", "react", "sql", "mssql", "postgres", "azure", "aws", "cloud"]);
    }

    [Fact]
    public void Every_domain_has_core_and_terms()
    {
        foreach (var d in Provider.Domains)
        {
            d.DisplayName.Should().NotBeNullOrWhiteSpace();
            d.Core.Should().HaveCountGreaterThanOrEqualTo(3).And.HaveCountLessThanOrEqualTo(6);
            d.Terms.Should().HaveCountGreaterThanOrEqualTo(20);
            d.Terms.Should().OnlyHaveUniqueItems();
        }
    }

    [Fact]
    public void BuildPromptSuffix_includes_enabled_core()
    {
        var suffix = Provider.BuildPromptSuffix(new HashSet<string> { "azure" }, "", 100);
        suffix.Should().Contain("Key Vault");
    }

    [Fact]
    public void BuildPromptSuffix_empty_when_disabled()
    {
        Provider.BuildPromptSuffix(new HashSet<string>(), "anything", 100).Should().BeEmpty();
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `cd /d/work/AIHelperNET-glossary && dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~GlossaryProviderTests"`
Expected: FAIL — provider does not exist.

- [ ] **Step 4: Embed the JSON** — add to `AIHelperNET.Infrastructure.csproj` inside an `<ItemGroup>`:

```xml
  <ItemGroup>
    <EmbeddedResource Include="Transcription\Glossary\glossary.json" />
  </ItemGroup>
```

- [ ] **Step 5: Implement the provider**

```csharp
using System.Reflection;
using System.Text.Json;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Glossary;
using AIHelperNET.Infrastructure.Common;
using Serilog;

namespace AIHelperNET.Infrastructure.Transcription;

/// <summary>Loads the glossary from an optional data-root override or the embedded default,
/// and builds prompt suffixes via <see cref="GlossarySelector"/>.</summary>
public sealed class JsonTranscriptionGlossaryProvider : ITranscriptionGlossaryProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public IReadOnlyList<GlossaryDomain> Domains { get; }

    /// <summary>Creates the provider, loading the override file if present else the embedded default.</summary>
    public JsonTranscriptionGlossaryProvider() => Domains = Load();

    /// <inheritdoc />
    public string BuildPromptSuffix(IReadOnlySet<string> enabledKeys, string recentContext, int wordBudget)
    {
        var terms = GlossarySelector.Select(Domains, enabledKeys, recentContext, wordBudget);
        return terms.Count == 0 ? string.Empty : string.Join(", ", terms) + ".";
    }

    private static IReadOnlyList<GlossaryDomain> Load()
    {
        try
        {
            var overridePath = Path.Combine(AppPaths.Base, "glossary.json");
            if (File.Exists(overridePath))
                return Parse(File.ReadAllText(overridePath));

            var asm = Assembly.GetExecutingAssembly();
            var resourceName = asm.GetManifestResourceNames()
                .First(n => n.EndsWith("glossary.json", StringComparison.OrdinalIgnoreCase));
            using var stream = asm.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Glossary load failed; transcription will run without bias terms");
            return [];
        }
    }

    private static IReadOnlyList<GlossaryDomain> Parse(string json)
    {
        var root = JsonSerializer.Deserialize<GlossaryFile>(json, JsonOptions);
        return root?.Domains ?? [];
    }

    private sealed record GlossaryFile(List<GlossaryDomain> Domains);
}
```

Note: `AppPaths.Base` is the data root (verify the member name in `src/AIHelperNET.Infrastructure/Common/AppPaths.cs` — it backs `DatabaseFile`/`SettingsFile`). `GlossaryDomain`'s constructor params (`Key`, `DisplayName`, `Core`, `Terms`) deserialize from the lowercased JSON keys via `JsonSerializerDefaults.Web` (case-insensitive).

- [ ] **Step 6: Register in DI** — in `src/AIHelperNET.Infrastructure/DependencyInjection.cs`, near line 52:

```csharp
        services.AddSingleton<ITranscriptionGlossaryProvider, JsonTranscriptionGlossaryProvider>();
        services.AddSingleton<ITranscriptionService, WhisperTranscriptionService>();
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `cd /d/work/AIHelperNET-glossary && dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~GlossaryProviderTests"`
Expected: PASS (4 tests). This also proves Task 1's data meets the schema.

- [ ] **Step 8: Commit**

```bash
git add src/AIHelperNET.Application/Abstractions/ITranscriptionGlossaryProvider.cs \
        src/AIHelperNET.Infrastructure/Transcription/JsonTranscriptionGlossaryProvider.cs \
        src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj \
        src/AIHelperNET.Infrastructure/DependencyInjection.cs \
        tests/AIHelperNET.Infrastructure.Tests/Transcription/GlossaryProviderTests.cs
git commit -m "feat(transcription): add glossary provider, embed JSON, register in DI"
```

---

## Task 6: Whisper wiring + thread the config through

**Files:**
- Modify: `src/AIHelperNET.Application/Abstractions/ITranscriptionService.cs`
- Modify: `src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs`
- Modify: `src/AIHelperNET.App/Services/SessionRunner.cs` (lines 133, 150; signatures at 24, 45, 79)
- Modify: `src/AIHelperNET.App/ViewModels/SessionControlViewModel.cs:92`

- [ ] **Step 1: Extend the port** — add a parameter to `TranscribeAsync`:

```csharp
    IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> frames,
        WhisperModelSize model,
        string language,
        IReadOnlySet<string> glossaryDomains,
        CancellationToken ct);
```

Add to the XML doc: `<param name="glossaryDomains">Enabled glossary domain keys to bias decoding; empty ⇒ no bias.</param>`.

- [ ] **Step 2: Wire the Whisper service** — modify `WhisperTranscriptionService`:

1. Inject the provider into the primary constructor:

```csharp
public sealed class WhisperTranscriptionService(
    WhisperModelProvider whisperModels,
    SileroModelProvider  sileroModels,
    ITranscriptionGlossaryProvider glossary) : ITranscriptionService
```

2. Add a recent-context ring buffer constant + the prompt budget:

```csharp
    private const int RecentContextSegments = 6;
    private const int GlossaryWordBudget = 110;
```

3. Update the signature to accept `IReadOnlySet<string> glossaryDomains` (matching the port, before `ct`).

4. Replace the `lastEmitted` tracking with a small queue and build the prompt combining preamble + glossary + recent context. Replace lines ~37 and ~49:

```csharp
        var recent = new Queue<string>(RecentContextSegments);
        string BuildPrompt()
        {
            var recentContext = string.Join(' ', recent);
            var suffix = glossaryDomains.Count == 0
                ? string.Empty
                : glossary.BuildPromptSuffix(glossaryDomains, recentContext, GlossaryWordBudget);
            // preamble + glossary bias + rolling recent context (each part trimmed to fit the cap)
            var basePart = recentContext.Length == 0 ? InitialPrompt : recentContext;
            return suffix.Length == 0 ? basePart : $"{suffix} {basePart}";
        }
```

Then at the builder, use `.WithPrompt(BuildPrompt())` instead of `.WithPrompt(lastEmitted ?? InitialPrompt)`.

5. Where a segment is emitted (replace `lastEmitted = seg.Text.Trim();` block), keep the ring buffer and de-dup reference:

```csharp
                var text = seg.Text.Trim();
                lastEmitted = text;                 // retained for IsNearDuplicate
                recent.Enqueue(text);
                while (recent.Count > RecentContextSegments) recent.Dequeue();
                yield return new TranscriptSegment(text, window.Speaker, DateTimeOffset.UtcNow, seg.Probability);
```

Keep `lastEmitted` declared (used by `IsNearDuplicate`). The key change: the glossary is now part of *every* prompt, not dropped after the first segment.

- [ ] **Step 3: Thread through SessionRunner** — add `IReadOnlySet<string> glossaryDomains` to `StartAsync` (line 24) and `RunAsync` (line 79) signatures, pass it from `StartAsync` into `RunAsync` (line 45), and add it to both `TranscribeAsync` calls (lines 133, 150):

```csharp
                        .TranscribeAsync(micChannel.Reader.ReadAllAsync(ct), model, language, glossaryDomains, ct)
```
```csharp
                        .TranscribeAsync(loopbackChannel.Reader.ReadAllAsync(ct), model, language, glossaryDomains, ct)
```

- [ ] **Step 4: Compute effective domains in the caller** — in `SessionControlViewModel.cs` around line 92 where `runner.StartAsync(...)` is called, read settings and pass:

```csharp
                var glossaryDomains = settings.GlossaryEnabled
                    ? new HashSet<string>(settings.EnabledGlossaryDomains, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>();
                await runner.StartAsync(sessionId, devices, model, language, audioSource, glossaryDomains);
```

Verify how `settings` (an `AppSettingsDto`) is obtained at this call site (it already reads `model`/`language`/`audioSource` from settings); reuse that same instance. The `StartAsync` argument order must match Step 3 (append `glossaryDomains` last).

- [ ] **Step 5: Update any other ITranscriptionService callers/mocks** — search and fix:

Run: `cd /d/work/AIHelperNET-glossary && grep -rn "TranscribeAsync" --include=*.cs tests/ src/`
For each test/fake implementing or calling `TranscribeAsync`, add the `IReadOnlySet<string> glossaryDomains` parameter (tests can pass `new HashSet<string>()`).

- [ ] **Step 6: Build the solution**

Run: `cd /d/work/AIHelperNET-glossary && dotnet build`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 7: Run the affected test suites**

Run: `cd /d/work/AIHelperNET-glossary && dotnet test tests/AIHelperNET.Application.Tests && dotnet test tests/AIHelperNET.Infrastructure.Tests`
Expected: PASS (no regressions).

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(transcription): bias Whisper prompt with glossary; fix lastEmitted overwrite"
```

---

## Task 7: Settings UI toggles

**Files:**
- Modify: `src/AIHelperNET.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/AIHelperNET.App/Windows/SettingsWindow.xaml`

First read both files fully and follow the existing patterns: `[ObservableProperty]` fields, how the VM loads from / saves to `AppSettingsDto`, and how existing toggles/checkboxes are laid out and styled (TabControl needs `PART_SelectedContentHost`; add UIA `AutomationProperties.Name` like neighbouring controls).

- [ ] **Step 1: Add VM state** — a master toggle and one observable item per domain. Add a small item type and an observable collection:

```csharp
    [ObservableProperty] private bool _glossaryEnabled = true;

    /// <summary>One UI row per glossary domain with an enabled checkbox.</summary>
    public ObservableCollection<GlossaryDomainToggle> GlossaryDomains { get; } = [];
```

```csharp
// New file or nested: src/AIHelperNET.App/ViewModels/GlossaryDomainToggle.cs
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIHelperNET.App.ViewModels;

/// <summary>A single glossary-domain checkbox row in Settings.</summary>
public sealed partial class GlossaryDomainToggle(string key, string displayName, bool isEnabled) : ObservableObject
{
    /// <summary>Stable domain key persisted in settings.</summary>
    public string Key { get; } = key;
    /// <summary>Human-readable domain name.</summary>
    public string DisplayName { get; } = displayName;
    /// <summary>Whether this domain biases transcription.</summary>
    [ObservableProperty] private bool _isEnabled = isEnabled;
}
```

- [ ] **Step 2: Populate on load + persist on save** — inject `ITranscriptionGlossaryProvider` into the VM (constructor) to list domains. When loading settings, populate `GlossaryDomains` from `provider.Domains`, ticking those whose `Key` is in `settings.EnabledGlossaryDomains`; set `GlossaryEnabled`. When building the `AppSettingsDto` to save, set:

```csharp
            GlossaryEnabled = GlossaryEnabled,
            EnabledGlossaryDomains = GlossaryDomains.Where(d => d.IsEnabled).Select(d => d.Key).ToList(),
```

(Match how the VM currently constructs the DTO — `with`-expression or `new`.)

- [ ] **Step 3: Add XAML** — in the appropriate settings section (near Transcription/Whisper settings), following the existing control style:

```xml
<StackPanel Margin="0,8,0,0">
    <CheckBox Content="Bias transcription with tech glossary"
              IsChecked="{Binding GlossaryEnabled}"
              AutomationProperties.Name="Enable transcription glossary" />
    <ItemsControl ItemsSource="{Binding GlossaryDomains}" Margin="16,4,0,0"
                  IsEnabled="{Binding GlossaryEnabled}">
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <CheckBox Content="{Binding DisplayName}"
                          IsChecked="{Binding IsEnabled}"
                          AutomationProperties.Name="{Binding DisplayName}" />
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</StackPanel>
```

- [ ] **Step 4: Build the App project** (stop the running overlay first to avoid MSB3027)

Run: `cd /d/work/AIHelperNET-glossary && dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 5: Run App tests**

Run: `cd /d/work/AIHelperNET-glossary && dotnet test tests/AIHelperNET.App.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(settings): per-domain transcription glossary toggles in Settings UI"
```

---

## Task 8: Regression test + full verification

**Files:**
- Create: `tests/AIHelperNET.Infrastructure.Tests/Transcription/WhisperGlossaryPromptTests.cs`

A full real-Whisper Tier-C test (audio → "N+1 query"/"Azure Key Vault") needs an audio fixture and is opt-in/slow. Ship a deterministic prompt-construction test now (proves the glossary actually reaches the prompt and persists), and document the manual Tier-C check.

- [ ] **Step 1: Write a prompt-construction test** verifying the provider yields a non-empty, budgeted suffix that contains a known garble-prone term for the relevant domain:

```csharp
using AIHelperNET.Infrastructure.Transcription;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Transcription;

public class WhisperGlossaryPromptTests
{
    [Fact]
    public void Suffix_surfaces_garble_prone_terms_within_budget()
    {
        var provider = new JsonTranscriptionGlossaryProvider();
        var enabled = new HashSet<string> { "dotnet", "azure" };
        var suffix = provider.BuildPromptSuffix(enabled, recentContext: "tell me about loading and the vault", wordBudget: 110);

        suffix.Should().NotBeEmpty();
        suffix.Split(' ').Length.Should().BeLessThanOrEqualTo(130); // budget + joiners
        suffix.Should().ContainAny("Key Vault", "Entity Framework"); // a core term made it in
    }
}
```

- [ ] **Step 2: Run it**

Run: `cd /d/work/AIHelperNET-glossary && dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~WhisperGlossaryPromptTests"`
Expected: PASS.

- [ ] **Step 3: Full build + test**

Run: `cd /d/work/AIHelperNET-glossary && dotnet build && dotnet test`
Expected: Build succeeded, all suites green. (Note any pre-existing known-flaky Integration/UITest failures from memory; they are unrelated.)

- [ ] **Step 4: Document the manual Tier-C check** — append to the spec a "Manual verification" note: run the overlay, enable `dotnet`+`azure`, play TTS questions containing "N+1 query" and "Azure Key Vault" through the loopback device, confirm the transcript shows those terms (use the `run-ai-eval`/`run-aihelper` skills + TTS→loopback technique).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "test(transcription): glossary prompt-construction regression + verification notes"
```

---

## Self-review notes (author checklist — completed)

- **Spec coverage:** data (T1), model (T2), selector (T3), settings+normalize (T4), port/provider/embed/DI (T5), Whisper wiring + bug fix + threading (T6), UI toggles (T7), tests/verification (T8). All spec sections mapped.
- **Type consistency:** `GlossaryDomain(Key, DisplayName, Core, Terms)`; `GlossarySelector.Select(domains, enabledKeys, recentContext, wordBudget)`; `ITranscriptionGlossaryProvider.{Domains, BuildPromptSuffix(enabledKeys, recentContext, wordBudget)}`; `TranscribeAsync(..., IReadOnlySet<string> glossaryDomains, ct)`; settings `GlossaryEnabled` (bool, default true) + `EnabledGlossaryDomains` (IReadOnlyList<string>). Consistent across tasks.
- **Open verifications flagged for implementers** (not assumptions): `AppPaths.Base` member name; exact `SessionControlViewModel` settings-read site; other `TranscribeAsync` callers/mocks; Settings XAML section + styling conventions.
```
