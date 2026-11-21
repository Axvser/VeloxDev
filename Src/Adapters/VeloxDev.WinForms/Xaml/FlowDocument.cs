using System.Collections.ObjectModel;
using System.Drawing;
using System.Text;

namespace VeloxDev.WinForms.Xaml;

/// <summary>
/// A mutable document-tree node that owns a sequence of <see cref="Block"/> children.
/// </summary>
/// <remarks>
/// <para>
/// The hierarchy mirrors the names of WPF's <c>System.Windows.Documents</c> types so XAML authors keep their mental
/// model, but it is an independent model: nothing here derives from a WinForms control, and rendering is done by
/// converting the tree to plain text or RTF.
/// </para>
/// <para>
/// The list container is named <see cref="ListBlock"/> rather than <c>List</c>: a public type named <c>List</c> in
/// this namespace would shadow <see cref="System.Collections.Generic.List{T}"/> for every source file in
/// <c>VeloxDev.WinForms.Xaml</c> and its child namespaces.
/// </para>
/// </remarks>
public abstract class Block
{
    /// <summary>Returns the plain-text form of this block.</summary>
    /// <returns>Text with one line per paragraph; a containing <see cref="FlowDocument"/> trims the final newline.</returns>
    public abstract string ToPlainText();

    /// <summary>Returns a complete RTF document for this block, usable by a <see cref="System.Windows.Forms.RichTextBox"/>.</summary>
    /// <returns>An RTF string; only the block itself is included.</returns>
    public string ToRtf()
    {
        var plain = new StringBuilder();
        var rtf = new StringBuilder();
        AppendFragment(plain, rtf);
        return RtfDocument.Wrap(rtf.ToString(), Color.Empty);
    }

    internal abstract void AppendFragment(StringBuilder plain, StringBuilder rtf);
}

/// <summary>
/// A mutable inline node: the leaf text and the character formatting around it.
/// </summary>
/// <remarks>Inline nodes are only meaningful inside a <see cref="Paragraph"/> or another inline container.</remarks>
public abstract class Inline
{
    /// <summary>Returns the plain-text form of this inline.</summary>
    /// <returns>The concatenated text of the inline and its descendants.</returns>
    public abstract string ToPlainText();

    /// <summary>Returns the RTF form of this inline.</summary>
    /// <returns>An RTF fragment wrapped in its own document header.</returns>
    public string ToRtf()
    {
        var plain = new StringBuilder();
        var rtf = new StringBuilder();
        AppendFragment(plain, rtf);
        return RtfDocument.Wrap(rtf.ToString(), Color.Empty);
    }

    internal abstract void AppendFragment(StringBuilder plain, StringBuilder rtf);
}

/// <summary>
/// The root of a document: a sequence of blocks rendered by the document viewers.
/// </summary>
/// <remarks>
/// Setting <see cref="Controls.FlowDocumentScrollViewer.Document"/> or <see cref="Controls.RichTextBox.Document"/>
/// loads the RTF produced by <see cref="ToRtf"/>.
/// </remarks>
public class FlowDocument : Block
{
    /// <summary>The top-level blocks of the document.</summary>
    public IList<Block> Blocks { get; } = new Collection<Block>();

    /// <summary>The collection the XAML compiler appends direct child elements to.</summary>
    /// <remarks>An alias for <see cref="Blocks"/> so that direct children need no property-element wrapper.</remarks>
    public IList<Block> Items => Blocks;

    /// <summary>Returns the document's plain text without a trailing newline.</summary>
    /// <returns>One line per paragraph, list item or table row.</returns>
    public override string ToPlainText()
    {
        var plain = new StringBuilder();
        var rtf = new StringBuilder();
        AppendBlocks(Blocks, plain, rtf);
        return plain.ToString().TrimEnd('\n');
    }

    /// <summary>Returns a complete RTF document for the whole tree.</summary>
    /// <returns>An RTF string accepted by a native rich-text editor.</returns>
    public new string ToRtf() => RtfDocument.Wrap(RtfFragment(), Color.Empty);

    internal override void AppendFragment(StringBuilder plain, StringBuilder rtf)
        => AppendBlocks(Blocks, plain, rtf);

    internal string RtfFragment()
    {
        var plain = new StringBuilder();
        var rtf = new StringBuilder();
        AppendFragment(plain, rtf);
        return rtf.ToString();
    }

    // 文档宿主按当前主题前景色生成 RTF：原生 RichTextBox 的 ForeColor 不覆盖 RTF 正文色。
    internal string ToRtf(Color foreground) => RtfDocument.Wrap(RtfFragment(), foreground);

    internal static void AppendBlocks(IList<Block>? blocks, StringBuilder plain, StringBuilder rtf)
    {
        if (blocks is null)
        {
            return;
        }

        foreach (var block in blocks)
        {
            block?.AppendFragment(plain, rtf);
        }
    }
}

/// <summary>A titled group of blocks inside a <see cref="FlowDocument"/>.</summary>
public class Section : Block
{
    /// <summary>The blocks owned by this section.</summary>
    public IList<Block> Blocks { get; } = new Collection<Block>();

    /// <summary>The collection the XAML compiler appends direct child elements to; an alias for <see cref="Blocks"/>.</summary>
    public IList<Block> Items => Blocks;

    /// <inheritdoc />
    public override string ToPlainText()
    {
        var plain = new StringBuilder();
        var rtf = new StringBuilder();
        AppendFragment(plain, rtf);
        return plain.ToString();
    }

    internal override void AppendFragment(StringBuilder plain, StringBuilder rtf)
        => FlowDocument.AppendBlocks(Blocks, plain, rtf);
}

/// <summary>A paragraph: a sequence of inline nodes ending with a line break.</summary>
public class Paragraph : Block
{
    /// <summary>The inline nodes of this paragraph.</summary>
    public IList<Inline> Inlines { get; } = new Collection<Inline>();

    /// <summary>The collection the XAML compiler appends direct child elements to; an alias for <see cref="Inlines"/>.</summary>
    public IList<Inline> Items => Inlines;

    /// <inheritdoc />
    public override string ToPlainText()
    {
        var plain = new StringBuilder();
        AppendFragment(plain, new StringBuilder());
        return plain.ToString();
    }

    internal override void AppendFragment(StringBuilder plain, StringBuilder rtf)
    {
        foreach (var inline in Inlines)
        {
            inline?.AppendFragment(plain, rtf);
        }

        // 段落自带换行：块之间不需要额外的分隔符，纯文本与 RTF 的结尾保持一致。
        plain.Append('\n');
        rtf.Append("\\par ");
    }
}

/// <summary>A bulleted or numbered list of <see cref="ListItem"/> nodes.</summary>
public class ListBlock : Block
{
    /// <summary>The items of the list.</summary>
    public IList<ListItem> ListItems { get; } = new Collection<ListItem>();

    /// <summary>The collection the XAML compiler appends direct child elements to; an alias for <see cref="ListItems"/>.</summary>
    public IList<ListItem> Items => ListItems;

    /// <inheritdoc />
    public override string ToPlainText()
    {
        var plain = new StringBuilder();
        AppendFragment(plain, new StringBuilder());
        return plain.ToString();
    }

    internal override void AppendFragment(StringBuilder plain, StringBuilder rtf)
    {
        foreach (var item in ListItems)
        {
            if (item is null)
            {
                continue;
            }

            // 列表项只用纯文本进入 RTF：嵌套格式会让 \pard 与项目符号互相踩，收益不成比例。
            var itemPlain = new StringBuilder();
            item.AppendFragment(itemPlain, new StringBuilder());
            plain.Append('•').Append(' ').Append(itemPlain);
            rtf.Append("\\pard\\bullet ").Append(RtfDocument.Escape(itemPlain.ToString())).Append("\\par ");
        }
    }
}

/// <summary>One item of a <see cref="ListBlock"/>; a sequence of blocks.</summary>
public class ListItem : Block
{
    /// <summary>The blocks owned by this item.</summary>
    public IList<Block> Blocks { get; } = new Collection<Block>();

    /// <summary>The collection the XAML compiler appends direct child elements to; an alias for <see cref="Blocks"/>.</summary>
    public IList<Block> Items => Blocks;

    /// <inheritdoc />
    public override string ToPlainText()
    {
        var plain = new StringBuilder();
        AppendFragment(plain, new StringBuilder());
        return plain.ToString();
    }

    internal override void AppendFragment(StringBuilder plain, StringBuilder rtf)
        => FlowDocument.AppendBlocks(Blocks, plain, rtf);
}

/// <summary>A table of rows; each row holds cells.</summary>
public class Table : Block
{
    /// <summary>The rows of the table.</summary>
    public IList<TableRow> Rows { get; } = new Collection<TableRow>();

    /// <summary>The collection the XAML compiler appends direct child elements to; an alias for <see cref="Rows"/>.</summary>
    public IList<TableRow> Items => Rows;

    /// <inheritdoc />
    public override string ToPlainText()
    {
        var plain = new StringBuilder();
        AppendFragment(plain, new StringBuilder());
        return plain.ToString();
    }

    internal override void AppendFragment(StringBuilder plain, StringBuilder rtf)
    {
        foreach (var row in Rows)
        {
            if (row is null)
            {
                continue;
            }

            var rowPlain = new StringBuilder();
            row.AppendFragment(rowPlain, new StringBuilder());
            plain.Append(rowPlain).Append('\n');
            rtf.Append("\\pard ").Append(RtfDocument.Escape(rowPlain.ToString().Replace('\t', ' '))).Append("\\par ");
        }
    }
}

/// <summary>One row of a <see cref="Table"/>.</summary>
public class TableRow
{
    /// <summary>The cells of the row.</summary>
    public IList<TableCell> Cells { get; } = new Collection<TableCell>();

    /// <summary>The collection the XAML compiler appends direct child elements to; an alias for <see cref="Cells"/>.</summary>
    public IList<TableCell> Items => Cells;

    internal void AppendFragment(StringBuilder plain, StringBuilder rtf)
    {
        for (var index = 0; index < Cells.Count; index++)
        {
            if (index > 0)
            {
                plain.Append('\t');
                rtf.Append("\\tab ");
            }

            Cells[index]?.AppendFragment(plain, rtf);
        }
    }
}

/// <summary>One cell of a <see cref="TableRow"/>; a sequence of blocks.</summary>
public class TableCell : Block
{
    /// <summary>The blocks owned by this cell.</summary>
    public IList<Block> Blocks { get; } = new Collection<Block>();

    /// <summary>The collection the XAML compiler appends direct child elements to; an alias for <see cref="Blocks"/>.</summary>
    public IList<Block> Items => Blocks;

    /// <inheritdoc />
    public override string ToPlainText()
    {
        var plain = new StringBuilder();
        AppendFragment(plain, new StringBuilder());
        return plain.ToString().TrimEnd('\n');
    }

    internal override void AppendFragment(StringBuilder plain, StringBuilder rtf)
    {
        var cellPlain = new StringBuilder();
        var cellRtf = new StringBuilder();
        FlowDocument.AppendBlocks(Blocks, cellPlain, cellRtf);
        plain.Append(cellPlain.ToString().TrimEnd('\n'));
        rtf.Append(cellRtf);
    }
}

/// <summary>A run of literally formatted text.</summary>
public class Run : Inline
{
    /// <summary>The text of the run.</summary>
    public string Text { get; set; } = string.Empty;

    /// <inheritdoc />
    public override string ToPlainText() => Text ?? string.Empty;

    internal override void AppendFragment(StringBuilder plain, StringBuilder rtf)
    {
        var text = Text ?? string.Empty;
        plain.Append(text);
        rtf.Append(RtfDocument.Escape(text));
    }
}

/// <summary>An inline container drawn in bold.</summary>
public class Bold : Inline
{
    /// <summary>The formatted inline nodes.</summary>
    public IList<Inline> Inlines { get; } = new Collection<Inline>();

    /// <summary>The collection the XAML compiler appends direct child elements to; an alias for <see cref="Inlines"/>.</summary>
    public IList<Inline> Items => Inlines;

    /// <inheritdoc />
    public override string ToPlainText()
    {
        var plain = new StringBuilder();
        AppendFragment(plain, new StringBuilder());
        return plain.ToString();
    }

    internal override void AppendFragment(StringBuilder plain, StringBuilder rtf)
        => RtfDocument.AppendFormatted(Inlines, plain, rtf, "\\b ");
}

/// <summary>An inline container drawn in italics.</summary>
public class Italic : Inline
{
    /// <summary>The formatted inline nodes.</summary>
    public IList<Inline> Inlines { get; } = new Collection<Inline>();

    /// <summary>The collection the XAML compiler appends direct child elements to; an alias for <see cref="Inlines"/>.</summary>
    public IList<Inline> Items => Inlines;

    /// <inheritdoc />
    public override string ToPlainText()
    {
        var plain = new StringBuilder();
        AppendFragment(plain, new StringBuilder());
        return plain.ToString();
    }

    internal override void AppendFragment(StringBuilder plain, StringBuilder rtf)
        => RtfDocument.AppendFormatted(Inlines, plain, rtf, "\\i ");
}

/// <summary>An inline container drawn with an underline.</summary>
public class Underline : Inline
{
    /// <summary>The formatted inline nodes.</summary>
    public IList<Inline> Inlines { get; } = new Collection<Inline>();

    /// <summary>The collection the XAML compiler appends direct child elements to; an alias for <see cref="Inlines"/>.</summary>
    public IList<Inline> Items => Inlines;

    /// <inheritdoc />
    public override string ToPlainText()
    {
        var plain = new StringBuilder();
        AppendFragment(plain, new StringBuilder());
        return plain.ToString();
    }

    internal override void AppendFragment(StringBuilder plain, StringBuilder rtf)
        => RtfDocument.AppendFormatted(Inlines, plain, rtf, "\\ul ");
}

/// <summary>A hyperlink: formatted inline nodes plus a target address.</summary>
/// <remarks>Rendering keeps the visible text and the address; the WinForms hosts show the text underlined.</remarks>
public class Hyperlink : Inline
{
    /// <summary>The target address; empty means the link has no destination.</summary>
    public string NavigateUri { get; set; } = string.Empty;

    /// <summary>The visible inline nodes.</summary>
    public IList<Inline> Inlines { get; } = new Collection<Inline>();

    /// <summary>The collection the XAML compiler appends direct child elements to; an alias for <see cref="Inlines"/>.</summary>
    public IList<Inline> Items => Inlines;

    /// <inheritdoc />
    public override string ToPlainText()
    {
        var plain = new StringBuilder();
        AppendFragment(plain, new StringBuilder());
        return plain.ToString();
    }

    internal override void AppendFragment(StringBuilder plain, StringBuilder rtf)
    {
        var text = new StringBuilder();
        var body = new StringBuilder();
        foreach (var inline in Inlines)
        {
            inline?.AppendFragment(text, body);
        }

        plain.Append(text);

        var uri = RtfDocument.Escape(NavigateUri ?? string.Empty);
        rtf.Append("{\\field{\\*\\fldinst{HYPERLINK \"").Append(uri).Append("\"}}{\\fldrslt{\\ul ").Append(body).Append("}}}");
    }
}

/// <summary>A hard line break inside a paragraph.</summary>
public class LineBreak : Inline
{
    /// <inheritdoc />
    public override string ToPlainText() => "\n";

    internal override void AppendFragment(StringBuilder plain, StringBuilder rtf)
    {
        plain.Append('\n');
        rtf.Append("\\line ");
    }
}

// 富文本两套输出共享的 RTF 细节：转义、字体表与可选的前景色表。
internal static class RtfDocument
{
    public static string Wrap(string body, Color foreground)
    {
        var builder = new StringBuilder();
        builder.Append("{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0\\fnil\\fcharset0 Segoe UI;}}");

        var useColor = foreground.A > 0;
        if (useColor)
        {
            builder.Append("{\\colortbl ;\\red")
                .Append(foreground.R)
                .Append("\\green")
                .Append(foreground.G)
                .Append("\\blue")
                .Append(foreground.B)
                .Append(";}");
        }

        builder.Append("\\viewkind4\\uc1\\pard\\f0\\fs20");
        if (useColor)
        {
            builder.Append("\\cf1");
        }

        builder.Append(' ').Append(body).Append('}');
        return builder.ToString();
    }

    public static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '{':
                    builder.Append("\\{");
                    break;
                case '}':
                    builder.Append("\\}");
                    break;
                case '\r':
                    break;
                case '\n':
                    builder.Append("\\line ");
                    break;
                case '\t':
                    builder.Append("\\tab ");
                    break;
                default:
                    if (character < 128)
                    {
                        builder.Append(character);
                    }
                    else
                    {
                        // RTF 的 \uN 是带符号 16 位；末尾的 ? 是非 Unicode 阅读器看到的占位字符。
                        builder.Append("\\u").Append((short)character).Append('?');
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    public static void AppendFormatted(IList<Inline> inlines, StringBuilder plain, StringBuilder rtf, string start)
    {
        var group = new StringBuilder();
        foreach (var inline in inlines)
        {
            inline?.AppendFragment(plain, group);
        }

        rtf.Append('{').Append(start).Append(group).Append('}');
    }
}
