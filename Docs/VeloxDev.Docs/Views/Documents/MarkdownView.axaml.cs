using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Svg.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using VeloxDev.Docs.ViewModels;
using MdInline = Markdig.Syntax.Inlines.Inline;

namespace VeloxDev.Docs;

public partial class MarkdownView : WikiElementViewBase
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly FontFamily MonospaceFontFamily = new("Cascadia Code,Cascadia Mono,Sarasa Mono SC,Microsoft YaHei UI,Segoe UI,monospace");

    // Build the Markdig pipeline once: UseAdvancedExtensions wires up many
    // extensions and is not cheap to repeat on every keystroke or document reload.
    private static readonly MarkdownPipeline MarkdownPipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    private const int InitialBlockBatchSize = 6;

    private readonly AvaloniaList<Control> _displayBlocks = [];
    private readonly Dictionary<string, Control> _anchors = new(StringComparer.OrdinalIgnoreCase);
    private MarkdownProvider? _provider;
    private int _rebuildToken;

    public MarkdownView()
    {
        InitializeComponent();
        InitializeEditChrome(ChromeBorder, DisplayPanel, EditPanel);
        DisplayItems.ItemsSource = _displayBlocks;
        PreferOwnScrolling(DisplayPanel);
        PreferOwnScrolling(EditScrollViewer);
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

        ApplyHeightMode();
        RebuildDisplay();
    }

    private void ProviderPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MarkdownProvider.Text) ||
            e.PropertyName == nameof(MarkdownProvider.EmbeddedCodeAutoHeight) ||
            e.PropertyName == nameof(MarkdownProvider.EmbeddedCodeMaxHeightValue))
            RebuildDisplay();
        else if (e.PropertyName == nameof(MarkdownProvider.AutoHeight) ||
                 e.PropertyName == nameof(MarkdownProvider.MaxHeightValue))
            ApplyHeightMode();
    }

    private void ApplyHeightMode()
    {
        var autoHeight = _provider?.AutoHeight ?? false;
        var maxHeight = Math.Max(120, _provider?.MaxHeightValue ?? 420);

        DisplayPanel.MaxHeight = autoHeight ? double.PositiveInfinity : maxHeight;
        DisplayPanel.VerticalScrollBarVisibility = autoHeight
            ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        EditScrollViewer.MaxHeight = autoHeight ? double.PositiveInfinity : maxHeight;
        EditScrollViewer.VerticalScrollBarVisibility = autoHeight
            ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;

        DisplayPanel.InvalidateMeasure();
        DisplayPanel.InvalidateArrange();
        EditScrollViewer.InvalidateMeasure();
        EditScrollViewer.InvalidateArrange();
        InvalidateMeasure();
        InvalidateArrange();
    }

    private void RebuildDisplay()
    {
        // Bump the token so any in-flight chunked rebuild from a previous call
        // stops appending stale content.
        var token = ++_rebuildToken;

        _displayBlocks.Clear();
        _anchors.Clear();

        var markdown = _provider?.Text;
        if (string.IsNullOrWhiteSpace(markdown))
        {
            _displayBlocks.Add(new TextBlock
            {
                Text = "No markdown",
                FontStyle = FontStyle.Italic,
                Opacity = 0.45,
                FontSize = 13
            });
            return;
        }

        // Markdig parsing itself is fast; the cost is in materializing
        // controls (especially fenced code blocks). We render the first batch
        // synchronously so the user sees content immediately, then yield to
        // the dispatcher to render the rest. This keeps document loading
        // responsive when many MarkdownView instances rebuild at once.
        var document = Markdown.Parse(markdown, MarkdownPipeline);
        var blocks = new List<Block>(document.Count);
        foreach (var block in document)
            blocks.Add(block);

        var index = RenderBlocks(blocks, 0, InitialBlockBatchSize, token);

        if (index < blocks.Count)
            ScheduleRenderRemainder(blocks, index, token);
        else if (_displayBlocks.Count == 0)
            AppendFallback(markdown);
    }

    private int RenderBlocks(List<Block> blocks, int start, int count, int token)
    {
        var end = Math.Min(blocks.Count, start + count);
        for (int i = start; i < end; i++)
        {
            if (token != _rebuildToken)
                return blocks.Count;

            if (CreateControl(blocks[i]) is { } control)
                _displayBlocks.Add(control);
        }
        return end;
    }

    private void ScheduleRenderRemainder(List<Block> blocks, int start, int token)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (token != _rebuildToken)
                return;

            var next = RenderBlocks(blocks, start, InitialBlockBatchSize, token);

            if (next < blocks.Count)
                ScheduleRenderRemainder(blocks, next, token);
            else if (_displayBlocks.Count == 0)
                AppendFallback(_provider?.Text ?? string.Empty);
        }, DispatcherPriority.Background);
    }

    private void AppendFallback(string markdown)
    {
        _displayBlocks.Add(new TextBlock
        {
            Text = markdown,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            LineHeight = 22
        });
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

    private Control CreateTable(Table table)
    {
        var headerRow = table.OfType<TableRow>().FirstOrDefault(r => r.IsHeader);
        var bodyRows = table.OfType<TableRow>().Where(r => !r.IsHeader).ToList();
        var rowCount = bodyRows.Count + (headerRow is null ? 0 : 1);
        var columnCount = new[]
        {
            table.ColumnDefinitions.Count,
            headerRow?.OfType<TableCell>().Count() ?? 0,
            bodyRows.Select(row => row.OfType<TableCell>().Count()).DefaultIfEmpty(0).Max()
        }.Max();

        var grid = new Grid
        {
            ColumnSpacing = 0,
            RowSpacing = 0
        };

        for (var col = 0; col < columnCount; col++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var row = 0; row < rowCount; row++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var rowIndex = 0;
        if (headerRow is not null)
        {
            var cells = headerRow.OfType<TableCell>().ToList();
            for (var col = 0; col < columnCount; col++)
            {
                var cell = col < cells.Count ? cells[col] : null;
                var control = CreateTableCell(cell, isHeader: true, GetTableAlignment(table, col));
                Grid.SetRow(control, rowIndex);
                Grid.SetColumn(control, col);
                grid.Children.Add(control);
            }

            rowIndex++;
        }

        foreach (var row in bodyRows)
        {
            var cells = row.OfType<TableCell>().ToList();
            for (var col = 0; col < columnCount; col++)
            {
                var cell = col < cells.Count ? cells[col] : null;
                var control = CreateTableCell(cell, isHeader: false, GetTableAlignment(table, col));
                Grid.SetRow(control, rowIndex);
                Grid.SetColumn(control, col);
                grid.Children.Add(control);
            }

            rowIndex++;
        }

        return new Border
        {
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(6),
            ClipToBounds = true,
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                IsScrollChainingEnabled = false,
                Content = grid
            }
        };
    }

    private Control CreateHeading(HeadingBlock heading)
    {
        var level = Math.Clamp(heading.Level, 1, 6);
        double fontSize = level switch
        {
            1 => 24,
            2 => 20,
            3 => 18,
            4 => 16,
            5 => 14,
            _ => 13
        };
        var control = CreateInlineTextControl(
            heading.Inline,
            fontSize,
            fallbackText: ExtractInlineText(heading.Inline));
        control.Margin = new Avalonia.Thickness(0, 0, 0, 4);
        RegisterAnchor(ExtractInlineText(heading.Inline), control);
        return control;
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

    private Control CreateCodeBlock(CodeBlock codeBlock, string? info)
    {
        var provider = new CodeProvider
        {
            Language = NormalizeLanguage(info),
            Code = ExtractCode(codeBlock),
            AutoHeight = _provider?.EmbeddedCodeAutoHeight ?? false,
            MaxHeightValue = Math.Max(120, _provider?.EmbeddedCodeMaxHeightValue ?? 300)
        };

        return new CodeView
        {
            DataContext = provider
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
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
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
            LineHeight = 22
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
            FontSize = fontSize
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

        if (ContainsInteractiveInline(inline.FirstChild))
            return CreateInteractiveInlineTextControl(inline.FirstChild, fontSize, lineHeight, fontWeight ?? textBlock.FontWeight, fontStyle ?? textBlock.FontStyle, fallbackText);

        var inlines = textBlock.Inlines;
        if (inlines is not null)
            AppendInlines(inlines, inline.FirstChild, new InlineRenderStyle(fontWeight ?? textBlock.FontWeight, fontStyle ?? textBlock.FontStyle, false, textBlock.LineHeight));

        if (inlines is null || inlines.Count == 0)
            textBlock.Text = fallbackText ?? ExtractInlineText(inline);

        return textBlock;
    }

    private Control CreateInteractiveInlineTextControl(
        MdInline? inline,
        double fontSize,
        double lineHeight,
        FontWeight fontWeight,
        FontStyle fontStyle,
        string? fallbackText)
    {
        var root = new StackPanel
        {
            Spacing = 0
        };

        var currentLine = CreateInteractiveLineHost();
        root.Children.Add(currentLine);

        AppendInteractiveInlineControls(root, ref currentLine, inline, new InlineRenderStyle(fontWeight, fontStyle, false, lineHeight), fontSize);

        if (currentLine.Children.Count == 0 && root.Children.Count == 1)
        {
            currentLine.Children.Add(CreateFlowTextBlock(fallbackText ?? string.Empty, new InlineRenderStyle(fontWeight, fontStyle, false, lineHeight), fontSize));
        }

        return root;
    }

    private static WrapPanel CreateInteractiveLineHost() => new()
    {
        Orientation = Avalonia.Layout.Orientation.Horizontal,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
    };

    private void AppendInteractiveInlineControls(
        StackPanel root,
        ref WrapPanel currentLine,
        MdInline? inline,
        InlineRenderStyle style,
        double fontSize)
    {
        for (var current = inline; current is not null; current = current.NextSibling)
        {
            switch (current)
            {
                case LiteralInline literal:
                    foreach (var token in TokenizeInlineText(GetLiteralText(literal)))
                        currentLine.Children.Add(CreateFlowTextBlock(token, style, fontSize));
                    break;
                case CodeInline code:
                    currentLine.Children.Add(CreateFlowTextBlock(code.Content, style with { IsCode = true }, fontSize));
                    break;
                case LineBreakInline:
                    currentLine = CreateInteractiveLineHost();
                    root.Children.Add(currentLine);
                    break;
                case LinkInline image when image.IsImage:
                    currentLine.Children.Add(CreateInlineImageControl(image, style));
                    break;
                case LinkInline link when !link.IsImage:
                    currentLine.Children.Add(CreateStandaloneLinkControl(link, style, fontSize));
                    break;
                case EmphasisInline emphasis:
                    AppendInteractiveInlineControls(root, ref currentLine, emphasis.FirstChild, style.Merge(emphasis), fontSize);
                    break;
                case ContainerInline container:
                    AppendInteractiveInlineControls(root, ref currentLine, container.FirstChild, style, fontSize);
                    break;
            }
        }
    }

    private void AppendInlines(InlineCollection inlines, MdInline? inline, InlineRenderStyle style)
    {
        for (var current = inline; current is not null; current = current.NextSibling)
        {
            switch (current)
            {
                case LiteralInline literal:
                    var literalText = GetLiteralText(literal);
                    if (!string.IsNullOrEmpty(literalText))
                        inlines.Add(CreateRun(literalText, style));
                    break;
                case CodeInline code:
                    inlines.Add(CreateRun(code.Content, style with { IsCode = true }));
                    break;
                case LineBreakInline:
                    inlines.Add(new LineBreak());
                    break;
                case LinkInline image when image.IsImage:
                    inlines.Add(new InlineUIContainer { Child = CreateInlineImageControl(image, style) });
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
        return new InlineUIContainer { Child = CreateStandaloneLinkControl(link, style, 14) };
    }

    private async Task OpenInlineLinkAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        if (url.StartsWith("#", StringComparison.Ordinal))
        {
            await ScrollToAnchorAsync(url).ConfigureAwait(true);
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
            await launcher.LaunchUriAsync(uri).ConfigureAwait(true);
    }

    private async Task ScrollToAnchorAsync(string fragment)
    {
        var key = NormalizeAnchor(fragment);
        if (string.IsNullOrWhiteSpace(key) || !_anchors.TryGetValue(key, out var target))
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var autoHeight = _provider?.AutoHeight ?? false;
            var scrollHost = autoHeight
                ? FindAncestorScrollViewer()
                : DisplayPanel;

            if (scrollHost is null)
                return;

            var point = target.TransformToVisual(scrollHost)?.Transform(new Point());
            if (point is { } anchorTop)
            {
                var maxY = Math.Max(0, scrollHost.Extent.Height - scrollHost.Viewport.Height);
                var targetOffsetY = Math.Clamp(scrollHost.Offset.Y + anchorTop.Y - 4, 0, maxY);
                scrollHost.Offset = new Vector(scrollHost.Offset.X, targetOffsetY);
            }
        }, DispatcherPriority.Background);
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
                    builder.Append(GetLiteralText(literal));
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

    private static string GetLiteralText(LiteralInline literal)
    {
        var content = literal.Content;
        if (string.IsNullOrEmpty(content.Text) || content.Start < 0 || content.End < content.Start)
            return string.Empty;

        var length = content.End - content.Start + 1;
        if (content.Start + length > content.Text.Length)
            return content.ToString();

        return content.Text.Substring(content.Start, length);
    }

    private void RegisterAnchor(string? text, Control control)
    {
        foreach (var key in GetAnchorAliases(text))
            _anchors[key] = control;
    }

    private ScrollViewer? FindAncestorScrollViewer()
    {
        Visual? current = this;
        while (current is not null)
        {
            if (current is ScrollViewer scrollViewer && !ReferenceEquals(scrollViewer, DisplayPanel))
                return scrollViewer;

            current = current.GetVisualParent();
        }

        return null;
    }

    private static string NormalizeAnchor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return GetAnchorAliases(text).FirstOrDefault() ?? string.Empty;
    }

    private static IEnumerable<string> GetAnchorAliases(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        var input = Uri.UnescapeDataString(text.Trim().TrimStart('#'));

        var slug = BuildAnchorSlug(input, trimEdgeHyphens: false);
        if (!string.IsNullOrWhiteSpace(slug))
            yield return slug;

        var trimmedSlug = BuildAnchorSlug(input, trimEdgeHyphens: true);
        if (!string.IsNullOrWhiteSpace(trimmedSlug) && !string.Equals(trimmedSlug, slug, StringComparison.Ordinal))
            yield return trimmedSlug;

        var compact = new string(input
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        if (!string.IsNullOrWhiteSpace(compact) &&
            !string.Equals(compact, slug, StringComparison.Ordinal) &&
            !string.Equals(compact, trimmedSlug, StringComparison.Ordinal))
        {
            yield return compact;
        }
    }

    private static string BuildAnchorSlug(string input, bool trimEdgeHyphens)
    {
        var builder = new StringBuilder(input.Length);
        var lastWasHyphen = false;

        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                lastWasHyphen = false;
            }
            else if ((char.IsWhiteSpace(c) || c == '-') && !lastWasHyphen)
            {
                builder.Append('-');
                lastWasHyphen = true;
            }
        }

        var result = builder.ToString();
        return trimEdgeHyphens ? result.Trim('-') : result;
    }

    private static TextAlignment GetTableAlignment(Table table, int index)
    {
        if (index < 0 || index >= table.ColumnDefinitions.Count)
            return TextAlignment.Left;

        return table.ColumnDefinitions[index].Alignment switch
        {
            TableColumnAlign.Center => TextAlignment.Center,
            TableColumnAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Left
        };
    }

    private Control CreateTableCell(TableCell? cell, bool isHeader, TextAlignment alignment)
    {
        var content = CreateTableCellContent(cell, alignment);
        return new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Avalonia.Thickness(0, 0, 1, 1),
            Background = isHeader
                ? new SolidColorBrush(Color.FromArgb(48, 128, 128, 128))
                : new SolidColorBrush(Color.FromArgb(12, 128, 128, 128)),
            Padding = new Avalonia.Thickness(6),
            MinWidth = 120,
            Child = content
        };
    }

    private Control CreateTableCellContent(TableCell? cell, TextAlignment alignment)
    {
        if (cell is null)
            return new TextBlock();

        var host = new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = alignment switch
            {
                TextAlignment.Center => Avalonia.Layout.HorizontalAlignment.Center,
                TextAlignment.Right => Avalonia.Layout.HorizontalAlignment.Right,
                _ => Avalonia.Layout.HorizontalAlignment.Stretch
            }
        };

        foreach (var block in cell)
        {
            var control = CreateControl(block);
            if (control is null)
                continue;

            ApplyAlignment(control, alignment);
            host.Children.Add(control);
        }

        if (host.Children.Count == 0)
        {
            var text = new TextBlock();
            ApplyAlignment(text, alignment);
            host.Children.Add(text);
        }

        return host;
    }

    private static void ApplyAlignment(Control control, TextAlignment alignment)
    {
        if (control is TextBlock textBlock)
            textBlock.TextAlignment = alignment;

        control.HorizontalAlignment = alignment switch
        {
            TextAlignment.Center => Avalonia.Layout.HorizontalAlignment.Center,
            TextAlignment.Right => Avalonia.Layout.HorizontalAlignment.Right,
            _ => Avalonia.Layout.HorizontalAlignment.Left
        };
    }

    private Control CreateLinkContent(LinkInline link, InlineRenderStyle style)
    {
        var controls = CreateInlineContentControls(link.FirstChild, style);
        if (controls.Count == 0)
        {
            var label = ExtractInlineText(link);
            return CreateLinkTextBlock(string.IsNullOrWhiteSpace(label) ? link.Url : label, style);
        }

        if (controls.Count == 1)
            return controls[0];

        var panel = new WrapPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        foreach (var control in controls)
        {
            if (control is Control child)
                child.Margin = new Avalonia.Thickness(0, 0, 4, 0);
            panel.Children.Add(control);
        }

        return panel;
    }

    private Control CreateStandaloneLinkControl(LinkInline link, InlineRenderStyle style, double fontSize)
    {
        var url = link.Url;
        var isPressed = false;
        var host = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Avalonia.Thickness(0),
            Margin = new Avalonia.Thickness(0),
            MinHeight = style.LineHeight > 0 ? style.LineHeight : 0,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Child = CreateLinkContent(link, style with { LineHeight = style.LineHeight > 0 ? style.LineHeight : fontSize + 6 }),
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        if (!string.IsNullOrWhiteSpace(url))
        {
            host.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
                {
                    isPressed = true;
                    e.Handled = true;
                }
            };

            host.PointerReleased += async (_, e) =>
            {
                if (!isPressed)
                    return;

                isPressed = false;
                if (host.IsPointerOver)
                {
                    e.Handled = true;
                    await OpenInlineLinkAsync(url).ConfigureAwait(true);
                }
            };

            host.PointerCaptureLost += (_, _) => isPressed = false;
            host.PointerExited += (_, _) => isPressed = false;
            ToolTip.SetTip(host, url);
        }
        else
        {
            host.IsHitTestVisible = false;
            host.Cursor = Cursor.Default;
        }

        return host;
    }

    private static TextBlock CreateFlowTextBlock(string? text, InlineRenderStyle style, double fontSize)
    {
        return new TextBlock
        {
            Text = text ?? string.Empty,
            FontSize = fontSize,
            TextWrapping = TextWrapping.NoWrap,
            TextDecorations = null,
            LineHeight = style.LineHeight > 0 ? style.LineHeight : 0,
            FontWeight = style.FontWeight,
            FontStyle = style.FontStyle,
            FontFamily = style.IsCode ? MonospaceFontFamily : null,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
        };
    }

    private static TextBlock CreateLinkTextBlock(string? text, InlineRenderStyle style)
    {
        return new TextBlock
        {
            Text = text ?? string.Empty,
            TextWrapping = TextWrapping.Wrap,
            TextDecorations = null,
            Foreground = new SolidColorBrush(Color.Parse("#4D9EF5")),
            LineHeight = style.LineHeight > 0 ? style.LineHeight : 0,
            FontWeight = style.FontWeight,
            FontStyle = style.FontStyle,
            FontFamily = style.IsCode ? MonospaceFontFamily : null,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
        };
    }

    private List<Control> CreateInlineContentControls(MdInline? inline, InlineRenderStyle style)
    {
        var controls = new List<Control>();
        for (var current = inline; current is not null; current = current.NextSibling)
        {
            switch (current)
            {
                case LiteralInline literal:
                    var literalText = GetLiteralText(literal);
                    if (!string.IsNullOrWhiteSpace(literalText))
                    {
                        controls.Add(CreateLinkTextBlock(literalText, style));
                    }
                    break;
                case CodeInline code:
                    controls.Add(CreateLinkTextBlock(code.Content, style with { IsCode = true }));
                    break;
                case LinkInline image when image.IsImage:
                    controls.Add(CreateInlineImageControl(image, style));
                    break;
                case EmphasisInline emphasis:
                    controls.AddRange(CreateInlineContentControls(emphasis.FirstChild, style.Merge(emphasis)));
                    break;
                case ContainerInline container:
                    controls.AddRange(CreateInlineContentControls(container.FirstChild, style));
                    break;
            }
        }

        return controls;
    }

    private Control CreateInlineImageControl(LinkInline image, InlineRenderStyle style)
    {
        var altText = ExtractInlineText(image);
        if (string.IsNullOrWhiteSpace(image.Url))
            return new TextBlock { Text = altText };

        var imageControl = new Image
        {
            MaxHeight = style.LineHeight > 0 ? Math.Max(12, style.LineHeight - 4) : 18,
            Stretch = Stretch.Uniform,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        var host = new ContentControl
        {
            Content = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                Child = imageControl,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            },
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        _ = LoadInlineImageAsync(host, imageControl, image.Url, altText);
        return host;
    }

    private static async Task LoadInlineImageAsync(ContentControl host, Image image, string source, string fallbackText)
    {
        try
        {
            var rendered = await LoadImageSourceAsync(source).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => image.Source = rendered);
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(fallbackText))
            {
                await Dispatcher.UIThread.InvokeAsync(() => host.Content = new TextBlock
                {
                    Text = fallbackText,
                    TextWrapping = TextWrapping.Wrap,
                    TextDecorations = null,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                });
            }
        }
    }

    private static async Task<IImage?> LoadImageSourceAsync(string source)
    {
        await using var stream = await OpenImageStreamAsync(source).ConfigureAwait(false);
        if (stream is null)
            return null;

        if (IsSvgFile(stream))
            return new SvgImage
            {
                Source = SvgSource.LoadFromStream(stream)
            };

        return new Bitmap(stream);
    }

    private static async Task<Stream?> OpenImageStreamAsync(string source)
    {
        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = await HttpClient.GetByteArrayAsync(source).ConfigureAwait(false);
            return new MemoryStream(bytes, writable: false);
        }

        var path = source.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? new Uri(source).LocalPath
            : source;

        if (!File.Exists(path))
            return null;

        await using var fileStream = File.OpenRead(path);
        var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream).ConfigureAwait(false);
        memoryStream.Position = 0;
        return memoryStream;
    }

    private static bool IsSvgFile(Stream stream)
    {
        if (stream.Length == 0)
            return false;

        try
        {
            const int bufferSize = 512;
            var length = (int)Math.Min(bufferSize, stream.Length);
            var buffer = new byte[length];
            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            var header = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            return header.Contains("<svg", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            stream.Position = 0;
        }
    }

    private static bool LooksLikeSvg(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        return source.Contains(".svg", StringComparison.OrdinalIgnoreCase)
            || source.Contains("image/svg", StringComparison.OrdinalIgnoreCase)
            || source.Contains("logo=", StringComparison.OrdinalIgnoreCase) && source.Contains("img.shields.io", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsInteractiveInline(MdInline? inline)
    {
        for (var current = inline; current is not null; current = current.NextSibling)
        {
            switch (current)
            {
                case LinkInline:
                    return true;
                case ContainerInline container when ContainsInteractiveInline(container.FirstChild):
                    return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> TokenizeInlineText(string text)
    {
        if (string.IsNullOrEmpty(text))
            yield break;

        var builder = new StringBuilder();
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (builder.Length > 0)
                {
                    yield return builder.ToString();
                    builder.Clear();
                }

                yield return c.ToString();
                continue;
            }

            if (IsIndependentWrapToken(c))
            {
                if (builder.Length > 0)
                {
                    yield return builder.ToString();
                    builder.Clear();
                }

                yield return c.ToString();
                continue;
            }

            builder.Append(c);
        }

        if (builder.Length > 0)
            yield return builder.ToString();
    }

    private static bool IsIndependentWrapToken(char c)
    {
        var category = char.GetUnicodeCategory(c);
        return c >= 0x2E80
            || category == System.Globalization.UnicodeCategory.OtherPunctuation
            || category == System.Globalization.UnicodeCategory.DashPunctuation
            || category == System.Globalization.UnicodeCategory.OpenPunctuation
            || category == System.Globalization.UnicodeCategory.ClosePunctuation
            || category == System.Globalization.UnicodeCategory.InitialQuotePunctuation
            || category == System.Globalization.UnicodeCategory.FinalQuotePunctuation;
    }

    private readonly record struct InlineRenderStyle(FontWeight FontWeight, FontStyle FontStyle, bool IsCode, double LineHeight)
    {
        public InlineRenderStyle Merge(EmphasisInline emphasis)
        {
            var weight = FontWeight;
            var style = FontStyle;

            if (emphasis.DelimiterCount < 2)
                style = FontStyle.Italic;

            return this with { FontWeight = weight, FontStyle = style };
        }
    }
}
