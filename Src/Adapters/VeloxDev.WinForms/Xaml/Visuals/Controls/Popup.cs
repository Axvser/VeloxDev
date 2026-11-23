using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>Where a <see cref="VisualPopup"/> is placed relative to its target.</summary>
public enum VisualPlacement
{
    /// <summary>Below the target, flipping above when there is no room.</summary>
    Bottom,

    /// <summary>Above the target, flipping below when there is no room.</summary>
    Top,

    /// <summary>To the right of the target.</summary>
    Right,

    /// <summary>To the left of the target.</summary>
    Left,
}

/// <summary>A layer that paints one child above the visual tree, inside the same surface window.</summary>
/// <remarks>
/// <para>
/// Opening moves the popup into the surface's overlay layer, which renders and hit-tests after the root tree. The
/// popup therefore escapes the clipping of its placement target and receives input first, while the whole tree still
/// has one HWND.
/// </para>
/// <para>
/// A pointer press outside the popup and outside <see cref="PlacementTarget"/> closes it unless
/// <see cref="StaysOpen"/> is set. Escape closes it from the focused element's bubbling key event.
/// </para>
/// </remarks>
public class VisualPopup : VisualElement
{
    /// <summary>Whether the popup is currently shown in the surface overlay.</summary>
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(VisualPopup),
        new PropertyMetadata(false) { AffectsRender = false });

    /// <summary>The element shown inside the popup.</summary>
    public static readonly DependencyProperty ChildProperty = DependencyProperty.Register(
        nameof(Child), typeof(VisualElement), typeof(VisualPopup),
        new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>The element the popup is positioned against.</summary>
    public static readonly DependencyProperty PlacementTargetProperty = DependencyProperty.Register(
        nameof(PlacementTarget), typeof(VisualElement), typeof(VisualPopup),
        new PropertyMetadata(null) { AffectsRender = false });

    /// <summary>The side of <see cref="PlacementTarget"/> the popup prefers.</summary>
    public static readonly DependencyProperty PlacementProperty = DependencyProperty.Register(
        nameof(Placement), typeof(VisualPlacement), typeof(VisualPopup),
        new PropertyMetadata(VisualPlacement.Bottom) { AffectsRender = false });

    /// <summary>The horizontal offset from the placement position, in pixels.</summary>
    public static readonly DependencyProperty HorizontalOffsetProperty = DependencyProperty.Register(
        nameof(HorizontalOffset), typeof(float), typeof(VisualPopup),
        new PropertyMetadata(0f) { AffectsRender = false });

    /// <summary>The vertical offset from the placement position, in pixels.</summary>
    public static readonly DependencyProperty VerticalOffsetProperty = DependencyProperty.Register(
        nameof(VerticalOffset), typeof(float), typeof(VisualPopup),
        new PropertyMetadata(0f) { AffectsRender = false });

    /// <summary>Whether the popup ignores outside pointer presses.</summary>
    public static readonly DependencyProperty StaysOpenProperty = DependencyProperty.Register(
        nameof(StaysOpen), typeof(bool), typeof(VisualPopup),
        new PropertyMetadata(false) { AffectsRender = false });

    private VisualHost? _surface;
    private bool _open;

    /// <summary>Creates a closed, hit-testable popup.</summary>
    public VisualPopup()
    {
        IsHitTestVisible = true;
        Background = Theme.Current.Surface;
        BorderBrush = Theme.Current.ControlBorder;
        BorderThickness = new XamlThickness(1f);
        CornerRadius = Theme.Current.CornerRadius;
        Padding = new XamlThickness(2f);
    }

    /// <summary>Raised after the popup opened.</summary>
    public event EventHandler? Opened;

    /// <summary>Raised after the popup closed.</summary>
    public event EventHandler? Closed;

    /// <summary>Whether the popup is currently shown in the surface overlay.</summary>
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty)!;
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>The element shown inside the popup.</summary>
    public VisualElement? Child
    {
        get => (VisualElement?)GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    /// <summary>Whether the popup is at least as wide as its placement target (a combo box, not a menu).</summary>
    public bool MatchTargetWidth { get; set; } = true;

    /// <summary>The element the popup is positioned against.</summary>
    public VisualElement? PlacementTarget
    {
        get => (VisualElement?)GetValue(PlacementTargetProperty);
        set => SetValue(PlacementTargetProperty, value);
    }

    /// <summary>The side of <see cref="PlacementTarget"/> the popup prefers.</summary>
    public VisualPlacement Placement
    {
        get => (VisualPlacement)GetValue(PlacementProperty)!;
        set => SetValue(PlacementProperty, value);
    }

    /// <summary>The horizontal offset from the placement position, in pixels.</summary>
    public float HorizontalOffset
    {
        get => (float)GetValue(HorizontalOffsetProperty)!;
        set => SetValue(HorizontalOffsetProperty, value);
    }

    /// <summary>The vertical offset from the placement position, in pixels.</summary>
    public float VerticalOffset
    {
        get => (float)GetValue(VerticalOffsetProperty)!;
        set => SetValue(VerticalOffsetProperty, value);
    }

    /// <summary>Whether the popup ignores outside pointer presses.</summary>
    public bool StaysOpen
    {
        get => (bool)GetValue(StaysOpenProperty)!;
        set => SetValue(StaysOpenProperty, value);
    }

    /// <summary>Opens the popup; the surface is resolved through the placement target or the popup's own parent.</summary>
    public void Open() => IsOpen = true;

    /// <summary>Closes the popup and returns its child to the detached state.</summary>
    public void Close() => IsOpen = false;

    /// <summary>Recomputes the popup's position and size from the target and the child's desired size.</summary>
    public void Reposition()
    {
        if (!_open || _surface is null)
        {
            return;
        }

        // 弹层的位置与偏移都是逻辑单位，物理像素交给表面的世界缩放。
        var client = _surface.LogicalClientSize;
        var available = new Size(Math.Max(0, client.Width), Math.Max(0, client.Height));
        // 自绘弹层（菜单）没有 Child，Measure 走它自己的 MeasureOverride；有 Child 时由基类量 Child + Padding。
        var desired = Measure(available);
        var target = PlacementTarget?.AbsoluteBounds ?? RectangleF.Empty;
        var width = MatchTargetWidth
            ? Math.Max(1f, Math.Max(target.Width, desired.Width))
            : Math.Max(1f, desired.Width);
        var height = Math.Max(1f, desired.Height);

        var (x, y) = Placement switch
        {
            VisualPlacement.Top => (target.Left, target.Top - height),
            VisualPlacement.Right => (target.Right, target.Top),
            VisualPlacement.Left => (target.Left - width, target.Top),
            _ => (target.Left, target.Bottom),
        };
        x += HorizontalOffset;
        y += VerticalOffset;

        if (Placement is VisualPlacement.Bottom or VisualPlacement.Top)
        {
            // 主方向放不下时翻到另一侧；两侧都放不下就截断到客户区内。
            if (Placement == VisualPlacement.Bottom && y + height > client.Height && target.Top - height >= 0f)
            {
                y = target.Top - height + VerticalOffset;
            }
            else if (Placement == VisualPlacement.Top && y < 0f && target.Bottom + height <= client.Height)
            {
                y = target.Bottom + VerticalOffset;
            }
        }

        x = XamlMath.Clamp(x, 0f, Math.Max(0f, client.Width - width));
        y = XamlMath.Clamp(y, 0f, Math.Max(0f, client.Height - height));
        Arrange(new RectangleF(x, y, width, height));
        InvalidateVisual();
    }

    // 目标或自身在祖先链上即视为点击来自弹层内部；ComboBox 点自己那一格时也走这条。
    internal bool ShouldStayOpen(VisualElement? target)
    {
        if (target is null)
        {
            return false;
        }

        for (var current = target; current is not null; current = current.ParentElement)
        {
            if (ReferenceEquals(current, this) || ReferenceEquals(current, PlacementTarget))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is not { } child)
        {
            return new Size(
                (int)Math.Ceiling(Padding.Horizontal),
                (int)Math.Ceiling(Padding.Vertical));
        }

        var inner = new Size(
            Math.Max(0, availableSize.Width - (int)Math.Ceiling(Padding.Horizontal)),
            Math.Max(0, availableSize.Height - (int)Math.Ceiling(Padding.Vertical)));
        var desired = child.Measure(inner);
        return new Size(
            desired.Width + (int)Math.Ceiling(Padding.Horizontal),
            desired.Height + (int)Math.Ceiling(Padding.Vertical));
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        if (Child is not { } child)
        {
            return;
        }

        child.Arrange(new RectangleF(
            (int)Math.Ceiling(Padding.Left),
            (int)Math.Ceiling(Padding.Top),
            Math.Max(0f, finalSize.Width - Padding.Horizontal),
            Math.Max(0f, finalSize.Height - Padding.Vertical)));
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (ReferenceEquals(e.Property, IsOpenProperty))
        {
            if (IsOpen)
            {
                OpenCore();
            }
            else
            {
                CloseCore();
            }

            return;
        }

        if (ReferenceEquals(e.Property, ChildProperty))
        {
            var old = e.OldValue as VisualElement;
            if (old is not null && Children.Contains(old))
            {
                RemoveChild(old);
            }

            if (_open)
            {
                AddChild();
                Reposition();
            }

            return;
        }

        if (ReferenceEquals(e.Property, PlacementTargetProperty)
            || ReferenceEquals(e.Property, PlacementProperty)
            || ReferenceEquals(e.Property, HorizontalOffsetProperty)
            || ReferenceEquals(e.Property, VerticalOffsetProperty))
        {
            Reposition();
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(VisualKeyEventArgs args)
    {
        base.OnKeyDown(args);
        if (args.KeyCode == Keys.Escape && _open)
        {
            Close();
            args.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CloseCore();
            Child?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OpenCore()
    {
        if (_open)
        {
            return;
        }

        var surface = PlacementTarget?.Surface ?? Surface;
        if (surface is null)
        {
            return;
        }

        _surface = surface;
        _open = true;
        surface.RegisterPopup(this);
        surface.OverlayHost.Add(this);
        AddChild();

        // 弹层脱离了根树，祖先链上的 DataContext / 资源 / 隐式样式都断了；这里补上目标的 DataContext。
        if (PlacementTarget is { } target && Child is { } child && !child.IsSet(VisualElement.DataContextProperty))
        {
            child.SetValue(VisualElement.DataContextProperty, target.DataContext, BindingPriority.Inherited);
        }

        Reposition();
        surface.InvalidateSurface();
        Opened?.Invoke(this, EventArgs.Empty);
    }

    private void CloseCore()
    {
        if (!_open)
        {
            return;
        }

        _open = false;
        _surface?.UnregisterPopup(this);
        if (_surface?.FocusedElement is { } focused && IsInside(focused))
        {
            _surface.FocusElement(null);
        }

        if (Child is { } child)
        {
            child.ClearValue(VisualElement.DataContextProperty, BindingPriority.Inherited);
            if (Children.Contains(child))
            {
                RemoveChild(child);
            }
        }

        _surface?.OverlayHost.Remove(this);
        _surface?.InvalidateSurface();
        _surface = null;
        Bounds = RectangleF.Empty;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void AddChild()
    {
        if (Child is not { } child || Children.Contains(child))
        {
            return;
        }

        // 子项可能原本挂在别的容器里；先摘掉，避免同一个节点同时进两个父集合。
        if (child.Parent is ContainerVisual parent)
        {
            parent.Remove(child);
        }

        Add(child);
    }

    // 关闭时把落在弹层里的逻辑焦点交还出去；调用方（ComboBox、菜单）决定接下来聚焦谁。
    private bool IsInside(VisualElement element)
    {
        for (var current = element; current is not null; current = current.ParentElement)
        {
            if (ReferenceEquals(current, this))
            {
                return true;
            }
        }

        return false;
    }
}
