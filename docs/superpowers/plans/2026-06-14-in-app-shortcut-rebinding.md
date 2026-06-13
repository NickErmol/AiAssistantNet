# In-App Shortcut Rebinding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Settings → Shortcuts tab editable so users can rebind the six global hotkeys to keyboard chords, persisted in `settings.json` and applied on Save.

**Architecture:** User chords are stored as lean *overrides* in `AppSettingsDto` and merged against `HotkeyDefaults.All` at load/startup. A pure `HotkeyValidator` (Application) enforces modifier-required + no-duplicates; a new App-layer `IHotkeyApplier` re-registers via the existing `GlobalHotkeyService` and reports OS-conflict failures. The Shortcuts tab gets per-action row ViewModels with a hybrid checkbox/dropdown + press-to-record capture.

**Tech Stack:** .NET 10, C#, WPF, CommunityToolkit.Mvvm, Mediator, System.Text.Json (settings), xUnit + FluentAssertions + NSubstitute.

**Spec:** `docs/superpowers/specs/2026-06-14-in-app-shortcut-rebinding-design.md`

---

## Pre-flight

- [ ] **Confirm branch.** This work lives on `feature/in-app-shortcut-rebinding` (already created off `develop`; the spec is already committed there). Verify:

```bash
git branch --show-current   # expect: feature/in-app-shortcut-rebinding
git status --short          # expect: clean
```

- [ ] **Build gotcha reminder:** if the overlay app is running, stop it before building/testing the App project — it locks output DLLs (MSB3027). Use the `run-aihelper` skill's stop step or close the overlay.

---

## File Structure

**Application layer (`net10.0`)**
- `src/AIHelperNET.Application/Abstractions/HotkeyTypes.cs` — expand `VirtualKey`; add `KeyChoice` + `HotkeyKeys`.
- `src/AIHelperNET.Application/Abstractions/HotkeyDefaults.cs` — add `HotkeyOverride`, `Resolve`; route `Gesture` through `HotkeyKeys.Display`.
- `src/AIHelperNET.Application/Abstractions/HotkeyValidator.cs` — **new**, pure validation.
- `src/AIHelperNET.Application/Sessions/Dtos/AppSettingsDto.cs` — add `HotkeyOverrides` init-prop; normalize.

**App layer (`net10.0-windows`)**
- `src/AIHelperNET.App/Hotkeys/IHotkeyApplier.cs` — **new** port.
- `src/AIHelperNET.App/Hotkeys/HotkeyApplier.cs` — **new** impl wrapping `IGlobalHotkeyService`.
- `src/AIHelperNET.App/Hotkeys/KeyGestureCapture.cs` — **new** static WPF→enum translator.
- `src/AIHelperNET.App/ViewModels/HotkeyRowViewModel.cs` — **new** per-action row VM.
- `src/AIHelperNET.App/ViewModels/SettingsViewModel.cs` — rows + save/validate/apply/reset/record.
- `src/AIHelperNET.App/Windows/SettingsWindow.xaml` — editable Shortcuts tab.
- `src/AIHelperNET.App/Windows/SettingsWindow.xaml.cs` — `PreviewKeyDown` recording handler.
- `src/AIHelperNET.App/DependencyInjection.cs` — register `IHotkeyApplier`.
- `src/AIHelperNET.App/App.xaml.cs` — `WireHotkeys` registers the resolved effective set via the applier.

**Tests**
- `tests/AIHelperNET.Application.Tests/Abstractions/` — `HotkeyTypes`, `HotkeyDefaults.Resolve`, `HotkeyValidator`.
- `tests/AIHelperNET.Application.Tests/` — `AppSettingsDto` overrides round-trip/normalize.
- `tests/AIHelperNET.App.Tests/` — applier, translator, row VM, SettingsViewModel.

---

## Task 1: Expand `VirtualKey` + add key display/choices

**Files:**
- Modify: `src/AIHelperNET.Application/Abstractions/HotkeyTypes.cs`
- Modify: `src/AIHelperNET.Application/Abstractions/HotkeyDefaults.cs` (Gesture only)
- Test: `tests/AIHelperNET.Application.Tests/Abstractions/HotkeyKeysTests.cs` (new)

- [ ] **Step 1: Write the failing test**

Create `tests/AIHelperNET.Application.Tests/Abstractions/HotkeyKeysTests.cs`:

```csharp
using AIHelperNET.Application.Abstractions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Abstractions;

public class HotkeyKeysTests
{
    [Theory]
    [InlineData(VirtualKey.A, "A")]
    [InlineData(VirtualKey.Z, "Z")]
    [InlineData(VirtualKey.D0, "0")]
    [InlineData(VirtualKey.D9, "9")]
    [InlineData(VirtualKey.F1, "F1")]
    [InlineData(VirtualKey.F12, "F12")]
    [InlineData(VirtualKey.Space, "Space")]
    public void Display_ReturnsFriendlyName(VirtualKey key, string expected)
        => HotkeyKeys.Display(key).Should().Be(expected);

    [Fact]
    public void Selectable_CoversLettersDigitsFKeysAndSpace_WithNoDuplicates()
    {
        var keys = HotkeyKeys.Selectable.Select(c => c.Key).ToList();
        keys.Should().Contain([VirtualKey.A, VirtualKey.Z, VirtualKey.D0, VirtualKey.D9,
            VirtualKey.F1, VirtualKey.F12, VirtualKey.Space]);
        keys.Should().OnlyHaveUniqueItems();
        keys.Count.Should().Be(26 + 10 + 12 + 1); // A-Z, 0-9, F1-F12, Space
    }

    [Fact]
    public void Digit_GestureUsesBareNumber()
    {
        var b = new HotkeyBinding(HotkeyId.GenerateAnswer, ModifierKeys.Ctrl | ModifierKeys.Alt,
            VirtualKey.D5, "test");
        b.Gesture.Should().Be("Ctrl+Alt+5");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~HotkeyKeysTests"`
Expected: FAIL — `VirtualKey.A`/`HotkeyKeys` don't exist (compile error).

- [ ] **Step 3: Replace the `VirtualKey` enum and add `KeyChoice` + `HotkeyKeys`**

In `HotkeyTypes.cs`, replace the entire `VirtualKey` enum (lines 35-50) with the expanded set and append the new types. The enum values are the real Win32 VK codes:

```csharp
/// <summary>Win32 virtual key codes that can be bound to a global hotkey.</summary>
public enum VirtualKey : uint
{
    /// <summary>Space bar.</summary>
    Space = 0x20,
    /// <summary>0 key.</summary> D0 = 0x30,
    /// <summary>1 key.</summary> D1 = 0x31,
    /// <summary>2 key.</summary> D2 = 0x32,
    /// <summary>3 key.</summary> D3 = 0x33,
    /// <summary>4 key.</summary> D4 = 0x34,
    /// <summary>5 key.</summary> D5 = 0x35,
    /// <summary>6 key.</summary> D6 = 0x36,
    /// <summary>7 key.</summary> D7 = 0x37,
    /// <summary>8 key.</summary> D8 = 0x38,
    /// <summary>9 key.</summary> D9 = 0x39,
    /// <summary>A key.</summary> A = 0x41,
    /// <summary>B key.</summary> B = 0x42,
    /// <summary>C key.</summary> C = 0x43,
    /// <summary>D key.</summary> D = 0x44,
    /// <summary>E key.</summary> E = 0x45,
    /// <summary>F key.</summary> F = 0x46,
    /// <summary>G key.</summary> G = 0x47,
    /// <summary>H key.</summary> H = 0x48,
    /// <summary>I key.</summary> I = 0x49,
    /// <summary>J key.</summary> J = 0x4A,
    /// <summary>K key.</summary> K = 0x4B,
    /// <summary>L key.</summary> L = 0x4C,
    /// <summary>M key.</summary> M = 0x4D,
    /// <summary>N key.</summary> N = 0x4E,
    /// <summary>O key.</summary> O = 0x4F,
    /// <summary>P key.</summary> P = 0x50,
    /// <summary>Q key.</summary> Q = 0x51,
    /// <summary>R key.</summary> R = 0x52,
    /// <summary>S key.</summary> S = 0x53,
    /// <summary>T key.</summary> T = 0x54,
    /// <summary>U key.</summary> U = 0x55,
    /// <summary>V key.</summary> V = 0x56,
    /// <summary>W key.</summary> W = 0x57,
    /// <summary>X key.</summary> X = 0x58,
    /// <summary>Y key.</summary> Y = 0x59,
    /// <summary>Z key.</summary> Z = 0x5A,
    /// <summary>F1 key.</summary> F1 = 0x70,
    /// <summary>F2 key.</summary> F2 = 0x71,
    /// <summary>F3 key.</summary> F3 = 0x72,
    /// <summary>F4 key.</summary> F4 = 0x73,
    /// <summary>F5 key.</summary> F5 = 0x74,
    /// <summary>F6 key.</summary> F6 = 0x75,
    /// <summary>F7 key.</summary> F7 = 0x76,
    /// <summary>F8 key.</summary> F8 = 0x77,
    /// <summary>F9 key.</summary> F9 = 0x78,
    /// <summary>F10 key.</summary> F10 = 0x79,
    /// <summary>F11 key.</summary> F11 = 0x7A,
    /// <summary>F12 key.</summary> F12 = 0x7B
}

/// <summary>A bindable key paired with its display label, for the Settings key dropdown.</summary>
/// <param name="Key">The virtual key.</param>
/// <param name="Display">The label shown to the user (e.g. <c>"5"</c> for <see cref="VirtualKey.D0"/>+5).</param>
public sealed record KeyChoice(VirtualKey Key, string Display);

/// <summary>Display and selection helpers for <see cref="VirtualKey"/>.</summary>
public static class HotkeyKeys
{
    /// <summary>Friendly label for a key — digits show as a bare number (<c>"5"</c>), everything else
    /// uses the enum name (<c>"A"</c>, <c>"F1"</c>, <c>"Space"</c>).</summary>
    public static string Display(VirtualKey key)
    {
        var name = key.ToString();
        return name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])
            ? name[1].ToString()
            : name;
    }

    /// <summary>All bindable keys, ordered for the dropdown: A–Z, 0–9, F1–F12, Space.</summary>
    public static IReadOnlyList<KeyChoice> Selectable { get; } = BuildSelectable();

    private static IReadOnlyList<KeyChoice> BuildSelectable()
    {
        var letters = Enumerable.Range('A', 26).Select(c => (VirtualKey)c);
        var digits  = Enumerable.Range(0, 10).Select(d => (VirtualKey)(0x30 + d));
        var fkeys   = Enumerable.Range(0, 12).Select(f => (VirtualKey)(0x70 + f));
        var all     = letters.Concat(digits).Concat(fkeys).Append(VirtualKey.Space);
        return all.Select(k => new KeyChoice(k, Display(k))).ToList();
    }
}
```

- [ ] **Step 4: Route `Gesture` through `HotkeyKeys.Display`**

In `HotkeyDefaults.cs`, in `HotkeyBinding.Gesture` change the key line:

```csharp
            parts.Add(HotkeyKeys.Display(Key));
```
(was `parts.Add(Key.ToString());`)

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~HotkeyKeysTests|FullyQualifiedName~HotkeyDefaultsTests"`
Expected: PASS (existing `HotkeyDefaultsTests` still green — Space/S unchanged).

- [ ] **Step 6: Commit**

```bash
git add src/AIHelperNET.Application/Abstractions/HotkeyTypes.cs \
        src/AIHelperNET.Application/Abstractions/HotkeyDefaults.cs \
        tests/AIHelperNET.Application.Tests/Abstractions/HotkeyKeysTests.cs
git commit -m "feat(hotkeys): expand VirtualKey set + key display/choices"
```

---

## Task 2: `HotkeyOverride` + `HotkeyDefaults.Resolve`

**Files:**
- Modify: `src/AIHelperNET.Application/Abstractions/HotkeyDefaults.cs`
- Test: `tests/AIHelperNET.Application.Tests/Abstractions/HotkeyResolveTests.cs` (new)

- [ ] **Step 1: Write the failing test**

Create `tests/AIHelperNET.Application.Tests/Abstractions/HotkeyResolveTests.cs`:

```csharp
using AIHelperNET.Application.Abstractions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Abstractions;

public class HotkeyResolveTests
{
    [Fact]
    public void Resolve_NoOverrides_EqualsDefaults()
        => HotkeyDefaults.Resolve([]).Should().BeEquivalentTo(HotkeyDefaults.All);

    [Fact]
    public void Resolve_ReplacesOnlyMatchingId_AndKeepsDescription()
    {
        var overrides = new[] { new HotkeyOverride(HotkeyId.GenerateAnswer,
            ModifierKeys.Ctrl | ModifierKeys.Alt, VirtualKey.G) };

        var resolved = HotkeyDefaults.Resolve(overrides);

        var changed = resolved.Single(b => b.Id == HotkeyId.GenerateAnswer);
        changed.Modifiers.Should().Be(ModifierKeys.Ctrl | ModifierKeys.Alt);
        changed.Key.Should().Be(VirtualKey.G);
        changed.Description.Should().Be(
            HotkeyDefaults.All.Single(b => b.Id == HotkeyId.GenerateAnswer).Description);

        // Every other binding is untouched.
        resolved.Where(b => b.Id != HotkeyId.GenerateAnswer)
            .Should().BeEquivalentTo(HotkeyDefaults.All.Where(b => b.Id != HotkeyId.GenerateAnswer));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~HotkeyResolveTests"`
Expected: FAIL — `HotkeyOverride` / `Resolve` don't exist.

- [ ] **Step 3: Add `HotkeyOverride` and `Resolve`**

In `HotkeyDefaults.cs`, add the record above the `HotkeyDefaults` class (after `HotkeyBinding`):

```csharp
/// <summary>A user override of one action's default global-hotkey chord. Persisted in settings;
/// merged against <see cref="HotkeyDefaults.All"/> by <see cref="HotkeyDefaults.Resolve"/>.</summary>
/// <param name="Id">The action whose chord is overridden.</param>
/// <param name="Modifiers">The replacement modifier keys.</param>
/// <param name="Key">The replacement virtual key.</param>
public sealed record HotkeyOverride(HotkeyId Id, ModifierKeys Modifiers, VirtualKey Key);
```

Inside the `HotkeyDefaults` class, add:

```csharp
    /// <summary>The effective bindings = <see cref="All"/> with any matching <see cref="HotkeyOverride.Id"/>
    /// replaced by the override's chord. Descriptions always come from the defaults.</summary>
    public static IReadOnlyList<HotkeyBinding> Resolve(IReadOnlyList<HotkeyOverride> overrides)
    {
        if (overrides is null || overrides.Count == 0) return All;
        var map = overrides.GroupBy(o => o.Id).ToDictionary(g => g.Key, g => g.First());
        return All.Select(b => map.TryGetValue(b.Id, out var o)
            ? b with { Modifiers = o.Modifiers, Key = o.Key }
            : b).ToList();
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~HotkeyResolveTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Application/Abstractions/HotkeyDefaults.cs \
        tests/AIHelperNET.Application.Tests/Abstractions/HotkeyResolveTests.cs
git commit -m "feat(hotkeys): HotkeyOverride + Resolve merge"
```

---

## Task 3: Persist `HotkeyOverrides` on `AppSettingsDto`

**Files:**
- Modify: `src/AIHelperNET.Application/Sessions/Dtos/AppSettingsDto.cs`
- Test: `tests/AIHelperNET.Application.Tests/AppSettingsHotkeyOverridesTests.cs` (new)

- [ ] **Step 1: Write the failing test**

Create `tests/AIHelperNET.Application.Tests/AppSettingsHotkeyOverridesTests.cs`:

```csharp
using System.Text.Json;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests;

public class AppSettingsHotkeyOverridesTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static AppSettingsDto Base() => new(
        AiBackend.Claude, WhisperModelSize.Medium, AnswerSettings.Default, CodeProfile.Empty, null, null);

    [Fact]
    public void HotkeyOverrides_RoundTripThroughJson()
    {
        var dto = Base() with
        {
            HotkeyOverrides = [new HotkeyOverride(HotkeyId.GenerateAnswer,
                ModifierKeys.Ctrl | ModifierKeys.Alt, VirtualKey.G)]
        };

        var back = JsonSerializer.Deserialize<AppSettingsDto>(JsonSerializer.Serialize(dto, Web), Web)!;

        back.HotkeyOverrides.Should().ContainSingle()
            .Which.Should().Be(new HotkeyOverride(HotkeyId.GenerateAnswer,
                ModifierKeys.Ctrl | ModifierKeys.Alt, VirtualKey.G));
    }

    [Fact]
    public void Normalized_DropsInvalidEnumAndDuplicateIdOverrides()
    {
        var dto = Base() with
        {
            HotkeyOverrides =
            [
                new HotkeyOverride(HotkeyId.GenerateAnswer, ModifierKeys.Ctrl, VirtualKey.G),
                new HotkeyOverride(HotkeyId.GenerateAnswer, ModifierKeys.Ctrl, VirtualKey.J), // dup Id → dropped
                new HotkeyOverride((HotkeyId)999, ModifierKeys.Ctrl, VirtualKey.G),            // bad Id → dropped
                new HotkeyOverride(HotkeyId.CopyAnswer, (ModifierKeys)0x40, VirtualKey.G),     // bad modifier bit → dropped
                new HotkeyOverride(HotkeyId.ToggleOverlay, ModifierKeys.Ctrl, (VirtualKey)0x07) // bad key → dropped
            ]
        };

        var n = dto.Normalized();

        n.HotkeyOverrides.Should().ContainSingle()
            .Which.Should().Be(new HotkeyOverride(HotkeyId.GenerateAnswer, ModifierKeys.Ctrl, VirtualKey.G));
    }

    [Fact]
    public void MissingField_DefaultsToEmpty()
    {
        // JSON without the property → empty list, no crash.
        const string json = """{"activeBackend":0,"whisperModel":2,"micDeviceId":null,"loopbackDeviceId":null}""";
        var dto = JsonSerializer.Deserialize<AppSettingsDto>(json, Web)!;
        dto.HotkeyOverrides.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~AppSettingsHotkeyOverridesTests"`
Expected: FAIL — `HotkeyOverrides` property doesn't exist.

- [ ] **Step 3: Add the init-prop + normalization**

In `AppSettingsDto.cs`, mirror the existing `Presets` init-prop (so the positional constructor signature is unchanged). After the `Presets` property (line 47) add:

```csharp
    /// <summary>User overrides of the default global-hotkey chords. Empty ⇒ all defaults.</summary>
    public IReadOnlyList<HotkeyOverride> HotkeyOverrides { get; init; } = [];
```

Extend `Normalized()` to also normalize overrides:

```csharp
    public AppSettingsDto Normalized() => this with
    {
        MaxAnswerTokens = MaxAnswerTokens <= 0
            ? DefaultMaxAnswerTokens
            : Math.Clamp(MaxAnswerTokens, MinAnswerTokens, MaxAnswerTokensLimit),
        LatestQuestionWindowSeconds = LatestQuestionWindowSeconds <= 0
            ? DefaultLatestQuestionWindowSeconds
            : Math.Clamp(LatestQuestionWindowSeconds, MinLatestQuestionWindowSeconds, MaxLatestQuestionWindowSeconds),
        HotkeyOverrides = NormalizeOverrides(HotkeyOverrides)
    };

    private static IReadOnlyList<HotkeyOverride> NormalizeOverrides(IReadOnlyList<HotkeyOverride> raw)
    {
        const uint modMask = (uint)(ModifierKeys.Alt | ModifierKeys.Ctrl | ModifierKeys.Shift | ModifierKeys.Win);
        var seen = new HashSet<HotkeyId>();
        var result = new List<HotkeyOverride>();
        foreach (var o in raw ?? [])
        {
            if (!Enum.IsDefined(o.Id)) continue;            // unknown action
            if (((uint)o.Modifiers & ~modMask) != 0) continue; // stray modifier bits
            if (!Enum.IsDefined(o.Key)) continue;           // unknown key
            if (!seen.Add(o.Id)) continue;                  // keep first per action
            result.Add(o);
        }
        return result;
    }
```

(`ModifierKeys`, `HotkeyOverride`, `VirtualKey`, `HotkeyId` are already in scope via the existing `using AIHelperNET.Application.Abstractions;` at the top of the file.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~AppSettingsHotkeyOverridesTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Application/Sessions/Dtos/AppSettingsDto.cs \
        tests/AIHelperNET.Application.Tests/AppSettingsHotkeyOverridesTests.cs
git commit -m "feat(settings): persist + normalize hotkey overrides"
```

---

## Task 4: `HotkeyValidator`

**Files:**
- Create: `src/AIHelperNET.Application/Abstractions/HotkeyValidator.cs`
- Test: `tests/AIHelperNET.Application.Tests/Abstractions/HotkeyValidatorTests.cs` (new)

- [ ] **Step 1: Write the failing test**

Create `tests/AIHelperNET.Application.Tests/Abstractions/HotkeyValidatorTests.cs`:

```csharp
using AIHelperNET.Application.Abstractions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Abstractions;

public class HotkeyValidatorTests
{
    [Fact]
    public void Defaults_AreValid()
        => HotkeyValidator.Validate(HotkeyDefaults.All).Should().BeEmpty();

    [Fact]
    public void BareKey_IsFlagged()
    {
        var bindings = new[]
        {
            new HotkeyBinding(HotkeyId.GenerateAnswer, ModifierKeys.None, VirtualKey.G, "Generate")
        };

        var errors = HotkeyValidator.Validate(bindings);

        errors.Should().ContainKey(HotkeyId.GenerateAnswer);
        errors[HotkeyId.GenerateAnswer].Should().Contain("modifier");
    }

    [Fact]
    public void DuplicateChord_FlagsBothActions()
    {
        var bindings = new[]
        {
            new HotkeyBinding(HotkeyId.GenerateAnswer, ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.G, "Generate"),
            new HotkeyBinding(HotkeyId.CopyAnswer,     ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.G, "Copy")
        };

        var errors = HotkeyValidator.Validate(bindings);

        errors.Should().ContainKeys(HotkeyId.GenerateAnswer, HotkeyId.CopyAnswer);
        errors[HotkeyId.GenerateAnswer].Should().Contain("Copy");
        errors[HotkeyId.CopyAnswer].Should().Contain("Generate");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~HotkeyValidatorTests"`
Expected: FAIL — `HotkeyValidator` doesn't exist.

- [ ] **Step 3: Create the validator**

Create `src/AIHelperNET.Application/Abstractions/HotkeyValidator.cs`:

```csharp
namespace AIHelperNET.Application.Abstractions;

/// <summary>Validates a proposed set of hotkey bindings before they are registered. Pure — no Win32,
/// no UI. OS/other-app conflicts are not knowable here; they surface only at registration time.</summary>
public static class HotkeyValidator
{
    /// <summary>Returns a map of <see cref="HotkeyId"/> → error message for every invalid binding.
    /// An empty map means the whole set is valid.</summary>
    public static IReadOnlyDictionary<HotkeyId, string> Validate(IReadOnlyList<HotkeyBinding> bindings)
    {
        var errors = new Dictionary<HotkeyId, string>();

        // Rule 1: a global hotkey needs at least one modifier, or it would hijack a bare key everywhere.
        foreach (var b in bindings)
            if (b.Modifiers == ModifierKeys.None)
                errors[b.Id] = "Add a modifier (Ctrl, Shift, Alt, or Win).";

        // Rule 2: no two actions may share the same chord.
        foreach (var group in bindings.GroupBy(b => (b.Modifiers, b.Key)).Where(g => g.Count() > 1))
        {
            var members = group.ToList();
            foreach (var b in members)
            {
                var other = members.First(m => m.Id != b.Id);
                errors[b.Id] = $"Same shortcut as “{other.Description}”.";
            }
        }

        return errors;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~HotkeyValidatorTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Application/Abstractions/HotkeyValidator.cs \
        tests/AIHelperNET.Application.Tests/Abstractions/HotkeyValidatorTests.cs
git commit -m "feat(hotkeys): HotkeyValidator (modifier required, no duplicates)"
```

---

## Task 5: `IHotkeyApplier` + `HotkeyApplier`

**Files:**
- Create: `src/AIHelperNET.App/Hotkeys/IHotkeyApplier.cs`
- Create: `src/AIHelperNET.App/Hotkeys/HotkeyApplier.cs`
- Test: `tests/AIHelperNET.App.Tests/HotkeyApplierTests.cs` (new)

- [ ] **Step 1: Write the failing test**

Create `tests/AIHelperNET.App.Tests/HotkeyApplierTests.cs`:

```csharp
using AIHelperNET.App.Hotkeys;
using AIHelperNET.Application.Abstractions;
using FluentAssertions;
using FluentResults;
using Xunit;

namespace AIHelperNET.App.Tests;

public class HotkeyApplierTests
{
    /// <summary>Fake hotkey service: records registrations and fails the chords named in <see cref="FailIds"/>.</summary>
    private sealed class FakeHotkeyService : IGlobalHotkeyService
    {
        public int UnregisterCalls;
        public readonly List<HotkeyId> Registered = [];
        public HashSet<HotkeyId> FailIds = [];

#pragma warning disable CS0067
        public event EventHandler<HotkeyId>? HotkeyPressed;
#pragma warning restore CS0067

        public Result Register(HotkeyId id, ModifierKeys modifiers, VirtualKey key)
        {
            if (FailIds.Contains(id)) return Result.Fail("in use");
            Registered.Add(id);
            return Result.Ok();
        }

        public void UnregisterAll() { UnregisterCalls++; Registered.Clear(); }
    }

    private static HotkeyBinding B(HotkeyId id) =>
        HotkeyDefaults.All.Single(b => b.Id == id);

    [Fact]
    public void Apply_UnregistersThenRegistersAll_ReturnsNoFailures()
    {
        var svc = new FakeHotkeyService();
        var applier = new HotkeyApplier(svc);

        var failures = applier.Apply(HotkeyDefaults.All);

        failures.Should().BeEmpty();
        svc.UnregisterCalls.Should().Be(1);
        svc.Registered.Should().BeEquivalentTo(HotkeyDefaults.All.Select(b => b.Id));
    }

    [Fact]
    public void Apply_ReturnsIds_TheOsRejected()
    {
        var svc = new FakeHotkeyService { FailIds = [HotkeyId.CaptureScreen] };
        var applier = new HotkeyApplier(svc);

        var failures = applier.Apply(HotkeyDefaults.All);

        failures.Should().ContainSingle().Which.Should().Be(HotkeyId.CaptureScreen);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.App.Tests --filter "FullyQualifiedName~HotkeyApplierTests"`
Expected: FAIL — `IHotkeyApplier`/`HotkeyApplier` don't exist.

- [ ] **Step 3: Create the port and implementation**

Create `src/AIHelperNET.App/Hotkeys/IHotkeyApplier.cs`:

```csharp
using AIHelperNET.Application.Abstractions;

namespace AIHelperNET.App.Hotkeys;

/// <summary>Re-registers the full set of global hotkeys atomically.</summary>
public interface IHotkeyApplier
{
    /// <summary>Unregisters all hotkeys, then registers <paramref name="bindings"/>. Returns the IDs the
    /// OS rejected (already in use by Windows/another app); an empty list means full success.</summary>
    IReadOnlyList<HotkeyId> Apply(IReadOnlyList<HotkeyBinding> bindings);
}
```

Create `src/AIHelperNET.App/Hotkeys/HotkeyApplier.cs`:

```csharp
using AIHelperNET.Application.Abstractions;

namespace AIHelperNET.App.Hotkeys;

/// <summary>Applies hotkey bindings via the live <see cref="IGlobalHotkeyService"/> (Win32). The service
/// must already be initialized with the overlay window handle before the first <see cref="Apply"/>.</summary>
public sealed class HotkeyApplier(IGlobalHotkeyService hotkeys) : IHotkeyApplier
{
    /// <inheritdoc/>
    public IReadOnlyList<HotkeyId> Apply(IReadOnlyList<HotkeyBinding> bindings)
    {
        hotkeys.UnregisterAll();
        var failed = new List<HotkeyId>();
        foreach (var b in bindings)
            if (hotkeys.Register(b.Id, b.Modifiers, b.Key).IsFailed)
                failed.Add(b.Id);
        return failed;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.App.Tests --filter "FullyQualifiedName~HotkeyApplierTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.App/Hotkeys/IHotkeyApplier.cs \
        src/AIHelperNET.App/Hotkeys/HotkeyApplier.cs \
        tests/AIHelperNET.App.Tests/HotkeyApplierTests.cs
git commit -m "feat(hotkeys): IHotkeyApplier wrapping GlobalHotkeyService"
```

---

## Task 6: `KeyGestureCapture` (WPF → enum translator)

**Files:**
- Create: `src/AIHelperNET.App/Hotkeys/KeyGestureCapture.cs`
- Test: `tests/AIHelperNET.App.Tests/KeyGestureCaptureTests.cs` (new)

- [ ] **Step 1: Write the failing test**

Create `tests/AIHelperNET.App.Tests/KeyGestureCaptureTests.cs`:

```csharp
using System.Windows.Input;
using AIHelperNET.App.Hotkeys;
using AIHelperNET.Application.Abstractions;
using FluentAssertions;
using Xunit;
using WpfModifiers = System.Windows.Input.ModifierKeys;

namespace AIHelperNET.App.Tests;

public class KeyGestureCaptureTests
{
    [Fact]
    public void TryTranslate_LetterWithCtrlShift_Maps()
    {
        var ok = KeyGestureCapture.TryTranslate(Key.G, WpfModifiers.Control | WpfModifiers.Shift,
            out var mods, out var key);

        ok.Should().BeTrue();
        mods.Should().Be(ModifierKeys.Ctrl | ModifierKeys.Shift);
        key.Should().Be(VirtualKey.G);
    }

    [Fact]
    public void TryTranslate_Digit_Maps()
    {
        var ok = KeyGestureCapture.TryTranslate(Key.D5, WpfModifiers.Control, out var mods, out var key);
        ok.Should().BeTrue();
        mods.Should().Be(ModifierKeys.Ctrl);
        key.Should().Be(VirtualKey.D5);
    }

    [Fact]
    public void TryTranslate_UnsupportedKey_ReturnsFalse()
        => KeyGestureCapture.TryTranslate(Key.OemTilde, WpfModifiers.Control, out _, out _)
            .Should().BeFalse();

    [Theory]
    [InlineData(Key.LeftCtrl, true)]
    [InlineData(Key.RightShift, true)]
    [InlineData(Key.LWin, true)]
    [InlineData(Key.System, true)]   // Alt is delivered as Key.System in WPF
    [InlineData(Key.G, false)]
    public void IsModifierKey_DetectsModifiers(Key key, bool expected)
        => KeyGestureCapture.IsModifierKey(key).Should().Be(expected);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.App.Tests --filter "FullyQualifiedName~KeyGestureCaptureTests"`
Expected: FAIL — `KeyGestureCapture` doesn't exist.

- [ ] **Step 3: Create the translator**

Create `src/AIHelperNET.App/Hotkeys/KeyGestureCapture.cs`:

```csharp
using System.Windows.Input;
using AIHelperNET.Application.Abstractions;
using WpfModifiers = System.Windows.Input.ModifierKeys;

namespace AIHelperNET.App.Hotkeys;

/// <summary>Translates a WPF key press into the app's <see cref="ModifierKeys"/>/<see cref="VirtualKey"/>.
/// Static and window-free so it can be unit-tested headless.</summary>
public static class KeyGestureCapture
{
    /// <summary>True when <paramref name="key"/> is itself a modifier (Ctrl/Shift/Alt/Win) — the recorder
    /// should keep waiting for the real key rather than treat it as the binding. (Alt arrives as
    /// <see cref="Key.System"/>.)</summary>
    public static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;

    /// <summary>Tries to translate <paramref name="key"/> + <paramref name="wpfMods"/> into a bindable chord.
    /// Returns false for keys outside the bindable set (see <see cref="HotkeyKeys.Selectable"/>).</summary>
    public static bool TryTranslate(Key key, WpfModifiers wpfMods, out ModifierKeys mods, out VirtualKey vk)
    {
        mods = ModifierKeys.None;
        if (wpfMods.HasFlag(WpfModifiers.Control)) mods |= ModifierKeys.Ctrl;
        if (wpfMods.HasFlag(WpfModifiers.Shift))   mods |= ModifierKeys.Shift;
        if (wpfMods.HasFlag(WpfModifiers.Alt))     mods |= ModifierKeys.Alt;
        if (wpfMods.HasFlag(WpfModifiers.Windows)) mods |= ModifierKeys.Win;

        var code = KeyInterop.VirtualKeyFromKey(key);
        if (code != 0 && Enum.IsDefined((VirtualKey)(uint)code))
        {
            vk = (VirtualKey)(uint)code;
            return true;
        }
        vk = default;
        return false;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.App.Tests --filter "FullyQualifiedName~KeyGestureCaptureTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.App/Hotkeys/KeyGestureCapture.cs \
        tests/AIHelperNET.App.Tests/KeyGestureCaptureTests.cs
git commit -m "feat(hotkeys): KeyGestureCapture WPF->enum translator"
```

---

## Task 7: `HotkeyRowViewModel`

**Files:**
- Create: `src/AIHelperNET.App/ViewModels/HotkeyRowViewModel.cs`
- Test: `tests/AIHelperNET.App.Tests/HotkeyRowViewModelTests.cs` (new)

- [ ] **Step 1: Write the failing test**

Create `tests/AIHelperNET.App.Tests/HotkeyRowViewModelTests.cs`:

```csharp
using AIHelperNET.App.ViewModels;
using AIHelperNET.Application.Abstractions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.App.Tests;

public class HotkeyRowViewModelTests
{
    private static HotkeyRowViewModel Row() =>
        HotkeyRowViewModel.FromBinding(HotkeyDefaults.All.Single(b => b.Id == HotkeyId.GenerateAnswer));

    [Fact]
    public void FromBinding_PopulatesFlagsAndKey()
    {
        var row = Row(); // default Ctrl+Shift+Q
        row.Id.Should().Be(HotkeyId.GenerateAnswer);
        row.Ctrl.Should().BeTrue();
        row.Shift.Should().BeTrue();
        row.Alt.Should().BeFalse();
        row.Win.Should().BeFalse();
        row.SelectedKey.Should().Be(VirtualKey.Q);
        row.Gesture.Should().Be("Ctrl+Shift+Q");
    }

    [Fact]
    public void ChangingFlags_UpdatesGesture_AndModifiers()
    {
        var row = Row();
        row.Shift = false;
        row.Alt = true;

        row.ToModifiers().Should().Be(ModifierKeys.Ctrl | ModifierKeys.Alt);
        row.Gesture.Should().Be("Ctrl+Alt+Q");
    }

    [Fact]
    public void SetChord_ReplacesEverything_AndClearsError()
    {
        var row = Row();
        row.ErrorMessage = "boom";

        row.SetChord(ModifierKeys.Win, VirtualKey.D5);

        row.Ctrl.Should().BeFalse();
        row.Win.Should().BeTrue();
        row.SelectedKey.Should().Be(VirtualKey.D5);
        row.Gesture.Should().Be("Win+5");
        row.ErrorMessage.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.App.Tests --filter "FullyQualifiedName~HotkeyRowViewModelTests"`
Expected: FAIL — `HotkeyRowViewModel` doesn't exist.

- [ ] **Step 3: Create the row VM**

Create `src/AIHelperNET.App/ViewModels/HotkeyRowViewModel.cs`:

```csharp
using AIHelperNET.Application.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIHelperNET.App.ViewModels;

/// <summary>One editable row in the Settings → Shortcuts tab: an action and its current chord.</summary>
public sealed partial class HotkeyRowViewModel : ObservableObject
{
    /// <summary>The action this row binds.</summary>
    public HotkeyId Id { get; }

    /// <summary>Human-readable action name (read-only).</summary>
    public string Description { get; }

    /// <summary>All bindable keys for the dropdown.</summary>
    public IReadOnlyList<KeyChoice> KeyChoices => HotkeyKeys.Selectable;

    [ObservableProperty] private bool _ctrl;
    [ObservableProperty] private bool _shift;
    [ObservableProperty] private bool _alt;
    [ObservableProperty] private bool _win;
    [ObservableProperty] private VirtualKey _selectedKey;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isRecording;

    private HotkeyRowViewModel(HotkeyId id, string description)
    {
        Id = id;
        Description = description;
    }

    /// <summary>Builds a row from a binding (used for both defaults and resolved/overridden chords).</summary>
    public static HotkeyRowViewModel FromBinding(HotkeyBinding b)
    {
        var row = new HotkeyRowViewModel(b.Id, b.Description);
        row.SetChord(b.Modifiers, b.Key);
        return row;
    }

    /// <summary>The combined modifier flags from the four checkboxes.</summary>
    public ModifierKeys ToModifiers()
    {
        var m = ModifierKeys.None;
        if (Ctrl)  m |= ModifierKeys.Ctrl;
        if (Shift) m |= ModifierKeys.Shift;
        if (Alt)   m |= ModifierKeys.Alt;
        if (Win)   m |= ModifierKeys.Win;
        return m;
    }

    /// <summary>Replaces the whole chord at once and clears any error (used by reset + recorder).</summary>
    public void SetChord(ModifierKeys mods, VirtualKey key)
    {
        Ctrl  = mods.HasFlag(ModifierKeys.Ctrl);
        Shift = mods.HasFlag(ModifierKeys.Shift);
        Alt   = mods.HasFlag(ModifierKeys.Alt);
        Win   = mods.HasFlag(ModifierKeys.Win);
        SelectedKey = key;
        ErrorMessage = null;
    }

    /// <summary>The current chord as a binding (description carried from the default).</summary>
    public HotkeyBinding ToBinding() => new(Id, ToModifiers(), SelectedKey, Description);

    /// <summary>The display gesture, e.g. <c>"Ctrl+Shift+Q"</c>.</summary>
    public string Gesture => ToBinding().Gesture;

    partial void OnCtrlChanged(bool value)  => OnPropertyChanged(nameof(Gesture));
    partial void OnShiftChanged(bool value) => OnPropertyChanged(nameof(Gesture));
    partial void OnAltChanged(bool value)   => OnPropertyChanged(nameof(Gesture));
    partial void OnWinChanged(bool value)   => OnPropertyChanged(nameof(Gesture));
    partial void OnSelectedKeyChanged(VirtualKey value) => OnPropertyChanged(nameof(Gesture));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.App.Tests --filter "FullyQualifiedName~HotkeyRowViewModelTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.App/ViewModels/HotkeyRowViewModel.cs \
        tests/AIHelperNET.App.Tests/HotkeyRowViewModelTests.cs
git commit -m "feat(hotkeys): HotkeyRowViewModel"
```

---

## Task 8: Wire rebinding into `SettingsViewModel`

**Files:**
- Modify: `src/AIHelperNET.App/ViewModels/SettingsViewModel.cs`
- Modify: `tests/AIHelperNET.App.Tests/SettingsViewModelTokenTests.cs` (constructor + add stub)
- Test: `tests/AIHelperNET.App.Tests/SettingsViewModelHotkeyTests.cs` (new)

> **Save ordering note (refines spec §6):** validate → **apply** → only **persist** if registration succeeded. This is stricter than the spec's "persist then apply" and prevents writing an unregisterable chord to `settings.json`. Same user-visible behavior (no dead hotkey, inline error, revert to last-good).

- [ ] **Step 1: Add the shared test stub + fix existing constructions**

In `tests/AIHelperNET.App.Tests/SettingsViewModelTokenTests.cs`, add a stub class at the bottom of the file (after `SettingsViewModelWindowTests`):

```csharp
/// <summary>No-op applier for VM tests that don't exercise registration; records the last applied set
/// and can be told which IDs to "fail".</summary>
internal sealed class StubHotkeyApplier : AIHelperNET.App.Hotkeys.IHotkeyApplier
{
    public IReadOnlyList<HotkeyBinding>? LastApplied;
    public int ApplyCalls;
    public IReadOnlyList<HotkeyId> Failures = [];

    public IReadOnlyList<HotkeyId> Apply(IReadOnlyList<HotkeyBinding> bindings)
    {
        ApplyCalls++;
        LastApplied = bindings;
        return Failures;
    }
}
```

Update the four `new SettingsViewModel(mediator)` / `new SettingsViewModel(mediator) { ... }` sites in this file to pass a stub:
- `new SettingsViewModel(mediator)` → `new SettingsViewModel(mediator, new StubHotkeyApplier())`
- `new SettingsViewModel(mediator) { MaxAnswerTokens = 1500 }` → `new SettingsViewModel(mediator, new StubHotkeyApplier()) { MaxAnswerTokens = 1500 }`
- `new SettingsViewModel(mediator) { LatestQuestionWindowSeconds = 200 }` → `new SettingsViewModel(mediator, new StubHotkeyApplier()) { LatestQuestionWindowSeconds = 200 }`

- [ ] **Step 2: Write the failing hotkey VM tests**

Create `tests/AIHelperNET.App.Tests/SettingsViewModelHotkeyTests.cs`:

```csharp
using AIHelperNET.App.ViewModels;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Application.Sessions.Queries;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using FluentResults;
using Mediator;
using NSubstitute;
using Xunit;

namespace AIHelperNET.App.Tests;

public class SettingsViewModelHotkeyTests
{
    private static (IMediator mediator, AppSettingsDto settings) Mocked(AppSettingsDto settings)
    {
        var mediator = Substitute.For<IMediator>();
#pragma warning disable CA2012
        mediator.Send(Arg.Any<GetSettingsQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<AppSettingsDto>>(Result.Ok(settings)));
        mediator.Send(Arg.Any<HasApiKeyQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<bool>>(Result.Ok(false)));
        mediator.Send(Arg.Any<SaveSettingsCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Ok()));
#pragma warning restore CA2012
        return (mediator, settings);
    }

    private static AppSettingsDto BaseSettings() => new(
        AiBackend.Claude, WhisperModelSize.Medium, AnswerSettings.Default, CodeProfile.Empty, null, null);

    [Fact]
    public async Task LoadAsync_BuildsOneRowPerAction_WithOverrideApplied()
    {
        var settings = BaseSettings() with
        {
            HotkeyOverrides = [new HotkeyOverride(HotkeyId.GenerateAnswer,
                ModifierKeys.Ctrl | ModifierKeys.Alt, VirtualKey.G)]
        };
        var (mediator, _) = Mocked(settings);
        var vm = new SettingsViewModel(mediator, new StubHotkeyApplier());

        await vm.LoadAsync();

        vm.HotkeyRows.Should().HaveCount(Enum.GetValues<HotkeyId>().Length);
        vm.HotkeyRows.Single(r => r.Id == HotkeyId.GenerateAnswer).Gesture.Should().Be("Ctrl+Alt+G");
        vm.HotkeyRows.Single(r => r.Id == HotkeyId.CopyAnswer).Gesture.Should().Be("Ctrl+Shift+C");
    }

    [Fact]
    public async Task SaveSettingsAsync_WithDuplicateChord_ShowsErrors_DoesNotPersistOrApply()
    {
        var (mediator, _) = Mocked(BaseSettings());
        var applier = new StubHotkeyApplier();
        var vm = new SettingsViewModel(mediator, applier);
        await vm.LoadAsync();

        // Make CopyAnswer collide with GenerateAnswer (Ctrl+Shift+Q).
        var copy = vm.HotkeyRows.Single(r => r.Id == HotkeyId.CopyAnswer);
        copy.SetChord(ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.Q);

        await vm.SaveSettingsAsync();

        copy.ErrorMessage.Should().NotBeNullOrEmpty();
        applier.ApplyCalls.Should().Be(0);
        await mediator.DidNotReceive().Send(Arg.Any<SaveSettingsCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveSettingsAsync_Clean_PersistsOverrides_AndApplies()
    {
        var (mediator, _) = Mocked(BaseSettings());
        var applier = new StubHotkeyApplier();
        var vm = new SettingsViewModel(mediator, applier);
        await vm.LoadAsync();

        vm.HotkeyRows.Single(r => r.Id == HotkeyId.GenerateAnswer)
            .SetChord(ModifierKeys.Ctrl | ModifierKeys.Alt, VirtualKey.G);

        await vm.SaveSettingsAsync();

        applier.ApplyCalls.Should().Be(1);
        await mediator.Received(1).Send(
            Arg.Is<SaveSettingsCommand>(c =>
                c.Settings.HotkeyOverrides.Count == 1 &&
                c.Settings.HotkeyOverrides[0].Id == HotkeyId.GenerateAnswer &&
                c.Settings.HotkeyOverrides[0].Key == VirtualKey.G),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveSettingsAsync_WhenApplierReportsConflict_ShowsRowError_AndReverts()
    {
        var (mediator, _) = Mocked(BaseSettings());
        var applier = new StubHotkeyApplier { Failures = [HotkeyId.GenerateAnswer] };
        var vm = new SettingsViewModel(mediator, applier);
        await vm.LoadAsync();

        vm.HotkeyRows.Single(r => r.Id == HotkeyId.GenerateAnswer)
            .SetChord(ModifierKeys.Ctrl | ModifierKeys.Alt, VirtualKey.G);

        await vm.SaveSettingsAsync();

        vm.HotkeyRows.Single(r => r.Id == HotkeyId.GenerateAnswer).ErrorMessage.Should().NotBeNullOrEmpty();
        applier.ApplyCalls.Should().Be(2); // attempt + revert to last-good
        await mediator.DidNotReceive().Send(Arg.Any<SaveSettingsCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetRow_RestoresDefault_ResetAll_RestoresEverything()
    {
        var (mediator, _) = Mocked(BaseSettings());
        var vm = new SettingsViewModel(mediator, new StubHotkeyApplier());
        await vm.LoadAsync();

        var gen = vm.HotkeyRows.Single(r => r.Id == HotkeyId.GenerateAnswer);
        gen.SetChord(ModifierKeys.Win, VirtualKey.J);

        vm.ResetRowCommand.Execute(gen);
        gen.Gesture.Should().Be("Ctrl+Shift+Q");

        vm.HotkeyRows.Single(r => r.Id == HotkeyId.CaptureScreen).SetChord(ModifierKeys.Win, VirtualKey.K);
        vm.ResetAllHotkeysCommand.Execute(null);
        vm.HotkeyRows.Single(r => r.Id == HotkeyId.CaptureScreen).Gesture.Should().Be("Ctrl+Shift+S");
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/AIHelperNET.App.Tests --filter "FullyQualifiedName~SettingsViewModelHotkeyTests"`
Expected: FAIL — constructor signature, `HotkeyRows`, commands don't exist yet.

- [ ] **Step 4: Implement the VM changes**

In `SettingsViewModel.cs`:

(a) Add usings at the top (alongside existing ones):
```csharp
using System.Collections.Generic;
using System.Linq;
using AIHelperNET.App.Hotkeys;
```

(b) Change the class declaration to inject the applier:
```csharp
public sealed partial class SettingsViewModel(IMediator mediator, IHotkeyApplier hotkeyApplier) : ObservableObject
```

(c) Replace the read-only `Hotkeys` property (lines 18-21) with the editable collection + last-good tracker:
```csharp
    /// <summary>Editable shortcut rows, one per action, shown in the Shortcuts tab.</summary>
    public ObservableCollection<HotkeyRowViewModel> HotkeyRows { get; } = [];

    /// <summary>The last successfully-registered effective set, used to revert on an OS conflict.</summary>
    private IReadOnlyList<HotkeyBinding> _lastGoodBindings = HotkeyDefaults.All;
```

(d) In `LoadAsync`, after the `Presets` block (around line 109) and before `await RefreshKeyStatusAsync();`, build the rows:
```csharp
        var effective = HotkeyDefaults.Resolve(s.HotkeyOverrides);
        _lastGoodBindings = effective;
        HotkeyRows.Clear();
        foreach (var b in effective) HotkeyRows.Add(HotkeyRowViewModel.FromBinding(b));
```

(e) In `SaveSettingsAsync`, **before** building the `dto`, insert the hotkey validate→apply gate and compute the overrides to save:
```csharp
        IReadOnlyList<HotkeyOverride> hotkeyOverridesToSave;
        if (HotkeyRows.Count > 0)
        {
            var proposed = HotkeyRows.Select(r => r.ToBinding()).ToList();

            var errors = HotkeyValidator.Validate(proposed);
            if (errors.Count > 0)
            {
                foreach (var row in HotkeyRows)
                    row.ErrorMessage = errors.TryGetValue(row.Id, out var msg) ? msg : null;
                StatusMessage = "Fix the highlighted shortcut conflicts, then Save.";
                return;
            }
            foreach (var row in HotkeyRows) row.ErrorMessage = null;

            var failed = hotkeyApplier.Apply(proposed);
            if (failed.Count > 0)
            {
                foreach (var row in HotkeyRows)
                    if (failed.Contains(row.Id))
                        row.ErrorMessage = "Already in use by Windows or another app — pick a different chord.";
                hotkeyApplier.Apply(_lastGoodBindings); // revert so no action is left unregistered
                StatusMessage = "Some shortcuts are in use by another app — not saved.";
                return;
            }

            _lastGoodBindings = proposed;
            var defaults = HotkeyDefaults.All.ToDictionary(b => b.Id);
            hotkeyOverridesToSave = proposed
                .Where(b => b.Modifiers != defaults[b.Id].Modifiers || b.Key != defaults[b.Id].Key)
                .Select(b => new HotkeyOverride(b.Id, b.Modifiers, b.Key))
                .ToList();
        }
        else
        {
            hotkeyOverridesToSave = current?.HotkeyOverrides ?? [];
        }
```

(f) In the `dto` initializer, add the overrides next to `Presets`:
```csharp
        {
            Presets = [.. Presets],
            HotkeyOverrides = [.. hotkeyOverridesToSave]
        };
```

(g) Add the recorder + reset commands and the recording helpers at the end of the class (before the closing brace):
```csharp
    // ── Shortcut editing ──────────────────────────────────────────
    /// <summary>True while any row is capturing a key press.</summary>
    public bool IsAnyRowRecording => HotkeyRows.Any(r => r.IsRecording);

    [RelayCommand]
    private void StartRecording(HotkeyRowViewModel? row)
    {
        if (row is null) return;
        foreach (var r in HotkeyRows) r.IsRecording = false;
        row.ErrorMessage = null;
        row.IsRecording = true;
    }

    [RelayCommand]
    private void ResetRow(HotkeyRowViewModel? row)
    {
        if (row is null) return;
        var d = HotkeyDefaults.All.Single(b => b.Id == row.Id);
        row.SetChord(d.Modifiers, d.Key);
    }

    [RelayCommand]
    private void ResetAllHotkeys()
    {
        var defaults = HotkeyDefaults.All.ToDictionary(b => b.Id);
        foreach (var row in HotkeyRows) row.SetChord(defaults[row.Id].Modifiers, defaults[row.Id].Key);
    }

    /// <summary>Called by the window's key handler when a complete chord is captured.</summary>
    public void ApplyRecordedChord(ModifierKeys mods, VirtualKey key)
    {
        var row = HotkeyRows.FirstOrDefault(r => r.IsRecording);
        if (row is null) return;
        row.SetChord(mods, key);
        row.IsRecording = false;
    }

    /// <summary>Called by the window's key handler when an unsupported key is pressed while recording.</summary>
    public void SetRecordingError(string message)
    {
        var row = HotkeyRows.FirstOrDefault(r => r.IsRecording);
        if (row is not null) row.ErrorMessage = message;
    }
```

- [ ] **Step 5: Run the full App.Tests project**

Run: `dotnet test tests/AIHelperNET.App.Tests`
Expected: PASS — new hotkey tests green; existing token/window tests still green with the stub.

- [ ] **Step 6: Commit**

```bash
git add src/AIHelperNET.App/ViewModels/SettingsViewModel.cs \
        tests/AIHelperNET.App.Tests/SettingsViewModelTokenTests.cs \
        tests/AIHelperNET.App.Tests/SettingsViewModelHotkeyTests.cs
git commit -m "feat(hotkeys): editable rows + validate/apply/reset in SettingsViewModel"
```

---

## Task 9: DI registration + startup wiring

**Files:**
- Modify: `src/AIHelperNET.App/DependencyInjection.cs`
- Modify: `src/AIHelperNET.App/App.xaml.cs` (WireHotkeys)

- [ ] **Step 1: Register the applier**

In `DependencyInjection.cs`, add the `using` and the registration. After `using AIHelperNET.App.Windows;` add:
```csharp
using AIHelperNET.App.Hotkeys;
```
After line 25 (`services.AddSingleton<SessionRunner>();`) add:
```csharp
        services.AddSingleton<IHotkeyApplier, HotkeyApplier>();
```

- [ ] **Step 2: Register the effective set at startup via the applier**

In `App.xaml.cs`, replace the hard-coded registration loop in `WireHotkeys` (lines 187-189):
```csharp
        // Register from the single source of truth so the Settings shortcut list can never drift.
        foreach (var binding in HotkeyDefaults.All)
            hotkeys.Register(binding.Id, binding.Modifiers, binding.Key);
```
with a resolve-from-settings + applier call:
```csharp
        // Register the effective set (defaults merged with any saved user overrides).
        var settingsResult = _host.Services.GetRequiredService<IMediator>()
            .Send(new GetSettingsQuery()).AsTask().GetAwaiter().GetResult();
        var overrides = settingsResult.IsSuccess ? settingsResult.Value.HotkeyOverrides : [];
        _host.Services.GetRequiredService<IHotkeyApplier>()
            .Apply(HotkeyDefaults.Resolve(overrides));
```
Add the needed usings at the top of `App.xaml.cs` if not present:
```csharp
using AIHelperNET.App.Hotkeys;
using AIHelperNET.Application.Sessions.Queries;
using Mediator;
```
(`hotkeys.Initialize(hwnd)` on line 185 stays — the applier resolves the same `IGlobalHotkeyService` singleton, so registrations land on the initialized instance.)

- [ ] **Step 3: Build the App project to verify it compiles**

> Stop the overlay app first if running (DLL lock).

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj`
Expected: Build succeeded, 0 warnings (warnings are errors here).

- [ ] **Step 4: Commit**

```bash
git add src/AIHelperNET.App/DependencyInjection.cs src/AIHelperNET.App/App.xaml.cs
git commit -m "feat(hotkeys): apply resolved bindings at startup via IHotkeyApplier"
```

---

## Task 10: Editable Shortcuts tab UI + recorder

**Files:**
- Modify: `src/AIHelperNET.App/Windows/SettingsWindow.xaml` (Shortcuts `TabItem`, lines 245-278)
- Modify: `src/AIHelperNET.App/Windows/SettingsWindow.xaml.cs`

- [ ] **Step 1: Replace the read-only Shortcuts tab markup**

In `SettingsWindow.xaml`, replace the `<ItemsControl ItemsSource="{Binding Hotkeys}">…</ItemsControl>` block (lines 252-276) with an editable template. Keep the surrounding `StackPanel`/headings:

```xml
                    <ItemsControl ItemsSource="{Binding HotkeyRows}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <StackPanel Margin="0,0,0,10">
                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="*"/>
                                            <ColumnDefinition Width="Auto"/>
                                        </Grid.ColumnDefinitions>
                                        <TextBlock Grid.Column="0" Text="{Binding Description}"
                                                   Foreground="{DynamicResource Brush.Foreground.Primary}"
                                                   FontSize="{DynamicResource Font.SM}"
                                                   VerticalAlignment="Center"/>
                                        <StackPanel Grid.Column="1" Orientation="Horizontal">
                                            <CheckBox Content="Ctrl"  IsChecked="{Binding Ctrl}"  VerticalAlignment="Center" Margin="0,0,4,0"/>
                                            <CheckBox Content="Shift" IsChecked="{Binding Shift}" VerticalAlignment="Center" Margin="0,0,4,0"/>
                                            <CheckBox Content="Alt"   IsChecked="{Binding Alt}"   VerticalAlignment="Center" Margin="0,0,4,0"/>
                                            <CheckBox Content="Win"   IsChecked="{Binding Win}"   VerticalAlignment="Center" Margin="0,0,8,0"/>
                                            <ComboBox ItemsSource="{Binding KeyChoices}"
                                                      SelectedValue="{Binding SelectedKey}"
                                                      SelectedValuePath="Key" DisplayMemberPath="Display"
                                                      Width="64" VerticalAlignment="Center" Margin="0,0,8,0"/>
                                            <Button Content="Record"
                                                    Command="{Binding DataContext.StartRecordingCommand,
                                                              RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                                    CommandParameter="{Binding}"
                                                    VerticalAlignment="Center" Margin="0,0,4,0"/>
                                            <Button Content="Reset"
                                                    Command="{Binding DataContext.ResetRowCommand,
                                                              RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                                    CommandParameter="{Binding}"
                                                    VerticalAlignment="Center"/>
                                        </StackPanel>
                                    </Grid>
                                    <TextBlock Text="Press the shortcut keys…"
                                               Foreground="{DynamicResource Brush.Foreground.Muted}"
                                               FontSize="{DynamicResource Font.XS}" Margin="0,2,0,0"
                                               Visibility="{Binding IsRecording,
                                                            Converter={StaticResource BoolToVisibilityConverter}}"/>
                                    <TextBlock Text="{Binding ErrorMessage}" Foreground="#E06C75"
                                               FontSize="{DynamicResource Font.XS}" Margin="0,2,0,0"
                                               TextWrapping="Wrap"
                                               Visibility="{Binding ErrorMessage,
                                                            Converter={StaticResource NullToCollapsedConverter}}"/>
                                </StackPanel>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <Button Content="Reset all to defaults"
                            Command="{Binding ResetAllHotkeysCommand}"
                            HorizontalAlignment="Left" Margin="0,4,0,0"/>
```

> **Converter check:** this uses `BoolToVisibilityConverter` and `NullToCollapsedConverter` as `StaticResource`s. Before writing, confirm what's already registered in the app's resource dictionaries:
> ```
> ```
> Run a search: look in `src/AIHelperNET.App/Resources/*.xaml` and `App.xaml` for existing visibility converters.
> - If a bool→visibility converter exists under a different key, use that key instead.
> - If a null→visibility converter does **not** exist, either (a) add a simple `ErrorMessage`-bound `TextBlock` without the `Visibility` binding (empty string renders nothing, which is acceptable), or (b) add a converter to the App resources. Prefer (a) to stay minimal: drop the `Visibility=...` line on the error `TextBlock`. For the "Press the shortcut keys…" hint, if no bool→visibility converter exists, bind its `Visibility` via a `DataTrigger` on `IsRecording` in a `Style`, or omit the hint text. Keep it simple — the recorder still works without the hint.

- [ ] **Step 2: Add the recording key handler in code-behind**

In `SettingsWindow.xaml.cs`, add usings:
```csharp
using System.Windows.Input;
using AIHelperNET.App.Hotkeys;
using WpfModifiers = System.Windows.Input.ModifierKeys;
```
Add a `PreviewKeyDown` handler method to the class:
```csharp
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_vm.IsAnyRowRecording) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key; // Alt chords arrive as Key.System
        if (KeyGestureCapture.IsModifierKey(key)) { e.Handled = true; return; } // wait for the real key

        if (KeyGestureCapture.TryTranslate(key, Keyboard.Modifiers, out var mods, out var vk))
            _vm.ApplyRecordedChord(mods, vk);
        else
            _vm.SetRecordingError("Unsupported key — pick a letter, digit, F-key, or Space.");

        e.Handled = true;
    }
```

- [ ] **Step 3: Hook the handler to the window**

In `SettingsWindow.xaml`, on the root `<Window …>` element, add the attribute (only if not already present):
```xml
        PreviewKeyDown="Window_PreviewKeyDown"
```
> If the root window already has a `Loaded="Window_Loaded"` etc., just add `PreviewKeyDown="Window_PreviewKeyDown"` alongside it.

- [ ] **Step 4: Build to verify it compiles**

> Stop the overlay app first if running.

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.App/Windows/SettingsWindow.xaml \
        src/AIHelperNET.App/Windows/SettingsWindow.xaml.cs
git commit -m "feat(hotkeys): editable Shortcuts tab with press-to-record"
```

---

## Task 11: Full suite + manual verification

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test`
Expected: All green. (Integration may show the one known SQLite-lock flake — bug 6 — that is pre-existing and unrelated.)

- [ ] **Step 2: Manual smoke test** (use the `run-aihelper` skill to build + launch)

Verify in the running app, Settings → Shortcuts:
1. All six actions show their current chord (defaults on a fresh profile).
2. Change "Generate answer" to `Ctrl+Alt+G` via the checkboxes + key dropdown → Save → status shows saved. Trigger it: pressing `Ctrl+Alt+G` generates an answer; the old `Ctrl+Shift+Q` no longer does.
3. **Record:** click **Record** on a row, press `Ctrl+Shift+J` → the row updates to `Ctrl+Shift+J`. Save → it works.
4. **Duplicate guard:** set two actions to the same chord → Save → both rows show an error, nothing is saved, old hotkeys still work.
5. **Bare key guard:** uncheck all modifiers on a row → Save → that row shows "Add a modifier…", not saved.
6. **OS conflict:** bind something to a combo Windows owns (if you can reproduce, e.g. a registered system hotkey) → row shows "Already in use…", and the previously working hotkeys still fire (reverted).
7. **Reset:** per-row **Reset** restores that default; **Reset all to defaults** restores all six.
8. **Persistence:** close and reopen the app → the custom chord from step 2/3 is still in effect (registered at startup).
9. **MX Vertical pairing (optional):** in Logitech Options+, assign the Back button to your chosen chord; pressing the mouse button fires the action.

- [ ] **Step 3: Confirm no secret leakage / security checklist**

```bash
git diff develop... --stat
```
Confirm: no secrets, no new privileged action on LLM output, settings remain JSON (no EF migration). Untrusted-input rules unaffected (input-side only).

- [ ] **Step 4: Final commit / push (only when the user asks)**

When the user approves, push the branch and open a PR into `develop`:
```bash
git push -u origin feature/in-app-shortcut-rebinding
gh pr create --base develop --title "In-app shortcut rebinding" --body "<summary>"
```

---

## Self-Review Notes

- **Spec coverage:** persistence-as-overrides (Task 3) · Resolve merge (Task 2) · VirtualKey expansion (Task 1) · validator: modifier+duplicates (Task 4) · IHotkeyApplier + OS-conflict reporting (Task 5) · WPF→enum translate (Task 6) · row VMs (Task 7) · editable tab hybrid capture + apply-on-save + reset (Tasks 8, 10) · startup applies resolved set (Task 9) · tests in both layers (every task) · no EF migration / no Domain change / mouse out of scope (scope guards, Task 11 step 3). All spec sections map to a task.
- **Refinement vs spec:** Task 8 applies **before** persisting (spec said persist-then-apply); documented inline — stricter, prevents writing an unregisterable chord. Same observable behavior.
- **Type consistency:** `HotkeyOverride(Id, Modifiers, Key)`, `IHotkeyApplier.Apply(IReadOnlyList<HotkeyBinding>) → IReadOnlyList<HotkeyId>`, `HotkeyValidator.Validate(...) → IReadOnlyDictionary<HotkeyId,string>`, `HotkeyRowViewModel.FromBinding/ToBinding/ToModifiers/SetChord`, `KeyGestureCapture.IsModifierKey/TryTranslate` are used identically across tasks.
- **Known external dependency to verify at implementation time:** the two XAML converters in Task 10 — the plan tells the implementer to confirm/adapt rather than assume.
```
