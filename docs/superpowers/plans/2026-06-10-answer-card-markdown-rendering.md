# Answer-Card 4-Part Pattern + Markdown Rendering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the overlay answer card emit the 4-part answer-card pattern and render its markdown (bold, bullets, code) instead of printing literal `**`/`-`.

**Architecture:** A pure markdown parser in the Application layer (text → block model, fully unit-tested) plus a thin code-only WPF `MarkdownPresenter` in the App layer that maps blocks to a themed visual tree. The card swaps from live raw mono text to the rendered presenter on stream completion. The `PromptBuilderService.Build` prompt is rewritten to emit the 4-part structure, scaled by `AnswerLength`.

**Tech Stack:** .NET 10, C#, WPF, CommunityToolkit.Mvvm, xUnit + FluentAssertions. No new NuGet dependency. No Domain/Infrastructure/EF change — **no migration**.

**Spec:** `docs/superpowers/specs/2026-06-10-answer-card-markdown-rendering-design.md`

---

## File Structure

**Create:**
- `src/AIHelperNET.Application/Answers/Markdown/MarkdownModel.cs` — pure record block/inline model.
- `src/AIHelperNET.Application/Answers/Markdown/AnswerMarkdownParser.cs` — pure parser.
- `src/AIHelperNET.App/Controls/MarkdownPresenter.cs` — code-only WPF presenter control.
- `tests/AIHelperNET.Application.Tests/Answers/AnswerMarkdownParserTests.cs` — parser tests.

**Modify:**
- `src/AIHelperNET.Application/Answers/PromptBuilderService.cs` — 4-part rewrite + shared markdown rule.
- `tests/AIHelperNET.Application.Tests/Answers/PromptBuilderServiceTests.cs` — prompt tests.
- `src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs` — `IsComplete`/`RenderedMarkdown`, instance `OnComplete`.
- `src/AIHelperNET.App/App.xaml.cs:86` — call instance `OnComplete`.
- `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml` — three-way answer presentation swap + xmlns.
- `src/AIHelperNET.App/Resources/DarkTheme.xaml` and `LightTheme.xaml` — three new brushes.

---

## Task 1: Markdown model + parser test suite (RED)

**Files:**
- Create: `src/AIHelperNET.Application/Answers/Markdown/MarkdownModel.cs`
- Test: `tests/AIHelperNET.Application.Tests/Answers/AnswerMarkdownParserTests.cs`

- [ ] **Step 1: Create the model**

`src/AIHelperNET.Application/Answers/Markdown/MarkdownModel.cs`:

```csharp
namespace AIHelperNET.Application.Answers.Markdown;

/// <summary>An inline run within a markdown paragraph or list item.</summary>
public abstract record MarkdownInline;

/// <summary>Plain text.</summary>
public sealed record TextRun(string Text) : MarkdownInline;

/// <summary>Bold text (also used for bold sub-labels).</summary>
public sealed record BoldRun(string Text) : MarkdownInline;

/// <summary>Inline code span.</summary>
public sealed record CodeRun(string Text) : MarkdownInline;

/// <summary>A top-level markdown block.</summary>
public abstract record MarkdownBlock;

/// <summary>A paragraph of inline runs.</summary>
public sealed record ParagraphBlock(IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;

/// <summary>An ordered or unordered list; each item is a list of inline runs.</summary>
public sealed record ListBlock(IReadOnlyList<IReadOnlyList<MarkdownInline>> Items, bool Ordered) : MarkdownBlock;

/// <summary>A fenced code block.</summary>
public sealed record CodeBlock(string? Language, string Code) : MarkdownBlock;
```

- [ ] **Step 2: Write the parser test suite**

`tests/AIHelperNET.Application.Tests/Answers/AnswerMarkdownParserTests.cs`:

```csharp
using AIHelperNET.Application.Answers.Markdown;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

public class AnswerMarkdownParserTests
{
    [Fact]
    public void Parse_Empty_ReturnsNoBlocks()
    {
        AnswerMarkdownParser.Parse("").Should().BeEmpty();
        AnswerMarkdownParser.Parse("   ").Should().BeEmpty();
        AnswerMarkdownParser.Parse(null).Should().BeEmpty();
    }

    [Fact]
    public void Parse_PlainParagraph_YieldsSingleTextRun()
    {
        var blocks = AnswerMarkdownParser.Parse("Resilience means failing safely.");
        blocks.Should().ContainSingle();
        var p = blocks[0].Should().BeOfType<ParagraphBlock>().Subject;
        p.Inlines.Should().ContainSingle()
            .Which.Should().BeOfType<TextRun>()
            .Which.Text.Should().Be("Resilience means failing safely.");
    }

    [Fact]
    public void Parse_Bold_YieldsBoldRunBetweenText()
    {
        var blocks = AnswerMarkdownParser.Parse("Use **idempotency** here.");
        var p = (ParagraphBlock)blocks[0];
        p.Inlines.Should().HaveCount(3);
        p.Inlines[0].Should().BeOfType<TextRun>().Which.Text.Should().Be("Use ");
        p.Inlines[1].Should().BeOfType<BoldRun>().Which.Text.Should().Be("idempotency");
        p.Inlines[2].Should().BeOfType<TextRun>().Which.Text.Should().Be(" here.");
    }

    [Fact]
    public void Parse_InlineCode_YieldsCodeRun()
    {
        var blocks = AnswerMarkdownParser.Parse("Call `Dispose()` after.");
        var p = (ParagraphBlock)blocks[0];
        p.Inlines[1].Should().BeOfType<CodeRun>().Which.Text.Should().Be("Dispose()");
    }

    [Fact]
    public void Parse_UnclosedBold_TreatedAsLiteralText()
    {
        var blocks = AnswerMarkdownParser.Parse("Half **bold only");
        var p = (ParagraphBlock)blocks[0];
        p.Inlines.Should().ContainSingle()
            .Which.Should().BeOfType<TextRun>()
            .Which.Text.Should().Be("Half **bold only");
    }

    [Fact]
    public void Parse_UnorderedBullets_YieldsUnorderedListBlock()
    {
        var blocks = AnswerMarkdownParser.Parse("- retries\n- circuit breakers\n- idempotency");
        var list = blocks[0].Should().BeOfType<ListBlock>().Subject;
        list.Ordered.Should().BeFalse();
        list.Items.Should().HaveCount(3);
        ((TextRun)list.Items[0][0]).Text.Should().Be("retries");
    }

    [Fact]
    public void Parse_OrderedList_YieldsOrderedListBlock()
    {
        var blocks = AnswerMarkdownParser.Parse("1. write to outbox\n2. relay publishes\n3. retry");
        var list = blocks[0].Should().BeOfType<ListBlock>().Subject;
        list.Ordered.Should().BeTrue();
        list.Items.Should().HaveCount(3);
        ((TextRun)list.Items[2][0]).Text.Should().Be("retry");
    }

    [Fact]
    public void Parse_BoldSubLabelThenBullets_YieldsParagraphThenList()
    {
        var blocks = AnswerMarkdownParser.Parse("**Fault tolerance:**\n- retries\n- breakers");
        blocks.Should().HaveCount(2);
        var p = blocks[0].Should().BeOfType<ParagraphBlock>().Subject;
        p.Inlines[0].Should().BeOfType<BoldRun>().Which.Text.Should().Be("Fault tolerance:");
        blocks[1].Should().BeOfType<ListBlock>().Which.Items.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_FencedCodeBlock_YieldsCodeBlockWithLanguage()
    {
        var md = "Here:\n```csharp\nvar x = 1;\nreturn x;\n```\nDone.";
        var blocks = AnswerMarkdownParser.Parse(md);
        blocks.Should().HaveCount(3);
        var code = blocks[1].Should().BeOfType<CodeBlock>().Subject;
        code.Language.Should().Be("csharp");
        code.Code.Should().Be("var x = 1;\nreturn x;");
        blocks[2].Should().BeOfType<ParagraphBlock>();
    }

    [Fact]
    public void Parse_UnterminatedFence_TreatsRemainderAsCode()
    {
        var md = "```\nline one\nline two";
        var code = AnswerMarkdownParser.Parse(md)[0].Should().BeOfType<CodeBlock>().Subject;
        code.Language.Should().BeNull();
        code.Code.Should().Be("line one\nline two");
    }

    [Fact]
    public void Parse_HeaderLine_DegradesToPlainParagraph()
    {
        var blocks = AnswerMarkdownParser.Parse("# Not a header");
        blocks[0].Should().BeOfType<ParagraphBlock>()
            .Which.Inlines[0].Should().BeOfType<TextRun>()
            .Which.Text.Should().Be("# Not a header");
    }

    [Fact]
    public void Parse_FullFourPartAnswer_ProducesExpectedBlockSequence()
    {
        var md =
            "Resilience means failing safely.\n\n" +
            "I would focus on:\n" +
            "- retries\n- breakers\n\n" +
            "```csharp\nPolicy.Handle<Exception>();\n```\n\n" +
            "Design for failure.";
        var blocks = AnswerMarkdownParser.Parse(md);
        blocks.Should().HaveCount(5);
        blocks[0].Should().BeOfType<ParagraphBlock>();
        blocks[1].Should().BeOfType<ParagraphBlock>();
        blocks[2].Should().BeOfType<ListBlock>();
        blocks[3].Should().BeOfType<CodeBlock>();
        blocks[4].Should().BeOfType<ParagraphBlock>();
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~AnswerMarkdownParser"`
Expected: FAIL — `AnswerMarkdownParser` does not exist (compile error).

- [ ] **Step 4: Commit**

```bash
git add src/AIHelperNET.Application/Answers/Markdown/MarkdownModel.cs tests/AIHelperNET.Application.Tests/Answers/AnswerMarkdownParserTests.cs
git commit -m "test(markdown): block model + failing parser test suite"
```

---

## Task 2: Implement the parser (GREEN)

**Files:**
- Create: `src/AIHelperNET.Application/Answers/Markdown/AnswerMarkdownParser.cs`

- [ ] **Step 1: Write the parser**

`src/AIHelperNET.Application/Answers/Markdown/AnswerMarkdownParser.cs`:

```csharp
using System.Text;

namespace AIHelperNET.Application.Answers.Markdown;

/// <summary>
/// Parses the constrained markdown subset emitted by answer prompts into a block model.
/// Supports paragraphs, unordered/ordered lists, fenced code blocks, and inline bold + code.
/// Never throws — unrecognized or malformed input degrades to plain text.
/// </summary>
public static class AnswerMarkdownParser
{
    /// <summary>Parses markdown text into an ordered list of blocks.</summary>
    public static IReadOnlyList<MarkdownBlock> Parse(string? text)
    {
        var blocks = new List<MarkdownBlock>();
        if (string.IsNullOrWhiteSpace(text))
            return blocks;

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var paragraph = new List<string>();
        var i = 0;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new ParagraphBlock(ParseInlines(string.Join(" ", paragraph))));
            paragraph.Clear();
        }

        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // Fenced code block
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                var lang = trimmed[3..].Trim();
                var code = new StringBuilder();
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    code.Append(lines[i]).Append('\n');
                    i++;
                }
                i++; // skip closing fence (or step past EOF)
                blocks.Add(new CodeBlock(
                    string.IsNullOrEmpty(lang) ? null : lang,
                    code.ToString().TrimEnd('\n')));
                continue;
            }

            // Blank line ends a paragraph
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                i++;
                continue;
            }

            // Unordered list
            if (IsUnorderedBullet(trimmed))
            {
                FlushParagraph();
                var items = new List<IReadOnlyList<MarkdownInline>>();
                while (i < lines.Length && IsUnorderedBullet(lines[i].TrimStart()))
                {
                    items.Add(ParseInlines(lines[i].TrimStart()[2..]));
                    i++;
                }
                blocks.Add(new ListBlock(items, Ordered: false));
                continue;
            }

            // Ordered list
            if (TryStripOrdered(trimmed, out var orderedContent))
            {
                FlushParagraph();
                var items = new List<IReadOnlyList<MarkdownInline>> { ParseInlines(orderedContent) };
                i++;
                while (i < lines.Length && TryStripOrdered(lines[i].TrimStart(), out var next))
                {
                    items.Add(ParseInlines(next));
                    i++;
                }
                blocks.Add(new ListBlock(items, Ordered: true));
                continue;
            }

            // Otherwise accumulate into the current paragraph
            paragraph.Add(line.Trim());
            i++;
        }

        FlushParagraph();
        return blocks;
    }

    private static bool IsUnorderedBullet(string trimmed)
        => trimmed.Length >= 2 && (trimmed[0] is '-' or '*') && trimmed[1] == ' ';

    private static bool TryStripOrdered(string trimmed, out string content)
    {
        content = string.Empty;
        var dot = trimmed.IndexOf('.');
        if (dot <= 0 || dot + 1 >= trimmed.Length || trimmed[dot + 1] != ' ') return false;
        for (var k = 0; k < dot; k++)
            if (!char.IsDigit(trimmed[k])) return false;
        content = trimmed[(dot + 2)..];
        return true;
    }

    private static IReadOnlyList<MarkdownInline> ParseInlines(string text)
    {
        var inlines = new List<MarkdownInline>();
        var buffer = new StringBuilder();

        void FlushText()
        {
            if (buffer.Length == 0) return;
            inlines.Add(new TextRun(buffer.ToString()));
            buffer.Clear();
        }

        var i = 0;
        while (i < text.Length)
        {
            // Bold **...**
            if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
            {
                var close = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (close > i + 1)
                {
                    FlushText();
                    inlines.Add(new BoldRun(text[(i + 2)..close]));
                    i = close + 2;
                    continue;
                }
            }

            // Inline code `...`
            if (text[i] == '`')
            {
                var close = text.IndexOf('`', i + 1);
                if (close > i)
                {
                    FlushText();
                    inlines.Add(new CodeRun(text[(i + 1)..close]));
                    i = close + 1;
                    continue;
                }
            }

            buffer.Append(text[i]);
            i++;
        }

        FlushText();
        return inlines;
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~AnswerMarkdownParser"`
Expected: PASS (12 tests).

- [ ] **Step 3: Commit**

```bash
git add src/AIHelperNET.Application/Answers/Markdown/AnswerMarkdownParser.cs
git commit -m "feat(markdown): constrained-subset answer markdown parser"
```

---

## Task 3: Rewrite the Build prompt for the 4-part pattern

**Files:**
- Modify: `src/AIHelperNET.Application/Answers/PromptBuilderService.cs`
- Test: `tests/AIHelperNET.Application.Tests/Answers/PromptBuilderServiceTests.cs`

- [ ] **Step 1: Write the failing prompt tests**

Append to `tests/AIHelperNET.Application.Tests/Answers/PromptBuilderServiceTests.cs` (inside the class):

```csharp
    [Fact]
    public void Build_SystemDescribesFourPartStructure()
    {
        var q = DetectedQuestion.Create("What is resilience?", QuestionSource.Audio, Now);
        var prompt = PromptBuilderService.Build(CodeProfile.Empty, AnswerSettings.Default, q);
        prompt.System.Should().Contain("definition");
        prompt.System.Should().Contain("bullets");
        prompt.System.Should().Contain("principle");
    }

    [Fact]
    public void Build_StillBansHeaders()
    {
        var q = DetectedQuestion.Create("x", QuestionSource.Audio, Now);
        var prompt = PromptBuilderService.Build(CodeProfile.Empty, AnswerSettings.Default, q);
        prompt.System.Should().Contain("NO headers");
    }

    [Fact]
    public void Build_StillHasCodeOnlyRule()
    {
        var q = DetectedQuestion.Create("x", QuestionSource.Audio, Now);
        var prompt = PromptBuilderService.Build(CodeProfile.Empty, AnswerSettings.Default, q);
        prompt.System.Should().Contain("include code ONLY");
    }

    [Fact]
    public void Build_ShortLength_OmitsGroupingAndExample()
    {
        var q = DetectedQuestion.Create("x", QuestionSource.Audio, Now);
        var settings = AnswerSettings.Default with { Length = AnswerLength.ShortLength };
        var prompt = PromptBuilderService.Build(CodeProfile.Empty, settings, q);
        prompt.System.Should().Contain("Do NOT group");
        prompt.System.Should().NotContain("concrete example");
    }

    [Fact]
    public void Build_DeepDive_IncludesGroupingAndExample()
    {
        var q = DetectedQuestion.Create("x", QuestionSource.Audio, Now);
        var settings = AnswerSettings.Default with { Length = AnswerLength.DeepDive };
        var prompt = PromptBuilderService.Build(CodeProfile.Empty, settings, q);
        prompt.System.Should().Contain("sub-labels");
        prompt.System.Should().Contain("concrete example");
    }

    [Fact]
    public void BuildFollowUp_IncludesMarkdownFormattingRule()
    {
        var prompt = PromptBuilderService.BuildFollowUp(CodeProfile.Empty, AnswerSettings.Default, "q", "a", "f");
        prompt.System.Should().Contain("fenced");
    }

    [Fact]
    public void BuildWithScreenMode_IncludesMarkdownFormattingRule()
    {
        var prompt = PromptBuilderService.BuildWithScreenMode(
            CodeProfile.Empty, AnswerSettings.Default, "code on screen",
            new[] { "line" }, ScreenAnalysisMode.ExplainCode);
        prompt.System.Should().Contain("fenced");
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~PromptBuilderServiceTests"`
Expected: FAIL on the new facts (current prompt has no "definition/principle/sub-labels", no "fenced", and says "3–5 sentences").

- [ ] **Step 3: Rewrite the `Build` STRICT RULES block**

In `PromptBuilderService.cs`, replace the existing STRICT RULES block (the lines from `system.AppendLine("STRICT RULES:");` through rule 5 ending `...restate the question.");`) with:

```csharp
        system.AppendLine("STRICT RULES:");
        system.AppendLine("1. Answer like an experienced engineer speaking — first person, spoken, direct, no filler.");
        system.AppendLine("2. Structure the answer as: (a) a 1–2 sentence definition or reframe to open; " +
            "(b) a first-person cue line (e.g. \"I would focus on:\") followed by terse \"- \" bullets; " +
            "(c) a single closing principle line.");
        AppendStructureGuidance(system, settings.Length);
        system.AppendLine("3. FORMATTING: use **bold** for emphasis and sub-labels, and \"- \" for bullets. " +
            "NO headers (no #, ##). Put any code in fenced ```language blocks.");
        system.AppendLine("4. CODE: include code ONLY when the question explicitly asks to write, " +
            "implement, fix, debug, show syntax, or provide a query/example. " +
            "For conceptual, design, 'what is', 'why', 'how does it work' questions — verbal answer only.");
        system.AppendLine("5. Start directly with the answer. Never say 'Great question' or restate the question.");
        system.AppendLine("6. Give the answer only — no 'why this is a good answer' commentary or meta-notes.");
```

- [ ] **Step 4: Add the structure-guidance and shared-markdown helpers**

Add these members to the `PromptBuilderService` class (e.g. just above `MapLengthToTokens`):

```csharp
    private const string SharedMarkdownRule =
        "Formatting: use \"- \" for bullets and **bold** for emphasis; " +
        "put any code in fenced ```language blocks; no headers (#).";

    private static void AppendStructureGuidance(StringBuilder sb, AnswerLength length)
    {
        switch (length)
        {
            case AnswerLength.VeryShort:
            case AnswerLength.ShortLength:
                sb.AppendLine("   Keep it flat: the opening definition, then 4–6 terse bullets, then the principle. " +
                    "Do NOT group bullets under sub-labels and do NOT include a worked example.");
                break;
            case AnswerLength.Medium:
                sb.AppendLine("   Bullets may be grouped under **bold sub-labels:** when the topic has natural dimensions.");
                break;
            case AnswerLength.Detailed:
            case AnswerLength.DeepDive:
                sb.AppendLine("   Group bullets under **bold sub-labels:** for each dimension, and include one short " +
                    "concrete example (a brief scenario or ordered steps) before the closing principle.");
                break;
            default:
                break;
        }
    }
```

- [ ] **Step 5: Add the shared markdown rule to the other two builders**

In `BuildFollowUp`, after the `AppendCodeProfile(system, profile);` call, add:

```csharp
        system.AppendLine(SharedMarkdownRule);
```

In `BuildWithScreenMode`, after `AppendCodeProfile(system, profile);`, add the same line:

```csharp
        system.AppendLine(SharedMarkdownRule);
```

- [ ] **Step 6: Run all Application tests**

Run: `dotnet test tests/AIHelperNET.Application.Tests`
Expected: PASS (existing + 7 new prompt tests + parser tests).

- [ ] **Step 7: Commit**

```bash
git add src/AIHelperNET.Application/Answers/PromptBuilderService.cs tests/AIHelperNET.Application.Tests/Answers/PromptBuilderServiceTests.cs
git commit -m "feat(prompt): 4-part answer structure on Build, scaled by length; shared markdown rule"
```

---

## Task 4: Add theme brushes

**Files:**
- Modify: `src/AIHelperNET.App/Resources/DarkTheme.xaml`
- Modify: `src/AIHelperNET.App/Resources/LightTheme.xaml`

- [ ] **Step 1: Add brushes to DarkTheme.xaml**

Immediately after the `Brush.Semantic.Error` line (~line 21) in `DarkTheme.xaml`, add:

```xml
    <SolidColorBrush x:Key="Brush.Markdown.CodeBackground"   Color="#11141C"/>
    <SolidColorBrush x:Key="Brush.Markdown.InlineCode"       Color="#CE9178"/>
    <SolidColorBrush x:Key="Brush.Markdown.SubLabel"         Color="#9CDCFE"/>
```

- [ ] **Step 2: Add brushes to LightTheme.xaml**

Immediately after the `Brush.Semantic.Error` line (~line 21) in `LightTheme.xaml`, add:

```xml
    <SolidColorBrush x:Key="Brush.Markdown.CodeBackground"   Color="#F0F0F0"/>
    <SolidColorBrush x:Key="Brush.Markdown.InlineCode"       Color="#A31515"/>
    <SolidColorBrush x:Key="Brush.Markdown.SubLabel"         Color="#0B5FB0"/>
```

- [ ] **Step 3: Build to confirm XAML is valid**

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj`
Expected: succeeds (0 warnings — warnings are errors).

- [ ] **Step 4: Commit**

```bash
git add src/AIHelperNET.App/Resources/DarkTheme.xaml src/AIHelperNET.App/Resources/LightTheme.xaml
git commit -m "feat(theme): markdown code/inline-code/sub-label brushes (dark+light)"
```

---

## Task 5: ViewModel streaming → rendered swap state

**Files:**
- Modify: `src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs`
- Modify: `src/AIHelperNET.App/App.xaml.cs:86`

- [ ] **Step 1: Add `IsComplete` + `RenderedMarkdown` to `AnswerVersionVm`**

In `ConversationTurnViewModel.cs`, inside `AnswerVersionVm`, after the `IsError` property add:

```csharp
    private bool _isComplete;
    /// <summary>Gets or sets a value indicating whether streaming for this version has finished
    /// (drives the swap from raw streaming text to the rendered markdown card).</summary>
    public bool IsComplete
    {
        get => _isComplete;
        set
        {
            if (SetProperty(ref _isComplete, value))
                OnPropertyChanged(nameof(RenderedMarkdown));
        }
    }

    /// <summary>Gets the answer text for markdown rendering — non-empty only once streaming is
    /// complete and this is not an error version, so the parser runs once (not per chunk).</summary>
    public string RenderedMarkdown => IsComplete && !IsError ? Text : string.Empty;
```

- [ ] **Step 2: Make `OnComplete` an instance method that flips `IsComplete`**

Replace the existing static `OnComplete` method:

```csharp
    /// <summary>Called when streaming for the given turn and version type completes.</summary>
    public static void OnComplete(ConversationTurnId turnId, AnswerVersionType versionType)
    {
        // Version already marked IsLatest during streaming — no further action needed.
    }
```

with:

```csharp
    /// <summary>Called when streaming for the given turn completes; marks the latest version
    /// complete so the card swaps to rendered markdown.</summary>
    public void OnComplete(ConversationTurnId turnId, AnswerVersionType versionType)
    {
        var turn = Turns.FirstOrDefault(t => t.Id == turnId);
        var version = turn?.AnswerVersions.FirstOrDefault(v => v.IsLatest);
        if (version is not null)
            version.IsComplete = true;
    }
```

- [ ] **Step 3: Mark error versions complete in `CreateNewVersion`**

In `CreateNewVersion`, change the version initializer from:

```csharp
        var version = new AnswerVersionVm(id, type, DateTimeOffset.UtcNow)
            { Text = text, IsLatest = true, IsError = isError };
```

to (error versions don't stream, so they are complete immediately):

```csharp
        var version = new AnswerVersionVm(id, type, DateTimeOffset.UtcNow)
            { Text = text, IsLatest = true, IsError = isError, IsComplete = isError };
```

- [ ] **Step 4: Update the sink wiring to call the instance method**

In `src/AIHelperNET.App/App.xaml.cs` line 86, change:

```csharp
            onComplete: (id, type)        => ConversationTurnViewModel.OnComplete(id, type),
```

to:

```csharp
            onComplete: (id, type)        => turnVm.OnComplete(id, type),
```

- [ ] **Step 5: Build to confirm it compiles**

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj`
Expected: succeeds, 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs src/AIHelperNET.App/App.xaml.cs
git commit -m "feat(ui): IsComplete/RenderedMarkdown swap state + instance OnComplete"
```

---

## Task 6: MarkdownPresenter WPF control

**Files:**
- Create: `src/AIHelperNET.App/Controls/MarkdownPresenter.cs`

> No unit test: this is a code-only WPF control with no parsing logic (it delegates to the
> already-tested parser). It is verified by the build and the live visual check in Task 8/9.

- [ ] **Step 1: Write the control**

`src/AIHelperNET.App/Controls/MarkdownPresenter.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AIHelperNET.Application.Answers.Markdown;

namespace AIHelperNET.App.Controls;

/// <summary>Renders the constrained answer-markdown subset into a themed WPF visual tree.</summary>
public sealed class MarkdownPresenter : ContentControl
{
    private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas");

    /// <summary>The markdown text to render. Rebuilds the visual tree on change.</summary>
    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.Register(nameof(Markdown), typeof(string), typeof(MarkdownPresenter),
            new PropertyMetadata(string.Empty, OnChanged));

    /// <summary>Base font size for prose and code. Rebuilds on change.</summary>
    public static readonly DependencyProperty BaseFontSizeProperty =
        DependencyProperty.Register(nameof(BaseFontSize), typeof(double), typeof(MarkdownPresenter),
            new PropertyMetadata(12.0, OnChanged));

    /// <summary>Gets or sets the markdown text to render.</summary>
    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    /// <summary>Gets or sets the base font size for prose and code.</summary>
    public double BaseFontSize
    {
        get => (double)GetValue(BaseFontSizeProperty);
        set => SetValue(BaseFontSizeProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MarkdownPresenter)d).Rebuild();

    private void Rebuild()
    {
        var panel = new StackPanel();
        foreach (var block in AnswerMarkdownParser.Parse(Markdown))
            panel.Children.Add(RenderBlock(block));
        Content = panel;
    }

    private UIElement RenderBlock(MarkdownBlock block) => block switch
    {
        ParagraphBlock p => Paragraph(p),
        ListBlock l      => List(l),
        CodeBlock c      => Code(c),
        _                => new TextBlock()
    };

    private TextBlock Paragraph(ParagraphBlock p)
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = BaseFontSize,
            Margin = new Thickness(0, 0, 0, 6)
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Foreground.Primary");
        foreach (var inline in p.Inlines)
            tb.Inlines.Add(ToInline(inline));
        return tb;
    }

    private Inline ToInline(MarkdownInline inline)
    {
        switch (inline)
        {
            case BoldRun b:
                var bold = new Run(b.Text) { FontWeight = FontWeights.SemiBold };
                bold.SetResourceReference(TextElement.ForegroundProperty, "Brush.Markdown.SubLabel");
                return bold;
            case CodeRun c:
                var code = new Run(c.Text) { FontFamily = MonoFont };
                code.SetResourceReference(TextElement.ForegroundProperty, "Brush.Markdown.InlineCode");
                return code;
            default:
                return new Run(((TextRun)inline).Text);
        }
    }

    private UIElement List(ListBlock l)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        for (var idx = 0; idx < l.Items.Count; idx++)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var bullet = new TextBlock
            {
                Text = l.Ordered ? $"{idx + 1}." : "•",
                FontSize = BaseFontSize,
                Margin = new Thickness(0, 0, 6, 0)
            };
            bullet.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Foreground.Secondary");
            Grid.SetColumn(bullet, 0);

            var content = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = BaseFontSize };
            content.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Foreground.Primary");
            foreach (var inline in l.Items[idx])
                content.Inlines.Add(ToInline(inline));
            Grid.SetColumn(content, 1);

            row.Children.Add(bullet);
            row.Children.Add(content);
            panel.Children.Add(row);
        }
        return panel;
    }

    private UIElement Code(CodeBlock c)
    {
        var tb = new TextBlock
        {
            Text = c.Code,
            FontFamily = MonoFont,
            FontSize = BaseFontSize,
            TextWrapping = TextWrapping.NoWrap
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Foreground.Primary");

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = tb
        };

        var border = new Border
        {
            Padding = new Thickness(8),
            Margin = new Thickness(0, 2, 0, 6),
            CornerRadius = new CornerRadius(3),
            Child = scroll
        };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.Markdown.CodeBackground");
        return border;
    }
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj`
Expected: succeeds, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add src/AIHelperNET.App/Controls/MarkdownPresenter.cs
git commit -m "feat(ui): MarkdownPresenter control rendering the answer markdown subset"
```

---

## Task 7: Wire the three-way presentation swap into the turn card

**Files:**
- Modify: `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml` (xmlns + answer block ~lines 418-436)

- [ ] **Step 1: Add the controls xmlns to the Window root**

In `MainOverlayWindow.xaml`, add this namespace declaration to the root `<Window …>` element (alongside the existing `xmlns:` lines):

```xml
        xmlns:controls="clr-namespace:AIHelperNET.App.Controls"
```

- [ ] **Step 2: Replace the answer `TextBlock` with the three-way swap**

Replace the entire answer `TextBlock` element (the one with `Text="{Binding LatestVersion.Text}"`, `FontFamily="Cascadia Mono, Consolas"`, and the `IsError` DataTrigger style — currently ~lines 418-436) with:

```xml
                                                <Grid Margin="0,0,0,4">
                                                    <!-- Streaming: live raw mono text -->
                                                    <TextBlock Text="{Binding LatestVersion.Text}"
                                                               FontSize="{Binding DataContext.ConversationTurn.AnswerFontSize,
                                                                                  RelativeSource={RelativeSource AncestorType=Window}}"
                                                               TextWrapping="Wrap"
                                                               FontFamily="Cascadia Mono, Consolas"
                                                               Foreground="{DynamicResource Brush.Foreground.Primary}">
                                                        <TextBlock.Style>
                                                            <Style TargetType="TextBlock">
                                                                <Setter Property="Visibility" Value="Collapsed"/>
                                                                <Style.Triggers>
                                                                    <MultiDataTrigger>
                                                                        <MultiDataTrigger.Conditions>
                                                                            <Condition Binding="{Binding LatestVersion.IsComplete}" Value="False"/>
                                                                            <Condition Binding="{Binding LatestVersion.IsError}" Value="False"/>
                                                                        </MultiDataTrigger.Conditions>
                                                                        <Setter Property="Visibility" Value="Visible"/>
                                                                    </MultiDataTrigger>
                                                                </Style.Triggers>
                                                            </Style>
                                                        </TextBlock.Style>
                                                    </TextBlock>

                                                    <!-- Complete: rendered markdown -->
                                                    <controls:MarkdownPresenter
                                                        Markdown="{Binding LatestVersion.RenderedMarkdown}"
                                                        BaseFontSize="{Binding DataContext.ConversationTurn.AnswerFontSize,
                                                                               RelativeSource={RelativeSource AncestorType=Window}}">
                                                        <controls:MarkdownPresenter.Style>
                                                            <Style TargetType="controls:MarkdownPresenter">
                                                                <Setter Property="Visibility" Value="Collapsed"/>
                                                                <Style.Triggers>
                                                                    <MultiDataTrigger>
                                                                        <MultiDataTrigger.Conditions>
                                                                            <Condition Binding="{Binding LatestVersion.IsComplete}" Value="True"/>
                                                                            <Condition Binding="{Binding LatestVersion.IsError}" Value="False"/>
                                                                        </MultiDataTrigger.Conditions>
                                                                        <Setter Property="Visibility" Value="Visible"/>
                                                                    </MultiDataTrigger>
                                                                </Style.Triggers>
                                                            </Style>
                                                        </controls:MarkdownPresenter.Style>
                                                    </controls:MarkdownPresenter>

                                                    <!-- Error: distinct error styling -->
                                                    <TextBlock Text="{Binding LatestVersion.Text}"
                                                               FontSize="{Binding DataContext.ConversationTurn.AnswerFontSize,
                                                                                  RelativeSource={RelativeSource AncestorType=Window}}"
                                                               TextWrapping="Wrap"
                                                               FontFamily="Cascadia Mono, Consolas"
                                                               Foreground="{DynamicResource Brush.Semantic.Error}"
                                                               Visibility="{Binding LatestVersion.IsError,
                                                                   Converter={StaticResource BoolToVisibilityConverter}}"/>
                                                </Grid>
```

- [ ] **Step 3: Build to confirm XAML compiles**

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj`
Expected: succeeds, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add src/AIHelperNET.App/Windows/MainOverlayWindow.xaml
git commit -m "feat(ui): three-way answer swap — streaming raw / rendered markdown / error"
```

---

## Task 8: Full build, test, and live visual verification

**Files:** none (verification only).

- [ ] **Step 1: Full solution build**

Run: `dotnet build`
Expected: succeeds, 0 warnings (TreatWarningsAsErrors).

- [ ] **Step 2: Full test suite (excluding the known-slow UI suite if desired)**

Run: `dotnet test tests/AIHelperNET.Domain.Tests tests/AIHelperNET.Application.Tests tests/AIHelperNET.Infrastructure.Tests`
Expected: PASS. (Application gains the parser + prompt tests.)

- [ ] **Step 3: Launch the app and verify rendering visually**

Use the `run-aihelper` skill (stop/build/launch the overlay). With an API key configured, start a session and trigger an answer (or use a screen-capture coding question). Confirm:
- A completed conceptual answer shows rendered **bold**, bullets, and a closing principle — **no literal `**` or `-`** on screen.
- A code answer shows a mono code block on a distinct background; prose is proportional.
- During streaming, raw text grows live, then swaps cleanly to the rendered card on completion.
- An error (e.g. wrong key) still shows in the error color, not as a normal answer.
- Toggle the theme (Dark/Light) and confirm code/inline-code/sub-label colors read correctly in both.

> If anything renders wrong, fix in `MarkdownPresenter.cs` (rendering) or `AnswerMarkdownParser.cs` (parsing — add a failing test in `AnswerMarkdownParserTests.cs` first), then re-run Steps 1-3.

- [ ] **Step 4: Final commit (if visual fixes were needed)**

```bash
git add -A
git commit -m "fix(ui): markdown rendering adjustments from visual verification"
```

---

## Done

When all tasks are checked: the answer card emits and renders the 4-part pattern, streaming swaps cleanly to rendered markdown, errors stay distinct, both themes work, the build is clean, and the parser is fully unit-tested. Hand off to `superpowers:finishing-a-development-branch` to open the PR to `develop`.
