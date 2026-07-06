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

    // --- Heading block tests ---

    [Fact]
    public void Parse_H2_YieldsHeadingBlockLevel2()
    {
        var blocks = AnswerMarkdownParser.Parse("## Section Title");
        blocks.Should().ContainSingle();
        var h = blocks[0].Should().BeOfType<HeadingBlock>().Subject;
        h.Level.Should().Be(2);
        h.Inlines.Should().ContainSingle()
            .Which.Should().BeOfType<TextRun>()
            .Which.Text.Should().Be("Section Title");
    }

    [Fact]
    public void Parse_H1_YieldsHeadingBlockLevel1()
    {
        var blocks = AnswerMarkdownParser.Parse("# H1");
        var h = blocks[0].Should().BeOfType<HeadingBlock>().Subject;
        h.Level.Should().Be(1);
        ((TextRun)h.Inlines[0]).Text.Should().Be("H1");
    }

    [Fact]
    public void Parse_H4_YieldsHeadingBlockLevel4()
    {
        var blocks = AnswerMarkdownParser.Parse("#### H4");
        var h = blocks[0].Should().BeOfType<HeadingBlock>().Subject;
        h.Level.Should().Be(4);
        ((TextRun)h.Inlines[0]).Text.Should().Be("H4");
    }

    [Fact]
    public void Parse_HashNoSpace_DegradesToParagraph()
    {
        var blocks = AnswerMarkdownParser.Parse("##NoSpace");
        blocks[0].Should().BeOfType<ParagraphBlock>()
            .Which.Inlines[0].Should().BeOfType<TextRun>()
            .Which.Text.Should().Be("##NoSpace");
    }

    [Fact]
    public void Parse_FiveHashes_DegradesToParagraph()
    {
        var blocks = AnswerMarkdownParser.Parse("##### five");
        blocks[0].Should().BeOfType<ParagraphBlock>()
            .Which.Inlines[0].Should().BeOfType<TextRun>()
            .Which.Text.Should().Be("##### five");
    }

    [Fact]
    public void Parse_HeadingWithBoldInline_YieldsMixedInlines()
    {
        var blocks = AnswerMarkdownParser.Parse("## Use **bold** here");
        var h = blocks[0].Should().BeOfType<HeadingBlock>().Subject;
        h.Level.Should().Be(2);
        h.Inlines.Should().HaveCount(3);
        h.Inlines[0].Should().BeOfType<TextRun>().Which.Text.Should().Be("Use ");
        h.Inlines[1].Should().BeOfType<BoldRun>().Which.Text.Should().Be("bold");
        h.Inlines[2].Should().BeOfType<TextRun>().Which.Text.Should().Be(" here");
    }

    [Fact]
    public void Parse_HeadingBetweenParagraphs_ParsesAllBlocks()
    {
        var md = "Intro text.\n\n## Section\n\nBody text.";
        var blocks = AnswerMarkdownParser.Parse(md);
        blocks.Should().HaveCount(3);
        blocks[0].Should().BeOfType<ParagraphBlock>();
        blocks[1].Should().BeOfType<HeadingBlock>().Which.Level.Should().Be(2);
        blocks[2].Should().BeOfType<ParagraphBlock>();
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

    [Fact]
    public void Parse_UnclosedInlineCode_TreatedAsLiteralText()
    {
        var blocks = AnswerMarkdownParser.Parse("Use `foo bar");
        var p = (ParagraphBlock)blocks[0];
        p.Inlines.Should().ContainSingle()
            .Which.Should().BeOfType<TextRun>()
            .Which.Text.Should().Be("Use `foo bar");
    }

    [Fact]
    public void Parse_FencedCodeWithBlankLine_PreservesBlankLineInCode()
    {
        var blocks = AnswerMarkdownParser.Parse("```\nline one\n\nline two\n```");
        ((CodeBlock)blocks[0]).Code.Should().Be("line one\n\nline two");
    }

    [Fact]
    public void Parse_EmptyBoldAndCodeMarkers_TreatedAsLiteralText()
    {
        var bold = (ParagraphBlock)AnswerMarkdownParser.Parse("a ****b")[0];
        bold.Inlines.Should().ContainSingle().Which.Should().BeOfType<TextRun>()
            .Which.Text.Should().Be("a ****b");

        var code = (ParagraphBlock)AnswerMarkdownParser.Parse("a ``b")[0];
        code.Inlines.Should().ContainSingle().Which.Should().BeOfType<TextRun>()
            .Which.Text.Should().Be("a ``b");
    }
}
