using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloxDev.Docs.ViewModels;

using MdInline = Markdig.Syntax.Inlines.Inline;

namespace VeloxDev.Docs;

public partial class MarkdownView : WikiElementViewBase
{
    private static readonly FontFamily ContentFontFamily = new("Microsoft YaHei UI,Microsoft JhengHei UI,PingFang SC,Hiragino Sans GB,Noto Sans CJK SC,Segoe UI,Inter,sans-serif");
    private static readonly FontFamily MonospaceFontFamily = new("Cascadia Code,Cascadia Mono,Sarasa Mono SC,Microsoft YaHei UI,Segoe UI,monospace");

    private readonly AvaloniaList<Control> _displayBlocks = [];
    private MarkdownProvider? _provider;

    public MarkdownView()
    {
        InitializeComponent();
        InitializeEditChrome(ChromeBorder, DisplayPanel, EditPanel);
        DisplayItems.ItemsSource = _displayBlocks;
        DataContextChanged += (_, _) => AttachProvider();
        AttachProvider();
    }

    private void AttachProvider()
    {
        if (_provider is not null)
            _provider.PropertyChanged -= ProviderPropertyChanged;

        _provider = DataContext as MarkdownProvider;
        if (_provider is not null)
            _provider.PropertyChanged += ProviderPropertyChanged;

        RebuildDisplay();
    }

    private void ProviderPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MarkdownProvider.Text))
            RebuildDisplay();
    }

    private void RebuildDisplay()
    {
        _displayBlocks.Clear();

        var markdown = _provider?.Text;
        if (string.IsNullOrWhiteSpace(markdown))
        {
            _displayBlocks.Add(new TextBlock
            {
                Text = "No markdown",
                FontStyle = FontStyle.Italic,
                Opacity = 0.45,
                FontSize = 13,
                FontFamily = ContentFontFamily
            });
            return;
        }

        var document = Markdown.Parse(markdown, new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());
        foreach (var block in document)
        {
            if (CreateControl(block) is { } control)
                _displayBlocks.Add(control);
        }

        if (_displayBlocks.Count == 0)
        {
            _displayBlocks.Add(new TextBlock
            {
                Text = markdown,
                TextWrapping = TextWrapping.NoWrap,
                FontSize = 14,
                LineHeight = 22,
                FontFamily = ContentFontFamily
            });
        }
    }

    private Control? CreateControl(Block block)
    {
        return block switch
        {
            HeadingBlock heading => CreateHeading(heading),
            ParagraphBlock paragraph => CreateParagraph(paragraph),
            QuoteBlock quote => CreateQuote(quote),
            FencedCodeBlock fencedCode => CreateCodeBlock(fencedCode, fencedCode.Info),
            CodeBlock code => CreateCodeBlock(code, null),
            ListBlock list => CreateList(list),
            Table table => CreateTable(table),
            ThematicBreakBlock => new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.Parse("#22ffffff")),
                Margin = new Avalonia.Thickness(0, 4)
            },
            _ => CreateFallback(block)
        };
    }

    private static Control CreateTable(Table table)
    {
        var provider = new TableProvider();

        var headerRow = table.OfType<TableRow>().FirstOrDefault(r => r.IsHeader);
        var bodyRows = table.OfType<TableRow>().Where(r => !r.IsHeader).ToList();

        if (headerRow is not null)
        {
            foreach (var cell in headerRow.OfType<TableCell>())
                provider.Headers.Add(ExtractBlockText(cell));
        }

        for (var col = 0; col < table.ColumnDefinitions.Count; col++)
        {
            provider.Alignments.Add(table.ColumnDefinitions[col].Alignment switch
            {
                TableColumnAlign.Center => "Center",
                TableColumnAlign.Right => "Right",
                _ => "Left"
            });
        }

        foreach (var row in bodyRows)
        {
            var rowProvider = new TableRowProvider();
            foreach (var cell in row.OfType<TableCell>())
                rowProvider.Cells.Add(ExtractBlockText(cell));
            provider.Rows.Add(rowProvider);
        }

        return new TableView
        {
            DataContext = provider,
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
            IsHitTestVisible = false
        };
    }

    private Control CreateHeading(HeadingBlock heading)
    {
        var level = Math.Clamp(heading.Level, 1, 6);
        return CreateInlineTextControl(
            heading.Inline,
            level switch
            {
                1 => 24,
                2 => 20,
                3 => 18,
                4 => 16,
                5 => 14,
                _ => 13
            },
            fontWeight: FontWeight.Bold);
    }

    private Control CreateParagraph(ParagraphBlock paragraph)
    {
        return CreateInlineTextControl(paragraph.Inline, 14, 22);
    }

    private Control CreateQuote(QuoteBlock quote)
    {
        var host = new StackPanel { Spacing = 6 };
        foreach (var child in quote)
        {
            if (CreateControl(child) is { } control)
                host.Children.Add(control);
        }

        if (host.Children.Count == 0)
        {
            host.Children.Add(CreateInlineTextControl(null, 14, 22, fontStyle: FontStyle.Italic, fallbackText: ExtractBlockText(quote)));
        }

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#4D9EF5")),
            BorderThickness = new Avalonia.Thickness(4, 0, 0, 0),
            Padding = new Avalonia.Thickness(10, 4),
            Child = host
        };
    }

    private static Control CreateCodeBlock(CodeBlock codeBlock, string? info)
    {
        var provider = new CodeProvider
        {
            Language = NormalizeLanguage(info),
            Code = ExtractCode(codeBlock)
        };

        return new CodeView
        {
            DataContext = provider,
            IsHitTestVisible = false
        };
    }

    private Control CreateList(ListBlock list)
    {
        var host = new StackPanel
        {
            Spacing = 4,
            Margin = new Avalonia.Thickness(8, 0, 0, 0)
        };

        var index = 1;
        _ = int.TryParse(list.OrderedStart?.ToString(), out index);
        if (index < 1)
            index = 1;

        foreach (var item in list.OfType<ListItemBlock>())
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*")
            };

            row.Children.Add(new TextBlock
            {
                Text = list.IsOrdered ? $"{index}." : "•",
                Margin = new Avalonia.Thickness(0, 0, 8, 0),
                FontSize = 14,
                LineHeight = 22,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                FontFamily = ContentFontFamily
            });

            var content = CreateInlineTextControl(FindInline(item), 14, 22, fallbackText: ExtractBlockText(item));
            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            host.Children.Add(row);

            if (list.IsOrdered)
                index++;
        }

        return host;
    }

    private static Control? CreateFallback(Block block)
    {
        var text = ExtractBlockText(block);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return new SelectableTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            LineHeight = 22,
            FontFamily = ContentFontFamily
        };
    }

    private Control CreateInlineTextControl(
        ContainerInline? inline,
        double fontSize,
        double lineHeight = 0,
        FontWeight? fontWeight = null,
        FontStyle? fontStyle = null,
        string? fallbackText = null)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
            FontFamily = ContentFontFamily
        };

        if (lineHeight > 0)
            textBlock.LineHeight = lineHeight;
        if (fontWeight is { } weight)
            textBlock.FontWeight = weight;
        if (fontStyle is { } style)
            textBlock.FontStyle = style;

        if (inline is null)
        {
            textBlock.Text = fallbackText ?? string.Empty;
            return textBlock;
        }

        var inlines = textBlock.Inlines;
        if (inlines is not null)
            AppendInlines(inlines, inline.FirstChild, new InlineRenderStyle(fontWeight ?? textBlock.FontWeight, fontStyle ?? textBlock.FontStyle, false));

        if (inlines is null || inlines.Count == 0)
            textBlock.Text = fallbackText ?? ExtractInlineText(inline);

        return textBlock;
    }

    private void AppendInlines(InlineCollection inlines, MdInline? inline, InlineRenderStyle style)
    {
        for (var current = inline; current is not null; current = current.NextSibling)
        {
            switch (current)
            {
                case LiteralInline literal:
                    var literalText = literal.Content.ToString();
                    if (!string.IsNullOrEmpty(literalText))
                        inlines.Add(CreateRun(literalText, style));
                    break;
                case CodeInline code:
                    inlines.Add(CreateRun(code.Content, style with { IsCode = true }));
                    break;
                case LineBreakInline:
                    inlines.Add(new LineBreak());
                    break;
                case LinkInline link when !link.IsImage:
                    inlines.Add(CreateLinkInline(link, style));
                    break;
                case EmphasisInline emphasis:
                    AppendInlines(inlines, emphasis.FirstChild, style.Merge(emphasis));
                    break;
                case ContainerInline container:
                    AppendInlines(inlines, container.FirstChild, style);
                    break;
            }
        }
    }

    private static Run CreateRun(string text, InlineRenderStyle style)
    {
        var run = new Run(text)
        {
            FontWeight = style.FontWeight,
            FontStyle = style.FontStyle
        };

        if (style.IsCode)
            run.FontFamily = MonospaceFontFamily;

        return run;
    }

    private InlineUIContainer CreateLinkInline(LinkInline link, InlineRenderStyle style)
    {
        var label = ExtractInlineText(link);
        var url = link.Url;
        var button = new Button
        {
            Content = string.IsNullOrWhiteSpace(label) ? url : label,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            Padding = new Avalonia.Thickness(0),
            Margin = new Avalonia.Thickness(0),
            Foreground = new SolidColorBrush(Color.Parse("#4D9EF5")),
            FontWeight = style.FontWeight,
            FontStyle = style.FontStyle,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        if (style.IsCode)
            button.FontFamily = MonospaceFontFamily;

        if (!string.IsNullOrWhiteSpace(url))
        {
            button.Click += async (_, _) => await OpenInlineLinkAsync(url).ConfigureAwait(true);
            ToolTip.SetTip(button, url);
        }
        else
        {
            button.IsEnabled = false;
        }

        return new InlineUIContainer { Child = button };
    }

    private async Task OpenInlineLinkAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
            await launcher.LaunchUriAsync(uri).ConfigureAwait(true);
    }

    private static ContainerInline? FindInline(ContainerBlock block)
    {
        foreach (var child in block)
        {
            if (child is LeafBlock leaf && leaf.Inline is not null)
                return leaf.Inline;
        }

        return null;
    }

    private static string NormalizeLanguage(string? info)
    {
        if (string.IsNullOrWhiteSpace(info))
            return "markdown";

        return info
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?
            .ToLowerInvariant() ?? "markdown";
    }

    private static string ExtractCode(CodeBlock codeBlock)
    {
        return codeBlock.Lines.ToString() ?? string.Empty;
    }

    private static string ExtractBlockText(Block block)
    {
        var builder = new StringBuilder();

        switch (block)
        {
            case LeafBlock leaf when leaf.Inline is not null:
                builder.Append(ExtractInlineText(leaf.Inline));
                break;
            case CodeBlock code:
                builder.Append(ExtractCode(code));
                break;
            case ContainerBlock container:
                foreach (var child in container)
                {
                    var text = ExtractBlockText(child);
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    if (builder.Length > 0)
                        builder.AppendLine();

                    builder.Append(text);
                }
                break;
        }

        return builder.ToString().Trim();
    }

    private static string ExtractInlineText(ContainerInline? inline)
    {
        if (inline is null)
            return string.Empty;

        var builder = new StringBuilder();
        AppendInlineText(inline.FirstChild, builder);
        return builder.ToString().Trim();
    }

    private static void AppendInlineText(MdInline? inline, StringBuilder builder)
    {
        for (var current = inline; current is not null; current = current.NextSibling)
        {
            switch (current)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    builder.Append(code.Content);
                    break;
                case LineBreakInline:
                    builder.AppendLine();
                    break;
                case LinkInline link:
                    var label = ExtractInlineText(link);
                    if (!string.IsNullOrWhiteSpace(label))
                        builder.Append(label);

                    if (!string.IsNullOrWhiteSpace(link.Url))
                    {
                        if (!string.IsNullOrWhiteSpace(label))
                            builder.Append(" (").Append(link.Url).Append(')');
                        else
                            builder.Append(link.Url);
                    }
                    break;
                case ContainerInline container:
                    AppendInlineText(container.FirstChild, builder);
                    break;
            }
        }
    }

    private readonly record struct InlineRenderStyle(FontWeight FontWeight, FontStyle FontStyle, bool IsCode)
    {
        public InlineRenderStyle Merge(EmphasisInline emphasis)
        {
            var weight = FontWeight;
            var style = FontStyle;

            if (emphasis.DelimiterCount >= 2)
                weight = FontWeight.Bold;
            else
                style = FontStyle.Italic;

            return this with { FontWeight = weight, FontStyle = style };
        }
    }
}
