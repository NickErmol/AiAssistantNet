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
