using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>
/// Realtime floating-text info layer for the node-editor surface: canvas actual size, the visible
/// viewport (canvas + world), zoom/origin and the visible node/link elements materialized by the Core
/// virtualization. Repaints from the Core model (Layout / helper VisibleItems / Nodes / Links) plus
/// the ScrollOffset/ContentOffset/Viewport DPs that the host binds (same feed as the minimap). The
/// 复制 button copies the current multi-line info to the clipboard.
///
/// The extra "[测试]" line is fed by WorkflowLinkBehaviors' six static global events — see the TEST-ONLY
/// region below.
/// </summary>
public partial class InfoOverlay : UserControl
{
    // =============================================================================================================
    // [TEST-ONLY] 六个 Link 指针/键盘事件的连带测试挂钩：
    //   PointerEntered / PointerLeaved / PointerPressed / PointerReleased（原生 MouseEventArgs 系）+ KeyDown + KeyUp。
    // 订阅的是 WorkflowLinkBehaviors 的静态全局事件，用于在 Trimmed demo 里人工连带验证「armed link →
    // 行为几何命中 → 全局事件 → 这里显示」这条链路是否通；KeyDown=Delete 时真实执行 link.DeleteCommand。
    //
    // ⚠️ 本区域仅用于测试：不计入 WorkflowSystem 的适配器/模板体系。它不是参考消费者、不是 API 面；
    //     adapter 层与各 workflow-* 模板都不得依赖或引用这里的任何东西。若要从 UHD 移除，删掉本区域 +
    //     InfoOverlay.xaml 里的 TestFeedText 即可，不影响任何生产路径。
    // =============================================================================================================
    private readonly MouseEventHandler _testOnEntered;
    private readonly MouseEventHandler _testOnLeaved;
    private readonly MouseButtonEventHandler _testOnPressed;
    private readonly MouseButtonEventHandler _testOnReleased;
    private readonly KeyEventHandler _testOnKeyDown;
    private readonly KeyEventHandler _testOnKeyUp;
    private readonly MouseWheelEventHandler _testOnMouseWheel;

    public static readonly DependencyProperty ScrollOffsetXProperty = DependencyProperty.Register(
        nameof(ScrollOffsetX), typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    public static readonly DependencyProperty ScrollOffsetYProperty = DependencyProperty.Register(
        nameof(ScrollOffsetY), typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    public static readonly DependencyProperty ContentOffsetXProperty = DependencyProperty.Register(
        nameof(ContentOffsetX), typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    public static readonly DependencyProperty ContentOffsetYProperty = DependencyProperty.Register(
        nameof(ContentOffsetY), typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    public static readonly DependencyProperty ViewportWidthProperty = DependencyProperty.Register(
        nameof(ViewportWidth), typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    public static readonly DependencyProperty ViewportHeightProperty = DependencyProperty.Register(
        nameof(ViewportHeight), typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));

    public double ScrollOffsetX { get => (double)GetValue(ScrollOffsetXProperty); set => SetValue(ScrollOffsetXProperty, value); }
    public double ScrollOffsetY { get => (double)GetValue(ScrollOffsetYProperty); set => SetValue(ScrollOffsetYProperty, value); }
    public double ContentOffsetX { get => (double)GetValue(ContentOffsetXProperty); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => (double)GetValue(ContentOffsetYProperty); set => SetValue(ContentOffsetYProperty, value); }
    public double ViewportWidth { get => (double)GetValue(ViewportWidthProperty); set => SetValue(ViewportWidthProperty, value); }
    public double ViewportHeight { get => (double)GetValue(ViewportHeightProperty); set => SetValue(ViewportHeightProperty, value); }

    private IWorkflowTreeViewModel? _tree;
    private string _copyText = "";

    public InfoOverlay()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        _testOnEntered = OnTestEntered;
        _testOnLeaved = OnTestLeaved;
        _testOnPressed = OnTestPressed;
        _testOnReleased = OnTestReleased;
        _testOnKeyDown = OnTestKeyDown;
        _testOnKeyUp = OnTestKeyUp;
        _testOnMouseWheel = OnTestMouseWheel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WorkflowLinkBehaviors.PointerEntered += _testOnEntered;
        WorkflowLinkBehaviors.PointerLeaved += _testOnLeaved;
        WorkflowLinkBehaviors.PointerPressed += _testOnPressed;
        WorkflowLinkBehaviors.PointerReleased += _testOnReleased;
        WorkflowLinkBehaviors.KeyDown += _testOnKeyDown;
        WorkflowLinkBehaviors.KeyUp += _testOnKeyUp;
        WorkflowSurfaceBehavior.MouseWheel += _testOnMouseWheel;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        WorkflowLinkBehaviors.PointerEntered -= _testOnEntered;
        WorkflowLinkBehaviors.PointerLeaved -= _testOnLeaved;
        WorkflowLinkBehaviors.PointerPressed -= _testOnPressed;
        WorkflowLinkBehaviors.PointerReleased -= _testOnReleased;
        WorkflowLinkBehaviors.KeyDown -= _testOnKeyDown;
        WorkflowLinkBehaviors.KeyUp -= _testOnKeyUp;
        WorkflowSurfaceBehavior.MouseWheel -= _testOnMouseWheel;
    }

    private void OnTestEntered(object? sender, MouseEventArgs e) => ShowTestState("Entered", sender);
    private void OnTestLeaved(object? sender, MouseEventArgs e) => ShowTestState("Leaved", sender);
    private void OnTestPressed(object? sender, MouseButtonEventArgs e) => ShowTestState("Pressed", sender);
    private void OnTestReleased(object? sender, MouseButtonEventArgs e) => ShowTestState("Released", sender);

    private void OnTestKeyDown(object? sender, KeyEventArgs e)
    {
        // 真实响应 Delete：命中悬停/聚焦 link 时删除它。
        if (e.Key == Key.Delete && (sender as FrameworkElement)?.DataContext is IWorkflowLinkViewModel link)
        {
            link.DeleteCommand.Execute(null);
        }

        ShowTestState("KeyDown(" + e.Key + ")", sender);
    }

    private void OnTestKeyUp(object? sender, KeyEventArgs e)
        => ShowTestState("KeyUp(" + e.Key + ")", sender);

    /// <summary>Wheel-injection example: Alt+wheel scrolls the canvas horizontally, Shift+wheel vertically.
    /// Plain wheel is intentionally left to the canvas scroll eating.</summary>
    private void OnTestMouseWheel(object? sender, MouseWheelEventArgs e)
    {
        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        if (!alt && !shift)
        {
            return; // 普通滚轮：不接管（画布滚动拦截吃掉）
        }

        if (sender is not DependencyObject host || FindScrollViewer(host) is not { } viewer)
        {
            return;
        }

        double step = e.Delta > 0 ? -72 : 72;
        if (alt)
        {
            viewer.ScrollToHorizontalOffset(Math.Clamp(viewer.HorizontalOffset + step, 0, viewer.ScrollableWidth));
        }
        else
        {
            viewer.ScrollToVerticalOffset(Math.Clamp(viewer.VerticalOffset + step, 0, viewer.ScrollableHeight));
        }

        e.Handled = true;
        ShowTestState(alt ? "Wheel(Alt→H)" : "Wheel(Shift→V)", sender);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer)
            {
                return viewer;
            }

            if (FindScrollViewer(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Updates the "[测试]" line from whichever of the six global events last fired.</summary>
    private void ShowTestState(string kind, object? sender)
    {
        var link = (sender as FrameworkElement)?.DataContext as IWorkflowLinkViewModel;
        string id = link is IWorkflowIdentifiable identifiable ? ShortRuntimeId(identifiable.RuntimeId) : "—";
        TestFeedText.Text = "[测试] " + kind + " · Link " + id;
    }

    private static string ShortRuntimeId(string runtimeId)
        => runtimeId.Length > 8 ? runtimeId.Substring(0, 8) : runtimeId;

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InfoOverlay overlay)
        {
            overlay.Refresh();
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        UnsubscribeTree();
        _tree = DataContext as IWorkflowTreeViewModel;
        SubscribeTree();
        Refresh();
    }

    private void SubscribeTree()
    {
        if (_tree is null)
        {
            return;
        }

        if (_tree.Layout is INotifyPropertyChanged layout)
        {
            layout.PropertyChanged += OnModelChanged;
        }

        _tree.Nodes.CollectionChanged += OnCollectionChanged;
        _tree.Links.CollectionChanged += OnCollectionChanged;
        _tree.GetHelper().VisibleItems.CollectionChanged += OnCollectionChanged;
    }

    private void UnsubscribeTree()
    {
        if (_tree is null)
        {
            return;
        }

        if (_tree.Layout is INotifyPropertyChanged layout)
        {
            layout.PropertyChanged -= OnModelChanged;
        }

        _tree.Nodes.CollectionChanged -= OnCollectionChanged;
        _tree.Links.CollectionChanged -= OnCollectionChanged;
        _tree.GetHelper().VisibleItems.CollectionChanged -= OnCollectionChanged;
        _tree = null;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    /// <summary>Recomputes display lines + copy text from the current model/scroll state.</summary>
    private void Refresh()
    {
        string[] lines = BuildLines();
        InfoText.Text = string.Join("\n", lines);
        _copyText = string.Join(Environment.NewLine, lines);
        CopyButton.Content = "复制";
    }

    private string[] BuildLines()
    {
        if (_tree is null)
        {
            return new[] { "VeloxDev Workflow — 未绑定画布" };
        }

        var layout = _tree.Layout;
        var actual = layout.ActualSize;
        double ox = ContentOffsetX, oy = ContentOffsetY;
        double sx = ScrollOffsetX, sy = ScrollOffsetY;
        double vw = ViewportWidth, vh = ViewportHeight;
        double wx = sx - ox, wy = sy - oy;

        double scale = layout.Scale.Horizontal;
        double zoomPercent = scale > 0 ? 100.0 / scale : 100.0;

        int totalNodes = _tree.Nodes.Count;
        int totalLinks = _tree.Links.Count;
        int visibleNodes = 0;
        int visibleLinks = 0;
        var virtualLink = _tree.VirtualLink;
        foreach (var item in _tree.GetHelper().VisibleItems)
        {
            if (item is IWorkflowNodeViewModel)
            {
                visibleNodes++;
            }
            else if (item is IWorkflowLinkViewModel link && !ReferenceEquals(link, virtualLink))
            {
                visibleLinks++;
            }
        }

        return new[]
        {
            "画布 " + Fmt(actual.Width) + " × " + Fmt(actual.Height),
            "视口(画布) " + Fmt(sx) + ", " + Fmt(sy) + "  " + Fmt(vw) + "×" + Fmt(vh),
            "视口(世界) " + Fmt(wx) + ", " + Fmt(wy) + "  " + Fmt(vw) + "×" + Fmt(vh),
            "缩放 " + Math.Round(zoomPercent).ToString() + "%  ·  Scale " + scale.ToString("0.00"),
            "原点 " + Fmt(ox) + ", " + Fmt(oy),
            "元素 节点 " + visibleNodes + "/" + totalNodes + " · 连线 " + visibleLinks + "/" + totalLinks,
        };
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_copyText);
            CopyButton.Content = "已复制";
        }
        catch
        {
            CopyButton.Content = "复制失败";
        }
    }

    private static string Fmt(double value)
    {
        double abs = Math.Abs(value);
        if (abs < 10000) return Math.Round(value).ToString();
        if (abs < 1000000) return Math.Round(value / 1000.0, 1).ToString() + "K";
        return Math.Round(value / 1000000.0, 1).ToString() + "M";
    }
}
