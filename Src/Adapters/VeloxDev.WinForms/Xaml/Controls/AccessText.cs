using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A <see cref="TextBlock"/> that interprets <c>_</c> as the access-key marker and underlines the following character.
/// </summary>
/// <remarks>
/// <para>
/// A single underscore is removed from the displayed text and underlines the character after it; a doubled underscore
/// renders one literal underscore. The type has no command target: it only draws the mnemonic, which keeps it useful
/// in templates that do not run WPF's access-key routing.
/// </para>
/// <para>
/// The underline is positioned for a single line. Wrapped text still removes the marker and draws the underline from
/// the measured prefix, but multi-line glyph positions are not tracked.
/// </para>
/// </remarks>
public class AccessText : TextBlock
{
    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var display = GetDisplayText(Text, out _);
        if (display.Length == 0)
        {
            return new Size(
                (int)Math.Ceiling(Padding.Horizontal),
                (int)Math.Ceiling(Padding.Vertical));
        }

        var width = proposedSize.Width > 0
            ? Math.Max(0, proposedSize.Width - (int)Math.Ceiling(Padding.Horizontal))
            : 0;
        var measured = TextRenderer.MeasureText(
            display,
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
        if (!IsPainted || string.IsNullOrEmpty(Text))
        {
            return;
        }

        var display = GetDisplayText(Text, out var underlineIndex);
        if (display.Length == 0)
        {
            return;
        }

        var bounds = XamlLayout.ContentBounds(this);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        TextRenderer.DrawText(e.Graphics, display, Font, bounds, EffectiveForeground, BuildFlags());
        if (underlineIndex >= 0 && underlineIndex < display.Length)
        {
            DrawUnderline(e.Graphics, display, underlineIndex, bounds);
        }
    }

    private TextFormatFlags BuildFlags()
    {
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping;
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

    private void DrawUnderline(Graphics graphics, string display, int underlineIndex, Rectangle bounds)
    {
        const TextFormatFlags measureFlags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        var full = TextRenderer.MeasureText(display, Font, new Size(int.MaxValue, int.MaxValue), measureFlags);
        var prefix = TextRenderer.MeasureText(display.Substring(0, underlineIndex), Font, new Size(int.MaxValue, int.MaxValue), measureFlags);
        var glyph = TextRenderer.MeasureText(display.Substring(underlineIndex, 1), Font, new Size(int.MaxValue, int.MaxValue), measureFlags);

        var startX = TextAlignment switch
        {
            TextAlignment.Right => bounds.Right - full.Width,
            TextAlignment.Center => bounds.Left + (bounds.Width - full.Width) / 2,
            _ => bounds.Left,
        };

        var x = startX + prefix.Width;
        var width = Math.Max(1, glyph.Width);
        var y = Math.Min(bounds.Bottom - 1, bounds.Top + full.Height - 1);
        if (y < bounds.Top || width <= 0)
        {
            return;
        }

        x = Math.Max(bounds.Left, Math.Min(x, bounds.Right - 1));
        using var pen = new Pen(EffectiveForeground, 1f);
        graphics.DrawLine(pen, x, y, Math.Min(bounds.Right, x + width), y);
    }

    // 解析访问键标记：`__` 是一个字面下划线，`_x` 去掉下划线并记住 x 在显示文本里的位置。
    private static string GetDisplayText(string? source, out int underlineIndex)
    {
        underlineIndex = -1;
        if (string.IsNullOrEmpty(source))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(source.Length);
        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (character == '_' && index + 1 < source.Length)
            {
                if (source[index + 1] == '_')
                {
                    builder.Append('_');
                    index++;
                    continue;
                }

                underlineIndex = builder.Length;
                builder.Append(source[index + 1]);
                index++;
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
