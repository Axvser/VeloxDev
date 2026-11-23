using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using DrawingPen = System.Drawing.Pen;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>Realtime canvas-info HUD drawn by the visual backend inside the workflow tree.</summary>
/// <remarks>
/// The surface feeds the scroll/content offsets and the camera zoom; the HUD reads the Core tree for actual size and
/// visible counts. It is hit-test transparent so drags fall through to the surface.
/// </remarks>
public sealed class WorkflowInfoOverlay : VisualElement
{
    private static readonly Font InfoFont = new("Segoe UI", 12f, GraphicsUnit.Pixel);

    /// <summary>The horizontal scroll offset, in surface pixels.</summary>
    public static readonly DependencyProperty ScrollOffsetXProperty = DependencyProperty.Register(
        nameof(ScrollOffsetX), typeof(double), typeof(WorkflowInfoOverlay),
        new PropertyMetadata(0d) { AffectsRender = true });

    /// <summary>The vertical scroll offset, in surface pixels.</summary>
    public static readonly DependencyProperty ScrollOffsetYProperty = DependencyProperty.Register(
        nameof(ScrollOffsetY), typeof(double), typeof(WorkflowInfoOverlay),
        new PropertyMetadata(0d) { AffectsRender = true });

    /// <summary>The horizontal content offset (canvas layout origin plus ruler band).</summary>
    public static readonly DependencyProperty ContentOffsetXProperty = DependencyProperty.Register(
        nameof(ContentOffsetX), typeof(double), typeof(WorkflowInfoOverlay),
        new PropertyMetadata(0d) { AffectsRender = true });

    /// <summary>The vertical content offset (canvas layout origin plus ruler band).</summary>
    public static readonly DependencyProperty ContentOffsetYProperty = DependencyProperty.Register(
        nameof(ContentOffsetY), typeof(double), typeof(WorkflowInfoOverlay),
        new PropertyMetadata(0d) { AffectsRender = true });

    /// <summary>The camera zoom.</summary>
    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(WorkflowInfoOverlay),
        new PropertyMetadata(1d) { AffectsRender = true });

    private ITreeComponent? _tree;
    private INotifyPropertyChanged? _layout;

    /// <summary>Creates the HUD at its design size and makes it hit-test transparent.</summary>
    public WorkflowInfoOverlay()
    {
        Width = 276d;
        Height = 138d;
        IsHitTestVisible = false;
        SubscribeTree();
    }

    /// <summary>The horizontal scroll offset.</summary>
    public double ScrollOffsetX
    {
        get => (double)GetValue(ScrollOffsetXProperty)!;
        set => SetValue(ScrollOffsetXProperty, value);
    }

    /// <summary>The vertical scroll offset.</summary>
    public double ScrollOffsetY
    {
        get => (double)GetValue(ScrollOffsetYProperty)!;
        set => SetValue(ScrollOffsetYProperty, value);
    }

    /// <summary>The horizontal content offset.</summary>
    public double ContentOffsetX
    {
        get => (double)GetValue(ContentOffsetXProperty)!;
        set => SetValue(ContentOffsetXProperty, value);
    }

    /// <summary>The vertical content offset.</summary>
    public double ContentOffsetY
    {
        get => (double)GetValue(ContentOffsetYProperty)!;
        set => SetValue(ContentOffsetYProperty, value);
    }

    /// <summary>The camera zoom.</summary>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty)!;
        set => SetValue(ZoomProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, DataContextProperty))
        {
            SubscribeTree();
        }
    }

    private void SubscribeTree()
    {
        UnsubscribeTree();
        _tree = DataContext as ITreeComponent;
        if (_tree is null)
        {
            return;
        }

        if (_tree.Layout is INotifyPropertyChanged layout)
        {
            _layout = layout;
            layout.PropertyChanged += OnModelChanged;
        }

        _tree.Nodes.CollectionChanged += OnCollectionChanged;
        _tree.Links.CollectionChanged += OnCollectionChanged;
        _tree.GetHelper().VisibleItems.CollectionChanged += OnCollectionChanged;
        InvalidateVisual();
    }

    private void UnsubscribeTree()
    {
        if (_layout is not null)
        {
            _layout.PropertyChanged -= OnModelChanged;
            _layout = null;
        }

        if (_tree is not null)
        {
            _tree.Nodes.CollectionChanged -= OnCollectionChanged;
            _tree.Links.CollectionChanged -= OnCollectionChanged;
            _tree.GetHelper().VisibleItems.CollectionChanged -= OnCollectionChanged;
            _tree = null;
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0f || height <= 0f)
        {
            return;
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new SolidBrush(Color.FromArgb(0xE6, 0x12, 0x15, 0x1B));
        using var border = new DrawingPen(Color.FromArgb(0xCC, 0x8E, 0xA3, 0xB8), 1f);
        graphics.FillRectangle(background, 0f, 0f, width, height);
        graphics.DrawRectangle(border, 0f, 0f, Math.Max(0f, width - 1f), Math.Max(0f, height - 1f));

        var y = 8f;
        foreach (var line in BuildLines())
        {
            VisualText.Draw(
                this,
                graphics,
                line,
                InfoFont,
                Color.FromArgb(0xDD, 0xE4, 0xEA),
                new RectangleF(10f, y, Math.Max(1f, width - 20f), 20f),
                wrap: false,
                StringAlignment.Near,
                StringAlignment.Near);
            y += 20f;
        }
    }

    private string[] BuildLines()
    {
        if (_tree is null)
        {
            return ["VeloxDev Workflow — 未绑定画布"];
        }

        var layout = _tree.Layout;
        var actual = layout.ActualSize;
        var sx = ScrollOffsetX;
        var sy = ScrollOffsetY;
        var ox = ContentOffsetX;
        var oy = ContentOffsetY;
        var viewport = FindViewport();
        var worldX = sx - ox;
        var worldY = sy - oy;
        var zoom = Zoom <= 0d ? 1d : Zoom;

        var visibleNodes = 0;
        var visibleLinks = 0;
        foreach (var item in _tree.GetHelper().VisibleItems)
        {
            if (item is INodeComponent)
            {
                visibleNodes++;
            }
            else if (item is ILinkComponent link && !ReferenceEquals(link, _tree.VirtualLink))
            {
                visibleLinks++;
            }
        }

        return
        [
            $"画布 {Format(actual.Width)} × {Format(actual.Height)}",
            $"视口(画布) {Format(sx)}, {Format(sy)}  {Format(viewport.Width)}×{Format(viewport.Height)}",
            $"视口(世界) {Format(worldX)}, {Format(worldY)}  {Format(viewport.Width / zoom)}×{Format(viewport.Height / zoom)}",
            $"缩放 {Math.Round(zoom * 100d)}%  ·  相机 {zoom:0.00}",
            $"原点 {Format(ox)}, {Format(oy)}",
            $"元素 节点 {visibleNodes}/{_tree.Nodes.Count} · 连线 {visibleLinks}/{_tree.Links.Count}",
        ];
    }

    private SizeF FindViewport()
    {
        VisualElement root = this;
        while (root.ParentElement is { } parent)
        {
            root = parent;
        }

        if (FindNamed(root, "PART_SurfaceBorder") is { } border)
        {
            return new SizeF(border.Bounds.Width, border.Bounds.Height);
        }

        return Bounds.Size;
    }

    private static VisualElement? FindNamed(VisualElement root, string name)
    {
        if (string.Equals(root.Name, name, StringComparison.Ordinal))
        {
            return root;
        }

        foreach (var child in root.Children)
        {
            if (child is VisualElement element && FindNamed(element, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static string Format(double value)
    {
        var abs = Math.Abs(value);
        if (abs < 10000d) return Math.Round(value).ToString();
        if (abs < 1000000d) return (Math.Round(value / 1000d, 1)).ToString() + "K";
        return (Math.Round(value / 1000000d, 1)).ToString() + "M";
    }
}
