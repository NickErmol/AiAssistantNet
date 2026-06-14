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

    private static Run ToInline(MarkdownInline inline)
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
                return inline is TextRun t ? new Run(t.Text) : new Run(string.Empty);
        }
    }

    private StackPanel List(ListBlock l)
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

    private Border Code(CodeBlock c)
    {
        var tb = new TextBlock
        {
            Text = c.Code,
            FontFamily = MonoFont,
            FontSize = BaseFontSize,
            TextWrapping = TextWrapping.Wrap
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Foreground.Primary");

        var border = new Border
        {
            Padding = new Thickness(8),
            Margin = new Thickness(0, 2, 0, 6),
            CornerRadius = new CornerRadius(3),
            Child = tb
        };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.Markdown.CodeBackground");
        return border;
    }
}
