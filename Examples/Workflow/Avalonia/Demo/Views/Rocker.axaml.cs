using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;

namespace Demo;

public partial class Rocker : UserControl
{
    public event EventHandler<Vector>? JoystickChanged;

    public static readonly StyledProperty<double> XProperty =
        AvaloniaProperty.Register<Rocker, double>(nameof(X), 0.0, coerce: ValidateAxis);

    public static readonly StyledProperty<double> YProperty =
        AvaloniaProperty.Register<Rocker, double>(nameof(Y), 0.0, coerce: ValidateAxis);

    public static readonly StyledProperty<double> ScaleProperty =
        AvaloniaProperty.Register<Rocker, double>(nameof(Scale), 0.5, coerce: ValidateScale);

    private static double ValidateAxis(AvaloniaObject sender, double value) => Math.Clamp(value, -1.0, 1.0);
    private static double ValidateScale(AvaloniaObject sender, double value) => Math.Clamp(value, 0.0, 1.0);

    public double X
    {
        get => GetValue(XProperty);
        set => SetValue(XProperty, value);
    }

    public double Y
    {
        get => GetValue(YProperty);
        set => SetValue(YProperty, value);
    }

    public double Scale
    {
        get => GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    private bool _isDragging = false;
    private Point _dragOffset; // 指针相对于小圆中心的偏移（像素）

    public Rocker()
    {
        InitializeComponent();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        ResetJoystick();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateVisualPosition();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == XProperty ||
            change.Property == YProperty ||
            change.Property == ScaleProperty)
        {
            UpdateVisualPosition();
        }
    }

    private void UpdateVisualPosition()
    {
        if (PART_BOARD?.Bounds is not { } bounds || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        if (PART_FG == null) return;

        double size = Math.Min(bounds.Width, bounds.Height);
        double bigRadius = size / 2;
        double smallDiameter = size * Scale;
        double smallRadius = smallDiameter / 2;

        PART_FG.Width = smallDiameter;
        PART_FG.Height = smallDiameter;

        double maxMove = Math.Max(0, bigRadius - smallRadius);

        double centerX = bounds.Width / 2 + X * maxMove;
        double centerY = bounds.Height / 2 + Y * maxMove;

        double left = centerX - smallRadius;
        double top = centerY - smallRadius;

        PART_FG.Margin = new Thickness(left, top, 0, 0);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (PART_BOARD == null || PART_FG == null) return;

        _isDragging = true;
        var pointerPos = e.GetPosition(PART_BOARD);

        // 获取当前小圆中心（像素）
        var bounds = PART_BOARD.Bounds;
        double size = Math.Min(bounds.Width, bounds.Height);
        double maxMove = Math.Max(0, size / 2 * (1 - Scale));
        double currentCenterX = bounds.Width / 2 + X * maxMove;
        double currentCenterY = bounds.Height / 2 + Y * maxMove;

        _dragOffset = new Point(
            pointerPos.X - currentCenterX,
            pointerPos.Y - currentCenterY
        );

        UpdateFromPointer(pointerPos);
        e.Pointer.Capture(PART_FG);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDragging && PART_BOARD != null)
        {
            var pos = e.GetPosition(PART_BOARD);
            UpdateFromPointer(pos);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ResetJoystick();
            e.Pointer.Capture(null);
        }
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (!_isDragging)
        {
            ResetJoystick();
        }
    }

    private void UpdateFromPointer(Point pointerPos)
    {
        if (PART_BOARD?.Bounds is not { } bounds || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        double size = Math.Min(bounds.Width, bounds.Height);
        double maxMove = Math.Max(0, size / 2 * (1 - Scale));

        double boardCenterX = bounds.Width / 2;
        double boardCenterY = bounds.Height / 2;

        // 减去 drag offset 得到期望的小圆中心位置
        double targetCenterX = pointerPos.X - _dragOffset.X;
        double targetCenterY = pointerPos.Y - _dragOffset.Y;

        double dx = targetCenterX - boardCenterX;
        double dy = targetCenterY - boardCenterY;

        if (maxMove <= 0)
        {
            X = 0;
            Y = 0;
        }
        else
        {
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len > maxMove)
            {
                // 归一化到圆边界
                dx = dx * maxMove / len;
                dy = dy * maxMove / len;
            }

            // 转换为 [-1, 1] 范围
            X = dx / maxMove;
            Y = dy / maxMove;
        }

        OnJoystickChanged();
    }

    private void ResetJoystick()
    {
        X = 0.0;
        Y = 0.0;
        OnJoystickChanged();
    }

    protected virtual void OnJoystickChanged()
    {
        JoystickChanged?.Invoke(this, new Vector(X, Y));
    }
}