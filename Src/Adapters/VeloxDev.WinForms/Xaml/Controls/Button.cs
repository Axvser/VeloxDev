using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Windows.Input;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A self-drawn button whose chrome, colours and pressed state are dependency properties.
/// </summary>
/// <remarks>
/// <para>
/// The button paints a rounded background and border itself; the content is the inherited
/// <see cref="ContentControl.Content"/> and is either a child control or text. <see cref="IsPointerOver"/> and
/// <see cref="Control.IsKeyboardFocused"/> are dependency properties, so a style or trigger can restyle the button
/// for hover and focus without code.
/// </para>
/// <para>
/// <see cref="Click"/> is raised from a left-button press/release pair or from Space/Enter while focused. The button
/// captures the pointer on press and releases it through the base control, so a drag off the button cancels the click.
/// </para>
/// <para>
/// The resting state paints <see cref="Control.Background"/> as a brush, so a gradient survives. Hover and pressed
/// states shift its <see cref="Brush.ApproximateColor"/>, so a gradient there becomes a solid of its first stop.
/// </para>
/// </remarks>
public class Button : ContentControl
{
    /// <summary>The border stroke brush; a translucent black by default.</summary>
    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush),
        typeof(Brush),
        typeof(Button),
        new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), OnVisualPropertyChanged));

    /// <summary>The border stroke width per side, in pixels; one pixel by default.</summary>
    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
        nameof(BorderThickness),
        typeof(Thickness),
        typeof(Button),
        new PropertyMetadata(new Thickness(1f), OnBorderThicknessChanged));

    /// <summary>The corner radius in pixels; four by default.</summary>
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius),
        typeof(float),
        typeof(Button),
        new PropertyMetadata(4f, OnVisualPropertyChanged));

    /// <summary>Whether the button is currently held down.</summary>
    public static readonly DependencyProperty IsPressedProperty = DependencyProperty.Register(
        nameof(IsPressed),
        typeof(bool),
        typeof(Button),
        new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>The command executed when the button is clicked.</summary>
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command),
        typeof(ICommand),
        typeof(Button),
        new PropertyMetadata(null, OnCommandPropertyChanged) { AffectsRender = false });

    /// <summary>The value passed to <see cref="Command"/> when it is evaluated and executed.</summary>
    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
        nameof(CommandParameter),
        typeof(object),
        typeof(Button),
        new PropertyMetadata(null, OnCommandParameterPropertyChanged) { AffectsRender = false });

    private bool _commandDisabledByCommand;

    /// <summary>Creates the button.</summary>
    public Button()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        // WinForms 会在 WM_LBUTTONUP 之后自己调一次 OnClick；本类在 OnMouseUp 里已经按落点判定过，
        // 保留标准点击会让一次按下/松开触发两次 Click。
        SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
        Cursor = Cursors.Hand;

        // 命令挂在 Click 上而不是 OnClick：RepeatButton 的重复帧直接调 RaiseClick，也要执行命令。
        Click += OnClickExecuteCommand;
    }

    /// <summary>Gets or sets the border stroke brush.</summary>
    /// <remarks>GDI+ pens are solid, so a gradient outline is flattened to its <see cref="Brush.ApproximateColor"/>.</remarks>
    public Brush? BorderBrush
    {
        get => GetValue(BorderBrushProperty) as Brush;
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>Gets or sets the border stroke width per side, in pixels.</summary>
    public Thickness BorderThickness
    {
        get => (Thickness)GetValue(BorderThicknessProperty)!;
        set => SetValue(BorderThicknessProperty, value);
    }

    /// <summary>Gets or sets the corner radius in pixels.</summary>
    public float CornerRadius
    {
        get => (float)GetValue(CornerRadiusProperty)!;
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>Gets or sets whether the button is currently held down.</summary>
    public bool IsPressed
    {
        get => (bool)GetValue(IsPressedProperty)!;
        set => SetValue(IsPressedProperty, value);
    }

    /// <summary>Gets or sets the command executed when the button is clicked.</summary>
    /// <remarks>
    /// While a command is assigned, <see cref="ICommand.CanExecute"/> drives <see cref="Control.IsEnabled"/>: the button
    /// is disabled when the command refuses and re-enabled when it allows. An <see cref="Control.IsEnabled"/>=false set
    /// by the author is preserved until the command refuses at least once; from then on the command owns the value.
    /// </remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>Gets or sets the value passed to <see cref="Command"/> when it is evaluated and executed.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>Raised when the button is clicked.</summary>
    public new event EventHandler? Click;

    /// <summary>Raises <see cref="Click"/> as if the button had been pressed and released.</summary>
    public void PerformClick()
    {
        if (IsEnabled)
        {
            OnClick();
        }
    }

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var content = base.GetPreferredSize(proposedSize);
        var border = BorderThickness;
        return new Size(
            content.Width + (int)Math.Ceiling(border.Horizontal),
            content.Height + (int)Math.Ceiling(border.Vertical));
    }

    /// <inheritdoc />
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !IsEnabled)
        {
            return;
        }

        Focus();
        IsPressed = true;
        CapturePointer();
    }

    /// <inheritdoc />
    protected override void OnMouseUp(MouseEventArgs e)
    {
        var wasPressed = IsPressed;
        IsPressed = false;
        base.OnMouseUp(e);

        if (wasPressed && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location) && IsEnabled)
        {
            OnClick();
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEnabled || IsPressed || e.KeyCode is not (Keys.Space or Keys.Enter))
        {
            return;
        }

        IsPressed = true;
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnKeyUp(KeyEventArgs e)
    {
        var wasPressed = IsPressed;
        IsPressed = false;
        base.OnKeyUp(e);

        if (wasPressed && e.KeyCode is Keys.Space or Keys.Enter && IsEnabled)
        {
            OnClick();
        }
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        var background = IsPressed && IsPointerOver
            ? new SolidColorBrush(Darken(Background?.ApproximateColor ?? Color.Transparent, 0.12f))
            : IsPointerOver
                ? new SolidColorBrush(Lighten(Background?.ApproximateColor ?? Color.Transparent, 0.08f))
                : Background ?? new SolidColorBrush(Color.Transparent);
        var foreground = IsEnabled ? EffectiveForeground : SystemColors.GrayText;

        var bounds = ClientRectangle;
        PaintChrome(e.Graphics, bounds, background, BorderBrush, BorderThickness, CornerRadius);
        if (ContentVisual is null)
        {
            var text = Content?.ToString();
            if (!string.IsNullOrEmpty(text))
            {
                var content = XamlLayout.ContentBounds(this);
                var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
                var horizontal = HasXamlValue(HorizontalContentAlignmentProperty)
                    ? HorizontalContentAlignment
                    : HorizontalAlignment.Center;
                var vertical = HasXamlValue(VerticalContentAlignmentProperty)
                    ? VerticalContentAlignment
                    : VerticalAlignment.Center;
                flags |= horizontal switch
                {
                    HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
                    HorizontalAlignment.Right => TextFormatFlags.Right,
                    _ => TextFormatFlags.Left,
                };
                flags |= vertical switch
                {
                    VerticalAlignment.Center => TextFormatFlags.VerticalCenter,
                    VerticalAlignment.Bottom => TextFormatFlags.Bottom,
                    _ => TextFormatFlags.Top,
                };
                TextRenderer.DrawText(e.Graphics, text, Font, content, foreground, flags);
            }
        }
    }

    // 按钮的直接消费 Brush：普通态可以是渐变，悬停/按下态用近似色；边框环与 XamlDrawing 共用同一条路径。
    private void PaintChrome(
        Graphics graphics,
        Rectangle bounds,
        Brush background,
        Brush? borderBrush,
        Thickness borderThickness,
        float cornerRadius)
        => XamlDrawing.PaintChromeWithBrushes(graphics, bounds, background, borderBrush, borderThickness, cornerRadius);

    /// <summary>Raises <see cref="Click"/>.</summary>
    protected virtual void OnClick() => RaiseClick();

    /// <summary>Raises <see cref="Click"/> without changing the checked state.</summary>
    protected void RaiseClick() => Click?.Invoke(this, EventArgs.Empty);

    /// <summary>Mixes a colour towards white.</summary>
    private static Color Lighten(Color color, float amount)
        => Blend(color, Color.White, amount);

    /// <summary>Mixes a colour towards black.</summary>
    private static Color Darken(Color color, float amount)
        => Blend(color, Color.Black, amount);

    private static Color Blend(Color color, Color towards, float amount)
    {
        amount = Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            color.A,
            (int)(color.R + ((towards.R - color.R) * amount)),
            (int)(color.G + ((towards.G - color.G) * amount)),
            (int)(color.B + ((towards.B - color.B) * amount)));
    }

    private static void OnVisualPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control && !control.IsDisposed)
        {
            control.Invalidate();
        }
    }

    private static void OnBorderThicknessChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control && !control.IsDisposed)
        {
            control.Invalidate();
            control.Parent?.PerformLayout();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Click -= OnClickExecuteCommand;
            if (Command is { } command)
            {
                command.CanExecuteChanged -= OnCommandCanExecuteChanged;
            }
        }

        base.Dispose(disposing);
    }

    private static void OnCommandPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Button button || button.IsDisposed)
        {
            return;
        }

        if (e.OldValue is ICommand previous)
        {
            previous.CanExecuteChanged -= button.OnCommandCanExecuteChanged;
        }

        if (e.NewValue is ICommand current)
        {
            current.CanExecuteChanged += button.OnCommandCanExecuteChanged;
        }

        button.UpdateCommandEnabled();
    }

    private static void OnCommandParameterPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Button button && !button.IsDisposed)
        {
            button.UpdateCommandEnabled();
        }
    }

    private void OnCommandCanExecuteChanged(object? sender, EventArgs e) => UpdateCommandEnabled();

    // 只有命令自己关掉的状态由命令恢复；作者显式设置的 IsEnabled=false 不被覆盖。
    private void UpdateCommandEnabled()
    {
        var command = Command;
        if (command is not null && !command.CanExecute(CommandParameter))
        {
            _commandDisabledByCommand = true;
            IsEnabled = false;
            return;
        }

        if (_commandDisabledByCommand)
        {
            _commandDisabledByCommand = false;
            IsEnabled = true;
        }
    }

    private void OnClickExecuteCommand(object? sender, EventArgs e)
    {
        var command = Command;
        if (command is null)
        {
            return;
        }

        var parameter = CommandParameter;
        if (command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }
}

/// <summary>
/// A button that keeps clicking while it is held down, like a WPF repeat button.
/// </summary>
/// <remarks><see cref="Delay"/> is the wait before the first repeat; <see cref="Interval"/> is the gap between repeats.</remarks>
public class RepeatButton : Button
{
    /// <summary>The wait before the first repeat, in milliseconds; 500 by default.</summary>
    public static readonly DependencyProperty DelayProperty = DependencyProperty.Register(
        nameof(Delay),
        typeof(int),
        typeof(RepeatButton),
        new PropertyMetadata(500));

    /// <summary>The gap between repeats, in milliseconds; 100 by default.</summary>
    public static readonly DependencyProperty IntervalProperty = DependencyProperty.Register(
        nameof(Interval),
        typeof(int),
        typeof(RepeatButton),
        new PropertyMetadata(100));

    private readonly System.Windows.Forms.Timer repeatTimer = new();

    /// <summary>Creates the button and its repeat timer.</summary>
    public RepeatButton()
    {
        repeatTimer.Tick += OnRepeatTick;
    }

    /// <summary>Gets or sets the wait before the first repeat, in milliseconds.</summary>
    public int Delay
    {
        get => (int)GetValue(DelayProperty)!;
        set => SetValue(DelayProperty, value);
    }

    /// <summary>Gets or sets the gap between repeats, in milliseconds.</summary>
    public int Interval
    {
        get => (int)GetValue(IntervalProperty)!;
        set => SetValue(IntervalProperty, value);
    }

    /// <inheritdoc />
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (IsPressed)
        {
            repeatTimer.Interval = Math.Max(1, Delay);
            repeatTimer.Start();
        }
    }

    /// <inheritdoc />
    protected override void OnMouseUp(MouseEventArgs e)
    {
        repeatTimer.Stop();
        base.OnMouseUp(e);
    }

    /// <inheritdoc />
    protected override void OnEnabledChanged(EventArgs e)
    {
        if (!Enabled)
        {
            repeatTimer.Stop();
        }

        base.OnEnabledChanged(e);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            repeatTimer.Stop();
            repeatTimer.Tick -= OnRepeatTick;
            repeatTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnRepeatTick(object? sender, EventArgs e)
    {
        if (!IsPressed || !IsEnabled)
        {
            repeatTimer.Stop();
            return;
        }

        if (repeatTimer.Interval != Math.Max(1, Interval))
        {
            repeatTimer.Interval = Math.Max(1, Interval);
        }

        RaiseClick();
    }
}
