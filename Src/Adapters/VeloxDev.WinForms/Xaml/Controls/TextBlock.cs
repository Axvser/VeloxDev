using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>How text is aligned inside a text control.</summary>
public enum TextAlignment
{
    /// <summary>Align to the left edge.</summary>
    Left = 0,

    /// <summary>Align to the right edge.</summary>
    Right = 1,

    /// <summary>Centre the text.</summary>
    Center = 2,
}

/// <summary>How a text control wraps lines that do not fit.</summary>
public enum TextWrapping
{
    /// <summary>Never wrap; overflow is clipped or trimmed.</summary>
    NoWrap = 0,

    /// <summary>Wrap at word boundaries.</summary>
    Wrap = 1,

    /// <summary>Wrap at word boundaries but leave overflowing words on one line.</summary>
    WrapWithOverflow = 2,
}

/// <summary>How a text control trims text that does not fit.</summary>
public enum TextTrimming
{
    /// <summary>Do not trim.</summary>
    None = 0,

    /// <summary>Trim at a character boundary and draw an ellipsis.</summary>
    CharacterEllipsis = 1,

    /// <summary>Trim at a word boundary and draw an ellipsis.</summary>
    WordEllipsis = 2,
}

/// <summary>
/// Draws a block of text with dependency-property colour, font and alignment.
/// </summary>
/// <remarks>
/// This is the XAML-layer counterpart of the native <see cref="System.Windows.Forms.Label"/>: the text, colour, font,
/// alignment, wrapping and trimming are dependency properties, so styles, triggers, bindings and transitions can drive
/// them. It never paints a native label child.
/// </remarks>
public class TextBlock : Control
{
    /// <summary>The text to draw; empty by default.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(TextBlock),
        new PropertyMetadata(string.Empty, OnTextPropertyChanged));

    /// <summary>How the text is aligned horizontally inside the content rectangle.</summary>
    public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register(
        nameof(TextAlignment),
        typeof(TextAlignment),
        typeof(TextBlock),
        new PropertyMetadata(TextAlignment.Left, OnTextPropertyChanged));

    /// <summary>How overlong lines wrap.</summary>
    public static readonly DependencyProperty TextWrappingProperty = DependencyProperty.Register(
        nameof(TextWrapping),
        typeof(TextWrapping),
        typeof(TextBlock),
        new PropertyMetadata(TextWrapping.NoWrap, OnTextPropertyChanged));

    /// <summary>How overlong text is trimmed.</summary>
    public static readonly DependencyProperty TextTrimmingProperty = DependencyProperty.Register(
        nameof(TextTrimming),
        typeof(TextTrimming),
        typeof(TextBlock),
        new PropertyMetadata(TextTrimming.None, OnTextPropertyChanged));

    /// <summary>Gets or sets the text to draw.</summary>
    public new string Text
    {
        get => (string?)GetValue(TextProperty) ?? string.Empty;
        set => SetValue(TextProperty, value);
    }

    /// <summary>Gets or sets the horizontal text alignment.</summary>
    public TextAlignment TextAlignment
    {
        get => (TextAlignment)GetValue(TextAlignmentProperty)!;
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <summary>Gets or sets how overlong lines wrap.</summary>
    public TextWrapping TextWrapping
    {
        get => (TextWrapping)GetValue(TextWrappingProperty)!;
        set => SetValue(TextWrappingProperty, value);
    }

    /// <summary>Gets or sets how overlong text is trimmed.</summary>
    public TextTrimming TextTrimming
    {
        get => (TextTrimming)GetValue(TextTrimmingProperty)!;
        set => SetValue(TextTrimmingProperty, value);
    }

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        if (string.IsNullOrEmpty(Text))
        {
            return new Size(
                (int)Math.Ceiling(Padding.Horizontal),
                (int)Math.Ceiling(Padding.Vertical));
        }

        var width = proposedSize.Width > 0
            ? Math.Max(0, proposedSize.Width - (int)Math.Ceiling(Padding.Horizontal))
            : 0;
        var measured = TextRenderer.MeasureText(
            Text,
            Font,
            new Size(width <= 0 ? int.MaxValue : width, int.MaxValue),
            BuildFlags() | TextFormatFlags.WordBreak);
        return new Size(
            measured.Width + (int)Math.Ceiling(Padding.Horizontal),
            measured.Height + (int)Math.Ceiling(Padding.Vertical));
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!IsPainted || string.IsNullOrEmpty(Text))
        {
            return;
        }

        var bounds = XamlLayout.ContentBounds(this);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, ForeColor, BuildFlags());
    }

    private TextFormatFlags BuildFlags()
    {
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping;
        flags |= TextAlignment switch
        {
            TextAlignment.Right => TextFormatFlags.Right,
            TextAlignment.Center => TextFormatFlags.HorizontalCenter,
            _ => TextFormatFlags.Left,
        };

        if (TextWrapping is TextWrapping.Wrap or TextWrapping.WrapWithOverflow)
        {
            flags |= TextFormatFlags.WordBreak;
        }
        else
        {
            flags |= TextFormatFlags.SingleLine;
        }

        flags |= TextTrimming switch
        {
            TextTrimming.CharacterEllipsis => TextFormatFlags.EndEllipsis,
            TextTrimming.WordEllipsis => TextFormatFlags.WordEllipsis,
            _ => TextFormatFlags.NoClipping,
        };

        return flags;
    }

    private static void OnTextPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control && !control.IsDisposed)
        {
            control.Invalidate();
            control.Parent?.PerformLayout();
        }
    }
}

/// <summary>
/// A <see cref="TextBlock"/> that also accepts WPF/Avalonia-style <see cref="Content"/>.
/// </summary>
/// <remarks>
/// <see cref="Content"/> is a convenience alias for <see cref="TextBlock.Text"/>: assigning an object writes its
/// <see cref="object.ToString"/> result. Use <see cref="TextBlock.Text"/> when the value is already a string.
/// </remarks>
public class Label : TextBlock
{
    /// <summary>The label content; it is the same dependency property as <see cref="TextBlock.Text"/>.</summary>
    /// <remarks>Declaring it as a separate property would let a style's Content setter leave a local Text value behind when the style is removed.</remarks>
    public static readonly DependencyProperty ContentProperty = TextProperty;

    /// <summary>Gets or sets the label content.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? Content
    {
        get => Text;
        set => Text = value?.ToString() ?? string.Empty;
    }
}
