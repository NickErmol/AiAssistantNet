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

            // Note: a blank line between bullets splits them into separate ListBlocks — acceptable
            // given the constrained answer-prompt output (the renderer treats adjacent lists the same).
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

    private static List<MarkdownInline> ParseInlines(string text)
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
                if (close > i + 2)
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
                if (close > i + 1)
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
