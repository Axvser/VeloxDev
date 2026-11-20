using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A button that keeps a checked state, like a WPF or Avalonia toggle button.
/// </summary>
/// <remarks>
/// Clicking toggles <see cref="IsChecked"/> and raises <see cref="Checked"/> or <see cref="Unchecked"/>. With
/// <see cref="IsThreeState"/> enabled the cycle is false, true, null. The checked state is a dependency property, so a
/// style trigger can restyle the button without code.
/// </remarks>
public class ToggleButton : Button
{
    /// <summary>Whether the toggle is checked; null is the indeterminate state.</summary>
    public static readonly DependencyProperty IsCheckedProperty = DependencyProperty.Register(
        nameof(IsChecked),
        typeof(bool?),
        typeof(ToggleButton),
        new PropertyMetadata(false, OnIsCheckedChanged));

    /// <summary>Whether the checked state cycles through null.</summary>
    public static readonly DependencyProperty IsThreeStateProperty = DependencyProperty.Register(
        nameof(IsThreeState),
        typeof(bool),
        typeof(ToggleButton),
        new PropertyMetadata(false));

    /// <summary>Gets or sets whether the toggle is checked; null is the indeterminate state.</summary>
    public bool? IsChecked
    {
        get => (bool?)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>Gets or sets whether the checked state cycles through null.</summary>
    public bool IsThreeState
    {
        get => (bool)GetValue(IsThreeStateProperty)!;
        set => SetValue(IsThreeStateProperty, value);
    }

    /// <summary>Raised when the toggle becomes checked.</summary>
    public event EventHandler? Checked;

    /// <summary>Raised when the toggle becomes unchecked.</summary>
    public event EventHandler? Unchecked;

    /// <summary>Raised when the toggle becomes indeterminate.</summary>
    public event EventHandler? Indeterminate;

    /// <inheritdoc />
    protected override void OnClick()
    {
        Toggle();
        base.OnClick();
    }

    /// <summary>Advances <see cref="IsChecked"/> to its next state.</summary>
    protected virtual void Toggle()
    {
        IsChecked = IsThreeState
            ? IsChecked switch
            {
                false => true,
                true => null,
                _ => false,
            }
            : IsChecked != true;
    }

    private static void OnIsCheckedChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not ToggleButton button || button.IsDisposed)
        {
            return;
        }

        button.Invalidate();
        switch (e.NewValue)
        {
            case true:
                button.Checked?.Invoke(button, EventArgs.Empty);
                break;
            case false:
                button.Unchecked?.Invoke(button, EventArgs.Empty);
                break;
            default:
                button.Indeterminate?.Invoke(button, EventArgs.Empty);
                break;
        }
    }
}

/// <summary>
/// A toggle button drawn as a check box with its content beside it.
/// </summary>
/// <remarks>The box and the content both use the inherited dependency properties; the check glyph is drawn by this control.</remarks>
public class CheckBox : ToggleButton
{
    private const int GlyphSize = 16;
    private const int GlyphGap = 6;

    /// <summary>Creates the check box with its content on the left.</summary>
    public CheckBox()
    {
    }

    /// <inheritdoc />
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // 没有作者设置背景时保持透明；样式写了 Background 才让基类画。
        if (HasXamlValue(BackColorProperty))
        {
            base.OnPaintBackground(e);
        }
    }

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var content = base.GetPreferredSize(proposedSize);
        return new Size(
            content.Width + GlyphSize + GlyphGap,
            Math.Max(content.Height, GlyphSize));
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        var bounds = XamlLayout.ContentBounds(this);
        var top = bounds.Top + Math.Max(0, (bounds.Height - GlyphSize) / 2);
        var box = new Rectangle(bounds.Left, top, GlyphSize, GlyphSize);
        var boxPath = XamlDrawing.RoundedRectangle(new RectangleF(box.X + 0.5f, box.Y + 0.5f, box.Width - 1f, box.Height - 1f), 3f);

        using (boxPath)
        {
            using var fill = new SolidBrush(IsChecked == true ? ForeColor : SystemColors.Window);
            e.Graphics.FillPath(fill, boxPath);
            using var pen = new Pen(Color.FromArgb(0x99, ForeColor), 1f);
            e.Graphics.DrawPath(pen, boxPath);
        }

        if (IsChecked == true)
        {
            using var checkPen = new Pen(SystemColors.Window, 1.8f);
            e.Graphics.DrawLines(checkPen,
            [
                new Point(box.Left + 4, box.Top + 8),
                new Point(box.Left + 7, box.Top + 11),
                new Point(box.Left + 12, box.Top + 5),
            ]);
        }
        else if (IsChecked is null)
        {
            using var dashPen = new Pen(SystemColors.Window, 1.8f);
            e.Graphics.DrawLine(dashPen, box.Left + 4, box.Top + 8, box.Right - 4, box.Top + 8);
        }

        var text = Content?.ToString();
        if (!string.IsNullOrEmpty(text))
        {
            var textBounds = new Rectangle(
                box.Right + GlyphGap,
                bounds.Top,
                Math.Max(0, bounds.Width - GlyphSize - GlyphGap),
                bounds.Height);
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            flags |= HorizontalContentAlignment switch
            {
                HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
                HorizontalAlignment.Right => TextFormatFlags.Right,
                _ => TextFormatFlags.Left,
            };
            TextRenderer.DrawText(
                e.Graphics,
                text,
                Font,
                textBounds,
                IsEnabled ? ForeColor : SystemColors.GrayText,
                flags);
        }
    }
}

/// <summary>
/// A toggle button drawn as a radio button with its content beside it.
/// </summary>
/// <remarks>
/// Checking one radio button unchecks the other radio buttons in the same parent that share <see cref="GroupName"/>;
/// an empty group name means every radio button in the parent. A radio button does not toggle itself off on click.
/// </remarks>
public class RadioButton : ToggleButton
{
    /// <summary>The group this radio button belongs to; an empty name means its parent.</summary>
    public static readonly DependencyProperty GroupNameProperty = DependencyProperty.Register(
        nameof(GroupName),
        typeof(string),
        typeof(RadioButton),
        new PropertyMetadata(string.Empty));

    private const int GlyphSize = 16;
    private const int GlyphGap = 6;

    /// <summary>Creates the radio button with its content on the left.</summary>
    public RadioButton()
    {
    }

    /// <inheritdoc />
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // 没有作者设置背景时保持透明；样式写了 Background 才让基类画。
        if (HasXamlValue(BackColorProperty))
        {
            base.OnPaintBackground(e);
        }
    }

    /// <summary>Gets or sets the group this radio button belongs to.</summary>
    public string GroupName
    {
        get => (string?)GetValue(GroupNameProperty) ?? string.Empty;
        set => SetValue(GroupNameProperty, value);
    }

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var content = base.GetPreferredSize(proposedSize);
        return new Size(
            content.Width + GlyphSize + GlyphGap,
            Math.Max(content.Height, GlyphSize));
    }

    /// <inheritdoc />
    protected override void OnClick()
    {
        if (IsChecked != true)
        {
            UncheckSiblings();
            IsChecked = true;
        }

        RaiseClick();
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        var bounds = XamlLayout.ContentBounds(this);
        var top = bounds.Top + Math.Max(0, (bounds.Height - GlyphSize) / 2);
        var circle = new Rectangle(bounds.Left, top, GlyphSize, GlyphSize);

        using (var fill = new SolidBrush(SystemColors.Window))
        using (var border = new Pen(Color.FromArgb(0x99, ForeColor), 1f))
        {
            e.Graphics.FillEllipse(fill, circle);
            e.Graphics.DrawEllipse(border, circle);
        }

        if (IsChecked == true)
        {
            var dot = Rectangle.Inflate(circle, -5, -5);
            using var fill = new SolidBrush(ForeColor);
            e.Graphics.FillEllipse(fill, dot);
        }

        var text = Content?.ToString();
        if (!string.IsNullOrEmpty(text))
        {
            var textBounds = new Rectangle(
                circle.Right + GlyphGap,
                bounds.Top,
                Math.Max(0, bounds.Width - GlyphSize - GlyphGap),
                bounds.Height);
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            flags |= HorizontalContentAlignment switch
            {
                HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
                HorizontalAlignment.Right => TextFormatFlags.Right,
                _ => TextFormatFlags.Left,
            };
            TextRenderer.DrawText(
                e.Graphics,
                text,
                Font,
                textBounds,
                IsEnabled ? ForeColor : SystemColors.GrayText,
                flags);
        }
    }

    private void UncheckSiblings()
    {
        if (Parent is not { } parent)
        {
            return;
        }

        foreach (System.Windows.Forms.Control sibling in parent.Controls)
        {
            if (ReferenceEquals(sibling, this) || sibling is not RadioButton other)
            {
                continue;
            }

            if (string.Equals(other.GroupName, GroupName, StringComparison.Ordinal))
            {
                other.IsChecked = false;
            }
        }
    }
}
