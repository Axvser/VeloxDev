using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using XamlDrawing = VeloxDev.WinForms.Xaml.Controls.XamlDrawing;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>The shared activation, focus and disabled behavior of the self-drawn toggle controls.</summary>
/// <remarks>
/// A derived control decides what its checked state means and paints its own indicator. A left pointer release and
/// <c>Space</c>/<c>Enter</c> activate it; <c>Enter</c> activates on key down and <c>Space</c> on key up, matching
/// <see cref="Button"/>.
/// </remarks>
public abstract class ToggleButtonBase : ButtonBase
{
    private bool _keyDown;

    /// <summary>Creates a focusable toggle with theme metrics.</summary>
    protected ToggleButtonBase()
    {
        Focusable = true;
        IsHitTestVisible = true;
        Cursor = Cursors.Hand;
        MinWidth = Theme.Current.ControlHeight;
        MinHeight = Theme.Current.ControlHeight;
    }

    /// <summary>Raised when the control changes from a non-checked state to checked.</summary>
    public event EventHandler? Checked;

    /// <summary>Whether the pointer is currently over this control.</summary>
    protected bool IsHovered => IsPointerOver;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        return IsTemplated
            ? MeasureTemplate(availableSize)
            : new Size(Theme.Current.ControlHeight, Theme.Current.ControlHeight);
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        if (IsTemplated || Bounds.Width <= 0f || Bounds.Height <= 0f)
        {
            return;
        }

        var bounds = new RectangleF(0f, 0f, Bounds.Width, Bounds.Height);
        RenderGlyph(graphics, bounds);
        if (IsFocusVisible)
        {
            DrawFocusRing(
                graphics,
                new RectangleF(2f, 2f, Math.Max(0f, bounds.Width - 4f), Math.Max(0f, bounds.Height - 4f)),
                4f);
        }
    }

    /// <summary>Paints the control's indicator inside <paramref name="bounds"/>, in local coordinates.</summary>
    /// <param name="graphics">The target surface.</param>
    /// <param name="bounds">The control's local rectangle.</param>
    protected abstract void RenderGlyph(Graphics graphics, RectangleF bounds);

    /// <summary>Applies the state change requested by a pointer or keyboard activation.</summary>
    protected abstract void ActivateFromInput();

    /// <summary>Raises <see cref="Checked"/>.</summary>
    protected void RaiseChecked() => Checked?.Invoke(this, EventArgs.Empty);

    // 先翻转状态再执行命令：命令读 IsChecked 时看到的是本次激活后的值，与 WPF 的 ToggleButton 一致。
    private void Activate()
    {
        if (!IsEnabled)
        {
            return;
        }

        ActivateFromInput();
        ExecuteCommand();
    }

    /// <summary>Draws the focus ring around <paramref name="bounds"/>.</summary>
    /// <param name="graphics">The target surface.</param>
    /// <param name="bounds">The rectangle to outline, in local coordinates.</param>
    /// <param name="radius">The corner radius.</param>
    protected void DrawFocusRing(Graphics graphics, RectangleF bounds, float radius)
    {
        using var path = XamlDrawing.RoundedRectangle(bounds, radius);
        using var pen = new Pen(Theme.Current.FocusRing, 1.6f);
        graphics.DrawPath(pen, path);
    }

    /// <inheritdoc />
    protected override void OnPointerEntered(VisualPointerEventArgs args)
    {
        SetIsPointerOver(true);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerExited(VisualPointerEventArgs args)
    {
        SetIsPointerOver(false);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        if (!IsEnabled || args.Button != MouseButtons.Left)
        {
            return;
        }

        SetIsPressed(true);
        Focus();
        InvalidateVisual();
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(VisualPointerEventArgs args)
    {
        if (!IsPressed)
        {
            return;
        }

        SetIsPressed(false);
        InvalidateVisual();
        args.Handled = true;
        if (IsEnabled && AbsoluteBounds.Contains(args.Position))
        {
            Activate();
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(VisualKeyEventArgs args)
    {
        if (!IsEnabled || _keyDown)
        {
            return;
        }

        if (args.KeyCode is Keys.Space or Keys.Enter)
        {
            _keyDown = true;
            if (args.KeyCode == Keys.Enter)
            {
                Activate();
            }

            args.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyUp(VisualKeyEventArgs args)
    {
        if (args.KeyCode is Keys.Space or Keys.Enter)
        {
            var activate = args.KeyCode == Keys.Space && _keyDown && IsEnabled;
            _keyDown = false;
            args.Handled = true;
            if (activate)
            {
                Activate();
            }
        }
    }

    /// <inheritdoc />
    protected override void OnGotFocus() => InvalidateVisual();

    /// <inheritdoc />
    protected override void OnLostFocus() => InvalidateVisual();
}

/// <summary>A self-drawn on/off switch with a sliding knob.</summary>
public class ToggleButton : ToggleButtonBase
{
    /// <summary>Whether the switch is on.</summary>
    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register(
            nameof(IsChecked), typeof(bool), typeof(ToggleButton),
            new PropertyMetadata(false) { AffectsRender = true });

    /// <summary>Creates an unchecked switch.</summary>
    public ToggleButton()
    {
    }

    /// <summary>Whether the switch is on.</summary>
    public bool IsChecked
    {
        get => (bool)GetValue(IsCheckedProperty)!;
        set => SetValue(IsCheckedProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        return IsTemplated
            ? MeasureTemplate(availableSize)
            : new Size(Theme.Current.ControlHeight * 2, Theme.Current.ControlHeight);
    }

    /// <inheritdoc />
    protected override void ActivateFromInput() => IsChecked = !IsChecked;

    /// <inheritdoc />
    protected override void RenderGlyph(Graphics graphics, RectangleF bounds)
    {
        var theme = Theme.Current;
        var height = Math.Max(14f, Math.Min(bounds.Height - 8f, 24f));
        var width = Math.Max(height * 1.7f, Math.Min(bounds.Width - 6f, height * 2.2f));
        var track = new RectangleF(
            (bounds.Width - width) / 2f,
            (bounds.Height - height) / 2f,
            width,
            height);

        var background = !IsEnabled
            ? theme.DisabledBackground
            : IsChecked
                ? theme.Accent
                : IsPressed
                    ? theme.ControlPressed
                    : IsHovered
                        ? theme.ControlHover
                        : theme.ControlBackground;
        var border = !IsEnabled
            ? theme.DisabledForeground
            : IsChecked
                ? theme.Accent
                : IsHovered || IsFocused
                    ? theme.FocusRing
                    : theme.ControlBorder;
        XamlDrawing.PaintChrome(graphics, Rectangle.Round(track), background, border, new XamlThickness(1f), height / 2f);

        // 轨道是像素对齐后画出来的，圆钮也必须以同一条对齐后的轨道为基准，否则半像素会让它看起来整体偏下。
        var paintedTrack = Rectangle.Round(track);
        var knobRadius = Math.Max(3f, (height / 2f) - 3f);
        var knobMargin = Math.Max(3f, (height / 2f) - knobRadius);
        var knobCenter = new PointF(
            IsChecked ? paintedTrack.Right - knobMargin - knobRadius : paintedTrack.Left + knobMargin + knobRadius,
            paintedTrack.Top + (paintedTrack.Height / 2f));
        var knobColor = !IsEnabled
            ? theme.DisabledForeground
            : IsChecked
                ? theme.AccentForeground
                : theme.MutedForeground;
        using var knobBrush = new SolidBrush(knobColor);
        graphics.FillEllipse(
            knobBrush,
            knobCenter.X - knobRadius,
            knobCenter.Y - knobRadius,
            knobRadius * 2f,
            knobRadius * 2f);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, IsCheckedProperty) && e.OldValue is not true && e.NewValue is true)
        {
            RaiseChecked();
        }
    }
}

/// <summary>A self-drawn check box with checked, unchecked and indeterminate states.</summary>
public class CheckBox : ToggleButtonBase
{
    /// <summary>The checked state; <see langword="null"/> draws the indeterminate dash.</summary>
    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register(
            nameof(IsChecked), typeof(bool?), typeof(CheckBox),
            new PropertyMetadata(false) { AffectsRender = true });

    /// <summary>Creates an unchecked check box.</summary>
    public CheckBox()
    {
    }

    /// <summary>The checked state; <see langword="null"/> draws the indeterminate dash.</summary>
    public bool? IsChecked
    {
        get => (bool?)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <inheritdoc />
    protected override void ActivateFromInput() => IsChecked = IsChecked != true;

    /// <inheritdoc />
    protected override void RenderGlyph(Graphics graphics, RectangleF bounds)
    {
        var theme = Theme.Current;
        var size = Math.Max(10f, Math.Min(Math.Min(bounds.Width, bounds.Height) - 6f, 20f));
        var box = new RectangleF(
            (bounds.Width - size) / 2f,
            (bounds.Height - size) / 2f,
            size,
            size);

        var background = !IsEnabled
            ? theme.DisabledBackground
            : IsChecked == true
                ? theme.Accent
                : IsPressed
                    ? theme.ControlPressed
                    : IsHovered
                        ? theme.ControlHover
                        : theme.ControlBackground;
        var border = !IsEnabled
            ? theme.DisabledForeground
            : IsChecked == true
                ? theme.Accent
                : IsHovered || IsFocused
                    ? theme.FocusRing
                    : theme.ControlBorder;
        XamlDrawing.PaintChrome(graphics, Rectangle.Round(box), background, border, new XamlThickness(1f), 3f);

        var markColor = !IsEnabled
            ? theme.DisabledForeground
            : IsChecked == true
                ? theme.AccentForeground
                : theme.Foreground;
        if (IsChecked == true)
        {
            using var pen = new Pen(markColor, Math.Max(1.6f, size * 0.12f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };
            graphics.DrawLines(
                pen,
                new[]
                {
                    new PointF(box.Left + (size * 0.24f), box.Top + (size * 0.52f)),
                    new PointF(box.Left + (size * 0.43f), box.Top + (size * 0.72f)),
                    new PointF(box.Left + (size * 0.77f), box.Top + (size * 0.31f)),
                });
        }
        else if (IsChecked is null)
        {
            using var pen = new Pen(markColor, Math.Max(1.6f, size * 0.12f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            graphics.DrawLine(
                pen,
                box.Left + (size * 0.25f),
                box.Top + (size * 0.5f),
                box.Left + (size * 0.75f),
                box.Top + (size * 0.5f));
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, IsCheckedProperty) && e.OldValue is not true && e.NewValue is true)
        {
            RaiseChecked();
        }
    }
}

/// <summary>A self-drawn radio button that is mutually exclusive with the same-named radios in its parent subtree.</summary>
public class RadioButton : ToggleButtonBase
{
    /// <summary>The checked state; setting it to <see langword="true"/> clears the grouped siblings.</summary>
    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register(
            nameof(IsChecked), typeof(bool?), typeof(RadioButton),
            new PropertyMetadata(false) { AffectsRender = true });

    /// <summary>The group name shared by mutually exclusive radio buttons.</summary>
    public static readonly DependencyProperty GroupNameProperty =
        DependencyProperty.Register(
            nameof(GroupName), typeof(string), typeof(RadioButton),
            new PropertyMetadata(string.Empty) { AffectsRender = false, AffectsMeasure = false });

    /// <summary>Creates an unchecked radio button.</summary>
    public RadioButton()
    {
    }

    /// <summary>The checked state; setting it to <see langword="true"/> clears the grouped siblings.</summary>
    public bool? IsChecked
    {
        get => (bool?)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>The group name shared by mutually exclusive radio buttons.</summary>
    public string GroupName
    {
        get => (string)GetValue(GroupNameProperty)!;
        set => SetValue(GroupNameProperty, value ?? string.Empty);
    }

    /// <inheritdoc />
    protected override void ActivateFromInput() => IsChecked = true;

    /// <inheritdoc />
    protected override void RenderGlyph(Graphics graphics, RectangleF bounds)
    {
        var theme = Theme.Current;
        var size = Math.Max(10f, Math.Min(Math.Min(bounds.Width, bounds.Height) - 6f, 20f));
        var circle = new RectangleF(
            (bounds.Width - size) / 2f,
            (bounds.Height - size) / 2f,
            size,
            size);

        var background = !IsEnabled
            ? theme.DisabledBackground
            : IsPressed
                ? theme.ControlPressed
                : IsHovered
                    ? theme.ControlHover
                    : theme.ControlBackground;
        var border = !IsEnabled
            ? theme.DisabledForeground
            : IsChecked == true
                ? theme.Accent
                : IsHovered || IsFocused
                    ? theme.FocusRing
                    : theme.ControlBorder;
        using (var fill = new SolidBrush(background.A > 0 ? background : Color.Transparent))
        {
            graphics.FillEllipse(fill, circle);
        }

        using (var pen = new Pen(border, IsChecked == true ? 2f : 1.4f))
        {
            graphics.DrawEllipse(pen, circle);
        }

        if (IsChecked == true)
        {
            var dotRadius = Math.Max(2f, (size / 2f) - 4f);
            using var dot = new SolidBrush(IsEnabled ? theme.Accent : theme.DisabledForeground);
            graphics.FillEllipse(
                dot,
                circle.X + (size / 2f) - dotRadius,
                circle.Y + (size / 2f) - dotRadius,
                dotRadius * 2f,
                dotRadius * 2f);
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!ReferenceEquals(e.Property, IsCheckedProperty) || e.NewValue is not true)
        {
            return;
        }

        UncheckGroupedSiblings();
        RaiseChecked();
    }

    // 只清理「同一个父级子树」里的同名单选：跨父级的两个分组不应互相影响。
    private void UncheckGroupedSiblings()
    {
        var parent = ParentElement;
        if (parent is null)
        {
            return;
        }

        var name = GroupName;
        foreach (var sibling in EnumerateVisuals(parent))
        {
            if (ReferenceEquals(sibling, this) || sibling is not RadioButton radio)
            {
                continue;
            }

            if (!string.Equals(radio.GroupName, name, StringComparison.Ordinal))
            {
                continue;
            }

            if (name.Length == 0 && !ReferenceEquals(radio.ParentElement, parent))
            {
                continue;
            }

            radio.IsChecked = false;
        }
    }

    private static IEnumerable<VisualElement> EnumerateVisuals(VisualElement root)
    {
        foreach (var child in root.Children)
        {
            if (child is not VisualElement element)
            {
                continue;
            }

            yield return element;
            foreach (var descendant in EnumerateVisuals(element))
            {
                yield return descendant;
            }
        }
    }
}
