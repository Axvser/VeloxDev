using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VisualButton = VeloxDev.WinForms.Xaml.Visuals.Controls.Button;
using DrawingPen = System.Drawing.Pen;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>Realtime canvas-info HUD drawn by the visual backend inside the workflow tree.</summary>
/// <remarks>
/// The surface feeds the scroll/content offsets and the camera zoom; the HUD reads the Core tree for actual size and
/// visible counts. The text area is hit-test transparent so drags fall through; the built-in copy button is the
/// only interactive region.
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
    private readonly System.Windows.Forms.Timer _copyResetTimer = new() { Interval = 1500 };

    /// <summary>Creates the HUD, its built-in copy button and the model subscriptions.</summary>
    public WorkflowInfoOverlay()
    {
        Width = 276d;
        Height = 138d;
        IsHitTestVisible = false;

        CopyButton = new VisualButton
        {
            Text = "复制",
            Width = 58d,
            Height = 22d,
            FontSize = 11f,
            Foreground = Color.FromArgb(0x7E, 0xC8, 0xFF),
            Background = Color.FromArgb(0x1E, 0x3A, 0x5F),
            BorderBrush = Color.FromArgb(0x7E, 0xC8, 0xFF),
            BorderThickness = new VeloxDev.WinForms.Xaml.Controls.Thickness(1f),
            Padding = new VeloxDev.WinForms.Xaml.Controls.Thickness(2f),
            CornerRadius = 4f,
        };
        CopyButton.Click += OnCopyClicked;
        _copyResetTimer.Tick += OnCopyResetTimerTick;
        Add(CopyButton);
        SubscribeTree();
    }

    /// <summary>The interactive copy button; the host can restyle it.</summary>
    public VisualButton CopyButton { get; }

    /// <summary>Raised with a short status message after a copy attempt.</summary>
    public event EventHandler<string>? CopyFeedback;

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

    /// <summary>The lines the HUD is currently showing, in display order; a host can copy them as text.</summary>
    public IReadOnlyList<string> Lines => BuildLines();

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
    protected override System.Drawing.Size MeasureOverride(System.Drawing.Size availableSize)
    {
        var width = double.IsNaN(Width) ? availableSize.Width : (int)Math.Round(Width);
        var height = double.IsNaN(Height) ? availableSize.Height : (int)Math.Round(Height);
        CopyButton.Measure(new System.Drawing.Size((int)Math.Round(CopyButton.Width), (int)Math.Round(CopyButton.Height)));
        return new System.Drawing.Size(width, height);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(System.Drawing.Size finalSize)
    {
        var width = (float)Math.Round(CopyButton.Width);
        var height = (float)Math.Round(CopyButton.Height);
        CopyButton.Arrange(new System.Drawing.RectangleF(finalSize.Width - width - 8f, finalSize.Height - height - 8f, width, height));
    }

    private void OnCopyClicked(object? sender, EventArgs e)
    {
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, Lines));
            CopyButton.Text = "已复制";
            CopyFeedback?.Invoke(this, "已复制画布信息");
        }
        catch (ExternalException)
        {
            CopyButton.Text = "复制失败";
            CopyFeedback?.Invoke(this, "复制失败：剪贴板被占用");
        }
        catch (ThreadStateException)
        {
            CopyButton.Text = "复制失败";
            CopyFeedback?.Invoke(this, "复制失败：剪贴板不可用");
        }

        _copyResetTimer.Stop();
        _copyResetTimer.Start();
    }

    private void OnCopyResetTimerTick(object? sender, EventArgs e)
    {
        _copyResetTimer.Stop();
        CopyButton.Text = "复制";
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _copyResetTimer.Stop();
            _copyResetTimer.Dispose();
            UnsubscribeTree();
        }

        base.Dispose(disposing);
    }

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
                new RectangleF(10f, y, Math.Max(1f, width - 20f - 66f), 20f),
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
