using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Views.Workflow;
using Demo.Workflow;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using Microsoft.Win32;
using VeloxDev.AI;
using VeloxDev.AI.MCP;
using VeloxDev.AI.Safety;
using VeloxDev.AI.SubAgents;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

namespace Demo;

/// <summary>The full WorkflowSystem demo on Jalium, in the same workbench shell as the Avalonia demo:
/// a 32px title strip, a 48px activity bar beside a 272px sidebar (Explorer / Run / MCP), a main area
/// whose tabs switch between the canvas and the execution output, a 348px right inspector (Properties /
/// AI assistant), and a 24px status bar. The canvas is the SAME NodeEditorSurface the trimmed demo uses
/// (drag / connect / pan / auto-grow / minimap all identical), bound to the shared voltage-analysis chain
/// (WorkflowDemoSession.Create). The Agent only responds when API_KEY_DEEPSEEK is configured.</summary>
internal sealed class MainWindow : Window
{
    private readonly NodeEditorSurface _surface;
    private readonly ScrollViewer _surfaceViewer;
    private readonly Minimap _minimap;
    private readonly Dispatcher _uiDispatcher;

    // 活动栏与侧栏的页：三页只亮一页。
    private readonly List<ActivityItem> _activityItems = [];
    private string _activeActivity = "explorer";
    private TextBlock _sidebarHeader = null!;
    private FrameworkElement _explorerPage = null!;
    private FrameworkElement _runPage = null!;
    private FrameworkElement _mcpPage = null!;

    // 资源管理器：元素表与「定位 / 删除」都走共享的 WorkflowElementCatalog。
    private readonly WorkflowElementCatalog _elementCatalog = new();
    private readonly List<ListBox> _elementLists = [];
    private readonly TextBlock _elementStatus = new();
    private CheckBox? _minimapToggle;
    private string _lastElementAction = "就绪";
    private bool _syncingElementSelection;

    // 主区域两个标签页。
    private TabControl _mainTabs = null!;

    // 右侧检查器两个标签页。
    private TabControl _inspectorTabs = null!;

    // 输出页。
    private readonly ListBox _executionLog = new();
    private readonly TextBlock _outputRoot = new();
    private readonly TextBlock _outputState = new();
    private readonly TextBlock _outputProgress = new();
    private readonly TextBlock _outputCount = new();
    private readonly TextBlock _outputHint = new();

    // 运行管理器。行按 Runs.Tasks 现建 —— Jalium 没有逐行 IsVisible 绑定，Can* 的闸门只能落在建行的时候。
    private readonly StackPanel _runRows = new() { Spacing = 0 };
    private readonly TextBlock _runSummary = new();
    private readonly TextBlock _runEmptyHint = new();

    // 属性页。
    private readonly StackPanel _propSelected = new() { Spacing = 8 };
    private readonly TextBlock _propLabel = new();
    private readonly TextBlock _propKind = new();
    private readonly TextBlock _propDetail = new();
    private readonly TextBlock _propEmpty = new();
    private readonly StackPanel _compiledRows = new() { Spacing = 3 };
    private readonly TextBlock _compiledCount = new();
    private readonly TextBlock _compiledEmpty = new();
    private WorkflowElementEntry? _watchedEntry;

    // 状态栏。
    private readonly Border _statusDot = new();
    private readonly TextBlock _statusRun = new();
    private readonly TextBlock _statusCompiled = new();
    private readonly TextBlock _statusElements = new();
    private readonly TextBlock _statusVisible = new();
    private readonly TextBlock _statusZoom = new();

    // AI 助手页。
    private readonly ListBox _agentLog = new();
    private readonly TextBox _agentInput = new();
    private ComboBox? _permissionModePicker;
    private CheckBox? _streamToggle;
    private FrameworkElement _subAgentPanel = null!;
    private readonly StackPanel _subAgentRows = new() { Spacing = 3 };
    private readonly TextBlock _subAgentSummary = new();
    private readonly TextBlock _subAgentEmpty = new();
    private SubAgentTreeViewModel? _subAgentTree;
    private DispatcherTimer? _subAgentTick;

    private TreeViewModel _tree = new();
    private TreeViewModel? _boundTree;
    private WorkflowDemoSession? _demo;

    // 一次 Agent 工具调用就要刷新一次，而刷新是全量的；合并之后一轮只刷一次。
    private CoalescedRefresh? _surfaceRefresh;

    private readonly TextBlock _mcpSummary = new();
    private McpStatusViewModel? _mcpStatus;
    private readonly HashSet<McpServerStatusViewModel> _mcpServerSubs = [];

    // 目录里一次运行会连着改好几个聚合量；一帧一刷，避免在按钮的点击调用栈里换掉那颗按钮本身。
    private bool _refreshQueued;

    public MainWindow()
    {
        Title = "VeloxDev Workflow - Jalium";
        Width = 1500;
        Height = 900;
        Background = CardPalette.CodeSurfaceBrush;

        _uiDispatcher = Dispatcher;

        var surfaceArea = BuildSurfaceArea(out var surface, out var viewer, out var minimap);
        _surface = surface;
        _surfaceViewer = viewer;
        _minimap = minimap;

        var activityBar = BuildActivityBar();
        var sidebar = BuildSidebar();
        var mainArea = BuildMainArea(surfaceArea);
        var inspector = BuildInspector();

        var leftSplitter = new GridSplitter
        {
            Width = 4,
            Background = CardPalette.DividerBrush,
            ResizeDirection = GridResizeDirection.Columns,
        };
        var rightSplitter = new GridSplitter
        {
            Width = 4,
            Background = CardPalette.DividerBrush,
            ResizeDirection = GridResizeDirection.Columns,
        };

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.FromPixels(48) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.FromPixels(272) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.FromPixels(4) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.FromPixels(4) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.FromPixels(348) });

        Grid.SetColumn(activityBar, 0);
        Grid.SetColumn(sidebar, 1);
        Grid.SetColumn(leftSplitter, 2);
        Grid.SetColumn(mainArea, 3);
        Grid.SetColumn(rightSplitter, 4);
        Grid.SetColumn(inspector, 5);
        body.Children.Add(activityBar);
        body.Children.Add(sidebar);
        body.Children.Add(leftSplitter);
        body.Children.Add(mainArea);
        body.Children.Add(rightSplitter);
        body.Children.Add(inspector);

        var titleBar = BuildTitleBar();
        var statusBar = BuildStatusBar();
        var outer = new Grid();
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.FromPixels(32) });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.FromPixels(24) });
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(body, 1);
        Grid.SetRow(statusBar, 2);
        outer.Children.Add(titleBar);
        outer.Children.Add(body);
        outer.Children.Add(statusBar);
        Content = outer;

        LoadNetworkDemo();
    }

    // ── Title / status strips ───────────────────────────────────────────────

    private static FrameworkElement BuildTitleBar()
    {
        var brand = NodeChrome.Text("VeloxDev", 12, CardPalette.TitleBrush, FontWeights.Bold);
        brand.VerticalAlignment = VerticalAlignment.Center;
        var subtitle = NodeChrome.Text("Workflow Studio", 12, CardPalette.ValueBrush, FontWeights.Normal);
        subtitle.Margin = new Thickness(8, 0, 0, 0);
        subtitle.VerticalAlignment = VerticalAlignment.Center;
        var note = NodeChrome.Text("Jalium", 11, CardPalette.LabelBrush, FontWeights.Normal);
        note.HorizontalAlignment = HorizontalAlignment.Right;
        note.VerticalAlignment = VerticalAlignment.Center;

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(brand);
        panel.Children.Add(subtitle);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(panel);
        Grid.SetColumn(note, 1);
        row.Children.Add(note);

        return new Border
        {
            Background = CardPalette.SurfaceBrush,
            BorderBrush = CardPalette.DividerBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 0, 12, 0),
            Child = row,
        };
    }

    private FrameworkElement BuildStatusBar()
    {
        _statusDot.Width = 6;
        _statusDot.Height = 6;
        _statusDot.CornerRadius = new CornerRadius(3);
        _statusDot.VerticalAlignment = VerticalAlignment.Center;
        _statusDot.Background = CardPalette.ExecOrderBrush;

        _statusRun.FontSize = 11;
        _statusRun.VerticalAlignment = VerticalAlignment.Center;
        _statusCompiled.FontSize = 11;
        _statusCompiled.Foreground = CardPalette.ValueBrush;
        _statusCompiled.VerticalAlignment = VerticalAlignment.Center;
        _statusElements.FontSize = 11;
        _statusElements.Foreground = CardPalette.ValueBrush;
        _statusElements.VerticalAlignment = VerticalAlignment.Center;
        _statusVisible.FontSize = 11;
        _statusVisible.Foreground = CardPalette.ValueBrush;
        _statusVisible.VerticalAlignment = VerticalAlignment.Center;
        _statusZoom.FontSize = 11;
        _statusZoom.Foreground = CardPalette.ValueBrush;
        _statusZoom.VerticalAlignment = VerticalAlignment.Center;

        var runGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        runGroup.Children.Add(_statusDot);
        runGroup.Children.Add(_statusRun);

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(runGroup);
        left.Children.Add(_statusCompiled);
        left.Children.Add(_statusElements);

        var hint = NodeChrome.Text("Ctrl+滚轮缩放", 11, CardPalette.LabelBrush, FontWeights.Normal);
        hint.VerticalAlignment = VerticalAlignment.Center;
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(_statusVisible);
        right.Children.Add(_statusZoom);
        right.Children.Add(hint);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(left);
        Grid.SetColumn(right, 2);
        row.Children.Add(right);

        return new Border
        {
            Background = CardPalette.SurfaceBrush,
            BorderBrush = CardPalette.DividerBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 0, 10, 0),
            Child = row,
        };
    }

    // ── Surface (the trimmed demo's NodeEditorSurface composition) ─────────

    private static FrameworkElement BuildSurfaceArea(out NodeEditorSurface surface, out ScrollViewer viewer, out Minimap minimap)
    {
        var surfaceView = new NodeEditorSurface();
        var viewerControl = new ScrollViewer
        {
            Content = surfaceView,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            PanningMode = PanningMode.None, // surface handles mouse-pan itself
        };
        surfaceView.AttachScrollViewer(viewerControl);

        var minimapOverlay = new Minimap(viewerControl)
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        // 画布信息 HUD：贴左下、不吃指针；滚动 / 缩放 / 拖动都由下面这一处喂给它。
        var info = new InfoOverlay
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(16, 0, 0, 18),
        };

        void RefreshOverlays()
        {
            minimapOverlay.WorkflowTree = surfaceView.Tree;
            minimapOverlay.ContentOffsetX = surfaceView.OriginX;
            minimapOverlay.ContentOffsetY = surfaceView.OriginY;
            minimapOverlay.ScrollOffsetX = viewerControl.HorizontalOffset;
            minimapOverlay.ScrollOffsetY = viewerControl.VerticalOffset;
            minimapOverlay.ViewportWidth = viewerControl.ViewportWidth;
            minimapOverlay.ViewportHeight = viewerControl.ViewportHeight;

            // 浮层铺满表面，小地图自己落在内容区右上角：右侧让开竖滚动条，顶部让开表面画的水平
            // 刻度带，底部让开横滚动条。厚度取 ScrollViewer 的 Actual − Viewport（内容区不含滚动条）。
            var verticalBar = viewerControl.ComputedVerticalScrollBarVisibility == Visibility.Visible
                ? Math.Max(0d, viewerControl.ActualWidth - viewerControl.ViewportWidth)
                : 0d;
            var horizontalBar = viewerControl.ComputedHorizontalScrollBarVisibility == Visibility.Visible
                ? Math.Max(0d, viewerControl.ActualHeight - viewerControl.ViewportHeight)
                : 0d;
            var overlayWidth = minimapOverlay.ActualWidth > 0 ? minimapOverlay.ActualWidth : viewerControl.ActualWidth;
            var overlayHeight = minimapOverlay.ActualHeight > 0 ? minimapOverlay.ActualHeight : viewerControl.ActualHeight;
            minimapOverlay.AvailableRight = Math.Max(0d, overlayWidth - verticalBar);
            minimapOverlay.AvailableTop = NodeEditorSurface.RulerThickness;
            minimapOverlay.AvailableBottom = Math.Max(0d, overlayHeight - horizontalBar);

            info.WorkflowTree = surfaceView.Tree;
            info.ContentOffsetX = surfaceView.OriginX;
            info.ContentOffsetY = surfaceView.OriginY;
            info.ScrollOffsetX = viewerControl.HorizontalOffset;
            info.ScrollOffsetY = viewerControl.VerticalOffset;
            info.ViewportWidth = viewerControl.ViewportWidth;
            info.ViewportHeight = viewerControl.ViewportHeight;
        }

        // SizeChanged 兜住 Jalium 可能不把首次测量报成滚动。
        viewerControl.ScrollChanged += (_, _) => RefreshOverlays();
        viewerControl.SizeChanged += (_, _) => RefreshOverlays();
        surfaceView.Changed += RefreshOverlays;

        var root = new Grid();
        root.Children.Add(viewerControl);
        root.Children.Add(minimapOverlay);
        root.Children.Add(info);

        // 首次布局前也先把刻度带让位定下来；尺寸类字段拿不到时由浮层自己按 Actual 回退。
        RefreshOverlays();

        surface = surfaceView;
        viewer = viewerControl;
        minimap = minimapOverlay;
        return root;
    }

    // ── Activity bar / sidebar ──────────────────────────────────────────────

    private FrameworkElement BuildActivityBar()
    {
        var activities = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        activities.Children.Add(BuildActivityButton("explorer", RailIconKind.Explorer, "资源管理器"));
        activities.Children.Add(BuildActivityButton("run", RailIconKind.Run, "运行"));
        activities.Children.Add(BuildActivityButton("mcp", RailIconKind.Mcp, "MCP 服务器"));

        return new Border
        {
            Background = CardPalette.CodeSurfaceBrush,
            BorderBrush = CardPalette.DividerBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = activities,
        };
    }

    // 活动栏的一项：按钮管点击与悬停底，左侧 2px 条只表示当前页。
    private FrameworkElement BuildActivityButton(string tag, RailIconKind kind, string tooltip)
    {
        var label = new RailIcon
        {
            Kind = kind,
            Width = 20,
            Height = 20,
            Foreground = CardPalette.LabelBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var button = new Button
        {
            Width = 48,
            Height = 48,
            // Jalium 的 Button 默认样式带 MinWidth/MinHeight；不压成 0，活动栏就会压进侧栏。
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            Background = CardPalette.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = label,
            ToolTip = tooltip,
            Cursor = Cursors.Hand,
        };
        var strip = new Border
        {
            Width = 2,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = CardPalette.Transparent,
            IsHitTestVisible = false,
        };

        _activityItems.Add(new ActivityItem(tag, button, strip, label));
        button.Click += (_, _) => ShowActivity(tag);
        button.MouseEnter += (_, _) =>
        {
            if (_activeActivity == tag) return;
            button.Background = CardPalette.HoverBrush;
            label.Foreground = CardPalette.TitleBrush;
        };
        button.MouseLeave += (_, _) =>
        {
            if (_activeActivity == tag) return;
            button.Background = CardPalette.Transparent;
            label.Foreground = CardPalette.LabelBrush;
        };

        var host = new Grid();
        host.Children.Add(button);
        host.Children.Add(strip);
        return host;
    }

    private FrameworkElement BuildSidebar()
    {
        _sidebarHeader = new TextBlock
        {
            Text = "资源管理器",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = CardPalette.TitleBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0),
        };

        _explorerPage = BuildExplorerPage();
        _runPage = BuildRunPage();
        _mcpPage = BuildMcpPage();

        var pages = new Grid();
        pages.Children.Add(_explorerPage);
        pages.Children.Add(_runPage);
        pages.Children.Add(_mcpPage);

        var headerHost = new Border
        {
            Height = 34,
            Background = CardPalette.SurfaceBrush,
            BorderBrush = CardPalette.DividerBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = _sidebarHeader,
        };

        var sidebarBody = new Grid();
        sidebarBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sidebarBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        Grid.SetRow(headerHost, 0);
        Grid.SetRow(pages, 1);
        sidebarBody.Children.Add(headerHost);
        sidebarBody.Children.Add(pages);

        ShowActivity("explorer");

        return new Border
        {
            Width = 272,
            Background = CardPalette.SurfaceBrush,
            Child = sidebarBody,
        };
    }

    // 切页：三页只亮一个，标题与活动项跟着走。
    private void ShowActivity(string view)
    {
        _activeActivity = view;

        _explorerPage.Visibility = view == "explorer" ? Visibility.Visible : Visibility.Collapsed;
        _runPage.Visibility = view == "run" ? Visibility.Visible : Visibility.Collapsed;
        _mcpPage.Visibility = view == "mcp" ? Visibility.Visible : Visibility.Collapsed;

        _sidebarHeader.Text = view switch
        {
            "run" => "运行",
            "mcp" => "MCP 服务器",
            _ => "资源管理器",
        };

        foreach (var item in _activityItems)
        {
            bool active = item.Tag == view;
            item.Strip.Background = active ? CardPalette.Of(CardPalette.AccentTimerPython) : CardPalette.Transparent;
            item.Glyph.Foreground = active ? CardPalette.TitleBrush : CardPalette.LabelBrush;
            item.Button.Background = CardPalette.Transparent;
        }
    }

    // 活动栏的一项：标签既切页，也用来判断悬停时该不该改色。
    private sealed class ActivityItem
    {
        public ActivityItem(string tag, Button button, Border strip, RailIcon glyph)
        {
            Tag = tag;
            Button = button;
            Strip = strip;
            Glyph = glyph;
        }

        public string Tag { get; }
        public Button Button { get; }
        public Border Strip { get; }
        public RailIcon Glyph { get; }
    }

    // ── Explorer ────────────────────────────────────────────────────────────

    private FrameworkElement BuildExplorerPage()
    {
        var body = new StackPanel();

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 6, 10, 0) };
        var save = SidebarIconButton(RailIconKind.Save, "保存工作流");
        save.Click += (_, _) => SaveWorkflow();
        var select = SidebarIconButton(RailIconKind.Open, "加载工作流");
        select.Click += (_, _) => _ = SelectWorkflowAsync();
        var undo = SidebarIconButton(RailIconKind.Undo, "撤销");
        undo.Click += (_, _) => _tree.UndoCommand.Execute(null);
        var redo = SidebarIconButton(RailIconKind.Redo, "重做");
        redo.Click += (_, _) => _tree.RedoCommand.Execute(null);
        var reload = SidebarIconButton(RailIconKind.Reload, "重新载入示例");
        reload.Click += (_, _) => LoadNetworkDemo();
        actions.Children.Add(save);
        actions.Children.Add(select);
        actions.Children.Add(undo);
        actions.Children.Add(redo);
        actions.Children.Add(reload);
        body.Children.Add(actions);

        var minimapRow = new Grid { Margin = new Thickness(12, 12, 12, 0) };
        minimapRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        minimapRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var minimapLabel = NodeChrome.Text("小地图", 11, CardPalette.ValueBrush, FontWeights.Normal);
        minimapLabel.VerticalAlignment = VerticalAlignment.Center;
        _minimapToggle = new CheckBox
        {
            IsChecked = true,
            Foreground = CardPalette.ValueBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _minimapToggle.Checked += (_, _) => _minimap.Visibility = Visibility.Visible;
        _minimapToggle.Unchecked += (_, _) => _minimap.Visibility = Visibility.Collapsed;
        minimapRow.Children.Add(minimapLabel);
        Grid.SetColumn(_minimapToggle, 1);
        minimapRow.Children.Add(_minimapToggle);
        body.Children.Add(minimapRow);

        body.Children.Add(SectionLabel("元素"));

        var listHost = new Border
        {
            Margin = new Thickness(12, 0, 12, 0),
            Padding = new Thickness(4),
            Background = CardPalette.FieldBrush,
            BorderBrush = CardPalette.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(CardPalette.FieldRadius),
            Child = BuildElementListPanel(),
        };
        body.Children.Add(listHost);

        _elementStatus.Foreground = CardPalette.LabelBrush;
        _elementStatus.FontSize = 11;
        _elementStatus.TextWrapping = TextWrapping.Wrap;
        _elementStatus.Margin = new Thickness(12, 8, 12, 12);
        body.Children.Add(_elementStatus);

        _elementCatalog.Changed += (_, _) => OnElementCatalogChanged();
        UpdateElementStatus();

        return new ScrollViewer
        {
            Background = CardPalette.Transparent,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = body,
        };
    }

    private FrameworkElement BuildElementListPanel()
    {
        var tabs = new TabControl
        {
            Height = 220,
            Background = CardPalette.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var rowTemplate = BuildElementRowTemplate();
        tabs.Items.Add(BuildElementTab("节点", _elementCatalog.Nodes, rowTemplate));
        tabs.Items.Add(BuildElementTab("插槽", _elementCatalog.Slots, rowTemplate));
        tabs.Items.Add(BuildElementTab("连线", _elementCatalog.Links, rowTemplate));
        tabs.SelectedIndex = 0;
        return tabs;
    }

    private TabItem BuildElementTab(string header, ObservableCollection<WorkflowElementEntry> items, DataTemplate rowTemplate)
    {
        var list = new ListBox
        {
            ItemsSource = items,
            ItemTemplate = rowTemplate,
            Background = CardPalette.Transparent,
            BorderThickness = new Thickness(0),
            MaxHeight = 190,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };

        // 选中的行就是右侧检查器描述的对象；同刻只有一张表能持有选中。
        list.SelectionChanged += (_, _) => OnElementListSelection(list);
        _elementLists.Add(list);

        return new TabItem
        {
            Header = header,
            Content = list,
            Foreground = CardPalette.ValueBrush,
            IndicatorBrush = CardPalette.Of(CardPalette.AccentTimerPython),
            SelectedBackground = CardPalette.FieldBrush,
            HoverBackground = CardPalette.HoverBrush,
        };
    }

    private DataTemplate BuildElementRowTemplate()
    {
        var template = new DataTemplate(typeof(WorkflowElementEntry));
        template.SetVisualTree(() =>
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Foreground = CardPalette.TitleBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            label.SetBinding(TextBlock.TextProperty, new Binding(nameof(WorkflowElementEntry.Label)));

            var locate = ElementRowButton("Locate");
            locate.ToolTip = "滚到视口中心；插槽定位所属节点，连线定位两端节点中心";
            locate.Click += (sender, _) =>
            {
                if ((sender as FrameworkElement)?.DataContext is WorkflowElementEntry entry)
                    LocateElement(entry);
            };

            var delete = ElementRowButton("Delete");
            delete.ToolTip = "删除该元素；插槽与节点会一并删除它们的连线";
            delete.Click += (sender, _) =>
            {
                if ((sender as FrameworkElement)?.DataContext is WorkflowElementEntry entry)
                    DeleteElement(entry);
            };

            Grid.SetColumn(locate, 1);
            Grid.SetColumn(delete, 2);
            row.Children.Add(label);
            row.Children.Add(locate);
            row.Children.Add(delete);
            return row;
        });

        return template;
    }

    private void OnElementListSelection(ListBox source)
    {
        if (_syncingElementSelection) return;

        _syncingElementSelection = true;
        try
        {
            foreach (var list in _elementLists)
            {
                if (!ReferenceEquals(list, source)) list.UnselectAll();
            }

            _tree.SelectedElement = source.SelectedItem as WorkflowElementEntry;
        }
        finally
        {
            _syncingElementSelection = false;
        }

        RefreshInspectorSelection();
    }

    // 三张表都是重建式的：换完表原有行对象已不在任何一张表里，选中与描述要一起落回。
    private void OnElementCatalogChanged()
    {
        _syncingElementSelection = true;
        try
        {
            foreach (var list in _elementLists) list.UnselectAll();
        }
        finally
        {
            _syncingElementSelection = false;
        }

        UpdateElementStatus();
        RefreshInspectorSelection();
        UpdateStatusBar();
    }

    private void LocateElement(WorkflowElementEntry entry)
    {
        _lastElementAction = _elementCatalog.Locate(entry)
            ? $"已定位 {entry.Label}"
            : "没有可用的表面，或该元素还没有有限坐标";
        UpdateElementStatus();
    }

    private void DeleteElement(WorkflowElementEntry entry)
    {
        _elementCatalog.Delete(entry);
        if (ReferenceEquals(_tree.SelectedElement, entry)) _tree.SelectedElement = null;

        _syncingElementSelection = true;
        try
        {
            foreach (var list in _elementLists) list.UnselectAll();
        }
        finally
        {
            _syncingElementSelection = false;
        }

        _lastElementAction = $"已删除 {entry.Label}";
        UpdateElementStatus();
        RefreshInspectorSelection();
    }

    private void UpdateElementStatus() => _elementStatus.Text = $"{_elementCatalog.Summary} · {_lastElementAction}";

    private static Button ElementRowButton(string text) => new()
    {
        Content = text,
        Margin = new Thickness(4, 0, 0, 0),
        Background = CardPalette.Transparent,
        Foreground = CardPalette.Of(CardPalette.AccentTimerPython),
        BorderBrush = CardPalette.BorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(CardPalette.FieldRadius),
        Padding = new Thickness(6, 1, 6, 1),
        FontSize = 11,
        MinWidth = 0,
        MinHeight = 0,
    };

    // ── Run manager ─────────────────────────────────────────────────────────

    private FrameworkElement BuildRunPage()
    {
        var body = new StackPanel();

        var header = new Grid { Margin = new Thickness(12, 12, 12, 6) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel { Spacing = 2 };
        titles.Children.Add(NodeChrome.Text("运行任务", 12, CardPalette.TitleBrush, FontWeights.SemiBold));
        _runSummary.FontSize = 10.5;
        _runSummary.Foreground = CardPalette.LabelBrush;
        titles.Children.Add(_runSummary);
        var clear = RunRowButton("清除已完成", CardPalette.AccentTimerPython, visible: true, () => _tree.Runs.ClearFinished());
        clear.Margin = new Thickness(0);
        clear.Padding = new Thickness(8, 3);
        Grid.SetColumn(clear, 1);
        header.Children.Add(titles);
        header.Children.Add(clear);
        body.Children.Add(header);

        var description = new TextBlock
        {
            Text = "手动编译、手动运行，以及 Agent 编译 / 运行的任务都会出现在这里。选中一行，输出页会跟随它。",
            Foreground = CardPalette.LabelBrush,
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 0, 12, 8),
        };
        body.Children.Add(description);

        _runRows.Margin = new Thickness(8, 0, 8, 0);
        body.Children.Add(_runRows);

        _runEmptyHint.Text = "还没有任务。在控制器卡上编译一次，或让 Agent 编译 / 运行工作流。";
        _runEmptyHint.Foreground = CardPalette.LabelBrush;
        _runEmptyHint.FontSize = 10.5;
        _runEmptyHint.TextWrapping = TextWrapping.Wrap;
        _runEmptyHint.Margin = new Thickness(12, 10, 12, 12);
        body.Children.Add(_runEmptyHint);

        return new ScrollViewer
        {
            Background = CardPalette.Transparent,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = body,
        };
    }

    private void RebuildRunRows()
    {
        _runSummary.Text = _tree.Runs.Summary;
        _runEmptyHint.Visibility = _tree.Runs.IsEmpty ? Visibility.Visible : Visibility.Collapsed;

        _runRows.Children.Clear();
        foreach (var task in _tree.Runs.Tasks)
        {
            _runRows.Children.Add(BuildRunRow(task));
        }
    }

    private FrameworkElement BuildRunRow(WorkflowRunTaskViewModel task)
    {
        bool selected = ReferenceEquals(_tree.Runs.Selected, task);
        var card = new Border
        {
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(CardPalette.FieldRadius),
            BorderThickness = new Thickness(1),
            Background = selected ? CardPalette.HoverBrush : CardPalette.FieldBrush,
            BorderBrush = selected
                ? CardPalette.Of(CardPalette.AccentTimerPython)
                : CardPalette.BorderBrush,
            Cursor = Cursors.Hand,
        };
        card.MouseDown += (_, e) =>
        {
            if (e.Handled) return;
            _tree.Runs.Selected = task;
            ScheduleRefresh();
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 左列：来源徽标 + 根节点，随后是状态 / 起止 / 进度。
        var details = new StackPanel { Spacing = 3 };

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var source = new Border
        {
            Background = CardPalette.CodeSurfaceBrush,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = NodeChrome.Text(task.SourceText, 10, CardPalette.LabelBrush, FontWeights.Normal),
        };
        var root = NodeChrome.Text(task.RootLabel, 12, CardPalette.TitleBrush, FontWeights.SemiBold);
        root.TextTrimming = TextTrimming.CharacterEllipsis;
        titleRow.Children.Add(source);
        titleRow.Children.Add(root);
        details.Children.Add(titleRow);

        var timing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        timing.Children.Add(NodeChrome.Text(task.StateText, 11, CardPalette.Of(RunStateColor(task)), FontWeights.Normal));
        timing.Children.Add(NodeChrome.Text(task.StartedText, 10, CardPalette.LabelBrush, FontWeights.Normal));
        timing.Children.Add(NodeChrome.Text(task.ElapsedText, 10, CardPalette.LabelBrush, FontWeights.Normal));
        details.Children.Add(timing);

        var progress = NodeChrome.Text(task.ProgressText, 10, CardPalette.LabelBrush, FontWeights.Normal);
        progress.TextWrapping = TextWrapping.Wrap;
        details.Children.Add(progress);

        grid.Children.Add(details);

        // 右列：Can* 决定哪几颗按钮出现，所以整列随行一起重建。
        var actions = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Top };
        actions.Children.Add(RunRowButton("暂停", CardPalette.AccentTimerPython, task.CanPause, task.Pause));
        actions.Children.Add(RunRowButton("继续", CardPalette.AccentTimerPython, task.CanResume, task.Resume));
        actions.Children.Add(RunRowButton("停止", CardPalette.GhostStopText, task.CanStop, task.Stop));
        actions.Children.Add(RunRowButton("运行", CardPalette.ExecOrderText, task.CanStart, task.Start));
        actions.Children.Add(RunRowButton("移除", CardPalette.ValueText, task.CanRemove, () => _tree.Runs.Remove(task)));
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);

        card.Child = grid;
        return card;
    }

    private static Color RunStateColor(WorkflowRunTaskViewModel task) => task.State switch
    {
        WorkflowRunState.Running => CardPalette.AccentTimerPython,
        WorkflowRunState.Completed => CardPalette.ExecOrderText,
        WorkflowRunState.Failed => CardPalette.GhostStopText,
        WorkflowRunState.Cancelled => CardPalette.LabelText,
        _ => CardPalette.ValueText,
    };

    // ── MCP ─────────────────────────────────────────────────────────────────

    private FrameworkElement BuildMcpPage()
    {
        var body = new StackPanel();
        body.Children.Add(SectionLabel("服务器"));
        var panel = BuildMcpPanel();
        panel.Margin = new Thickness(12, 0, 12, 12);
        body.Children.Add(panel);

        return new ScrollViewer
        {
            Background = CardPalette.Transparent,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = body,
        };
    }

    private FrameworkElement BuildMcpPanel()
    {
        var body = new StackPanel { Spacing = 6 };

        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var header = NodeChrome.Text("MCP 服务器", 12, CardPalette.Of(CardPalette.AccentTimerPython), FontWeights.Bold);
        header.VerticalAlignment = VerticalAlignment.Center;
        _mcpSummary.Foreground = CardPalette.ValueBrush;
        _mcpSummary.FontSize = 10.5;
        _mcpSummary.VerticalAlignment = VerticalAlignment.Center;

        var reloadBtn = RunRowButton("重载", CardPalette.AccentTimerPython, visible: true, () =>
        {
            if (_tree.GetHelper() is AgentHelper helper) _ = helper.LoadMcpServersAsync();
        });
        reloadBtn.HorizontalAlignment = HorizontalAlignment.Right;
        reloadBtn.Padding = new Thickness(8, 2);
        reloadBtn.Margin = new Thickness(6, 0, 0, 0);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(_mcpSummary);
        right.Children.Add(reloadBtn);
        Grid.SetColumn(header, 0);
        Grid.SetColumn(right, 1);
        headerRow.Children.Add(header);
        headerRow.Children.Add(right);
        body.Children.Add(headerRow);

        _mcpServers.Margin = new Thickness(0, 2, 0, 0);
        _mcpServers.Spacing = 3;
        body.Children.Add(_mcpServers);

        return Section("", body);
    }

    private readonly StackPanel _mcpServers = new();

    // ── Main area: canvas / output ──────────────────────────────────────────

    private FrameworkElement BuildMainArea(FrameworkElement surfaceArea)
    {
        var output = BuildOutputPanel();

        _mainTabs = new TabControl
        {
            Background = CardPalette.CodeSurfaceBrush,
            BorderThickness = new Thickness(0),
            TabStripBackground = CardPalette.SurfaceBrush,
            TabStripBorderBrush = CardPalette.DividerBrush,
            TabStripHeight = 34,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        _mainTabs.Items.Add(MakeTab("流程图", surfaceArea));
        _mainTabs.Items.Add(MakeTab("输出", output));
        _mainTabs.SelectedIndex = 0;
        _mainTabs.SelectionChanged += (_, _) =>
        {
            ApplyTabState(_mainTabs);
            if (_mainTabs.SelectedIndex == 1) ScrollOutputToEnd();
        };
        ApplyTabState(_mainTabs);
        return _mainTabs;
    }

    private FrameworkElement BuildOutputPanel()
    {
        _outputRoot.FontSize = 12;
        _outputRoot.Foreground = CardPalette.Of(CardPalette.AccentTimerPython);
        _outputRoot.VerticalAlignment = VerticalAlignment.Center;
        _outputRoot.TextTrimming = TextTrimming.CharacterEllipsis;

        _outputState.FontSize = 11;
        _outputState.VerticalAlignment = VerticalAlignment.Center;
        _outputState.Foreground = CardPalette.ValueBrush;

        _outputProgress.FontSize = 10.5;
        _outputProgress.VerticalAlignment = VerticalAlignment.Center;
        _outputProgress.Foreground = CardPalette.LabelBrush;

        var title = NodeChrome.Text("执行输出", 12, CardPalette.TitleBrush, FontWeights.Bold);
        title.VerticalAlignment = VerticalAlignment.Center;

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(title);
        left.Children.Add(_outputRoot);
        left.Children.Add(_outputState);
        left.Children.Add(_outputProgress);

        _outputCount.FontSize = 10.5;
        _outputCount.Foreground = CardPalette.LabelBrush;
        _outputCount.VerticalAlignment = VerticalAlignment.Center;
        _outputCount.HorizontalAlignment = HorizontalAlignment.Right;

        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerRow.Children.Add(left);
        Grid.SetColumn(_outputCount, 1);
        headerRow.Children.Add(_outputCount);

        var header = new Border
        {
            Background = CardPalette.SurfaceBrush,
            BorderBrush = CardPalette.DividerBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8, 12, 8),
            Child = headerRow,
        };

        _outputHint.Text = "运行一次，或让 Agent 编译 / 运行工作流。节点开始、完成、重试、重定向和运行收尾都会按发生顺序落在这里。";
        _outputHint.Foreground = CardPalette.LabelBrush;
        _outputHint.FontSize = 10.5;
        _outputHint.TextWrapping = TextWrapping.Wrap;
        _outputHint.Margin = new Thickness(12, 8, 12, 0);

        _executionLog.Background = CardPalette.Transparent;
        _executionLog.BorderThickness = new Thickness(0);
        _executionLog.Foreground = CardPalette.TitleBrush;
        _executionLog.FontFamily = new FontFamily(CardPalette.MonoFamily);
        _executionLog.FontSize = 11.5;
        _executionLog.Margin = new Thickness(8, 8, 8, 8);

        var page = new Grid();
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        page.Children.Add(header);
        Grid.SetRow(_outputHint, 1);
        page.Children.Add(_outputHint);
        Grid.SetRow(_executionLog, 2);
        page.Children.Add(_executionLog);
        return page;
    }

    private void UpdateOutputHeader()
    {
        var task = _tree.Runs.Selected;
        bool has = task is not null;

        _outputRoot.Text = task?.RootLabel ?? string.Empty;
        _outputState.Text = task?.StateText ?? string.Empty;
        _outputProgress.Text = task?.ProgressText ?? string.Empty;
        _outputState.Foreground = task is null ? CardPalette.ValueBrush : CardPalette.Of(RunStateColor(task));

        _outputRoot.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        _outputState.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        _outputProgress.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        _outputHint.Visibility = has ? Visibility.Collapsed : Visibility.Visible;

        _outputCount.Text = $"已记录 {_tree.ExecutionLog.Count} 行";
    }

    private void ScrollOutputToEnd()
    {
        // 输出页在另一个标签里：没被选中时列表还没实现，滚它没有意义，也可能碰到未生成的容器。
        if (_mainTabs is null || _mainTabs.SelectedIndex != 1) return;
        if (_executionLog.Items.Count == 0) return;

        _executionLog.ScrollIntoView(_executionLog.Items[_executionLog.Items.Count - 1]);
    }

    // ── Right inspector ─────────────────────────────────────────────────────

    private FrameworkElement BuildInspector()
    {
        _inspectorTabs = new TabControl
        {
            Background = CardPalette.SurfaceBrush,
            BorderThickness = new Thickness(0),
            TabStripBackground = CardPalette.SurfaceBrush,
            TabStripBorderBrush = CardPalette.DividerBrush,
            TabStripHeight = 34,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        _inspectorTabs.Items.Add(MakeTab("属性", BuildPropertiesPanel()));
        _inspectorTabs.Items.Add(MakeTab("AI 助手", BuildAgentPanel()));
        _inspectorTabs.SelectedIndex = 0;
        _inspectorTabs.SelectionChanged += (_, _) => ApplyTabState(_inspectorTabs);
        ApplyTabState(_inspectorTabs);

        return new Border
        {
            Width = 348,
            Background = CardPalette.SurfaceBrush,
            BorderBrush = CardPalette.DividerBrush,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = _inspectorTabs,
        };
    }

    private FrameworkElement BuildPropertiesPanel()
    {
        var body = new StackPanel();
        body.Children.Add(SectionLabel("选中元素"));

        _propLabel.Foreground = CardPalette.TitleBrush;
        _propLabel.FontSize = 12;
        _propLabel.FontWeight = FontWeights.SemiBold;
        _propLabel.TextWrapping = TextWrapping.Wrap;
        _propLabel.VerticalAlignment = VerticalAlignment.Center;

        _propKind.Foreground = CardPalette.ValueBrush;
        _propKind.FontSize = 11;
        _propKind.VerticalAlignment = VerticalAlignment.Center;
        _propKind.Margin = new Thickness(8, 0, 0, 0);

        _propDetail.Foreground = CardPalette.ValueBrush;
        _propDetail.FontFamily = new FontFamily(CardPalette.MonoFamily);
        _propDetail.FontSize = 11;
        _propDetail.TextWrapping = TextWrapping.Wrap;

        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleRow.Children.Add(_propLabel);
        Grid.SetColumn(_propKind, 1);
        titleRow.Children.Add(_propKind);

        var locate = NodeChrome.Ghost("定位", CardPalette.AccentTimerPython);
        locate.FontSize = 11;
        locate.Padding = new Thickness(6, 2);
        locate.Click += (_, _) => LocateSelectedElement();
        var delete = NodeChrome.Ghost("删除", CardPalette.GhostStopText);
        delete.FontSize = 11;
        delete.Padding = new Thickness(6, 2);
        delete.Click += (_, _) => DeleteSelectedElement();

        var buttonRow = new Grid { ColumnSpacing = 6 };
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        buttonRow.Children.Add(locate);
        Grid.SetColumn(delete, 1);
        buttonRow.Children.Add(delete);

        _propSelected.Children.Add(titleRow);
        _propSelected.Children.Add(NodeChrome.Divider());
        _propSelected.Children.Add(_propDetail);
        _propSelected.Children.Add(buttonRow);

        _propEmpty.Text = "在左侧「元素」列表里选一行，这里显示它的类型、位置、编译顺序与连接。";
        _propEmpty.Foreground = CardPalette.LabelBrush;
        _propEmpty.FontSize = 10.5;
        _propEmpty.TextWrapping = TextWrapping.Wrap;

        var selectionHost = new Grid();
        selectionHost.Children.Add(_propSelected);
        selectionHost.Children.Add(_propEmpty);

        body.Children.Add(Card(selectionHost));

        body.Children.Add(SectionLabel("编译结构"));

        var compiledHeader = new Grid();
        compiledHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        compiledHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var compiledLabel = NodeChrome.Text("分段", 11, CardPalette.ValueBrush, FontWeights.Normal);
        compiledLabel.VerticalAlignment = VerticalAlignment.Center;
        _compiledCount.FontSize = 10.5;
        _compiledCount.Foreground = CardPalette.LabelBrush;
        _compiledCount.VerticalAlignment = VerticalAlignment.Center;
        compiledHeader.Children.Add(compiledLabel);
        Grid.SetColumn(_compiledCount, 1);
        compiledHeader.Children.Add(_compiledCount);

        _compiledEmpty.Text = "编译一次后这里列出实际执行段。";
        _compiledEmpty.Foreground = CardPalette.LabelBrush;
        _compiledEmpty.FontSize = 10.5;
        _compiledEmpty.TextWrapping = TextWrapping.Wrap;

        var compiledBody = new StackPanel { Spacing = 8 };
        compiledBody.Children.Add(compiledHeader);
        compiledBody.Children.Add(_compiledRows);
        compiledBody.Children.Add(_compiledEmpty);
        body.Children.Add(Card(compiledBody));

        var page = new StackPanel();
        page.Children.Add(body);

        return new ScrollViewer
        {
            Background = CardPalette.Transparent,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = page,
        };
    }

    private void RefreshInspectorSelection()
    {
        var entry = _tree.SelectedElement;

        // DetailText 是活读的：编译写入序号、运行写入状态，都只通过这一行的 PropertyChanged 报出来。
        if (!ReferenceEquals(_watchedEntry, entry))
        {
            if (_watchedEntry is not null) _watchedEntry.PropertyChanged -= OnSelectedEntryChanged;
            _watchedEntry = entry;
            if (entry is not null) entry.PropertyChanged += OnSelectedEntryChanged;
        }

        bool has = entry is not null;
        _propSelected.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        _propEmpty.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        if (entry is null) return;

        _propLabel.Text = entry.Label;
        _propKind.Text = entry.KindText;
        _propDetail.Text = entry.DetailText;
    }

    private void OnSelectedEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not WorkflowElementEntry entry || !ReferenceEquals(entry, _tree.SelectedElement)) return;

        _propLabel.Text = entry.Label;
        _propDetail.Text = entry.DetailText;
    }

    private void LocateSelectedElement()
    {
        if (_tree.SelectedElement is not { } entry) return;
        LocateElement(entry);
    }

    private void DeleteSelectedElement()
    {
        if (_tree.SelectedElement is { } entry) DeleteElement(entry);
    }

    private void RebuildCompiledStructure()
    {
        _compiledRows.Children.Clear();
        foreach (var row in _tree.CompiledStructure)
        {
            var line = new Grid { Margin = new Thickness(row.Depth * 12, 0, 0, 0) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.FromPixels(58) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

            var kind = NodeChrome.Text(row.Kind, 11, CardPalette.Of(CardPalette.AccentEnum), FontWeights.Normal);
            var label = NodeChrome.Text(row.Label, 11, CardPalette.TitleBrush, FontWeights.Normal);
            label.TextWrapping = TextWrapping.Wrap;
            line.Children.Add(kind);
            Grid.SetColumn(label, 1);
            line.Children.Add(label);
            _compiledRows.Children.Add(line);
        }

        var count = _tree.CompiledStructure.Count;
        _compiledCount.Text = $"{count} 段";
        _compiledEmpty.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── AI assistant ────────────────────────────────────────────────────────

    private FrameworkElement BuildAgentPanel()
    {
        var page = new Grid();
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var session = new StackPanel();
        session.Children.Add(SectionLabel("会话"));
        var permission = BuildPermissionModePanel();
        permission.Margin = new Thickness(12, 0, 12, 4);
        session.Children.Add(permission);
        page.Children.Add(session);

        var chatHeader = BuildAgentChatHeader();
        chatHeader.Margin = new Thickness(12, 4, 12, 6);
        Grid.SetRow(chatHeader, 1);
        page.Children.Add(chatHeader);

        _agentLog.Background = CardPalette.Transparent;
        _agentLog.BorderThickness = new Thickness(0);
        _agentLog.Foreground = CardPalette.TitleBrush;
        _agentLog.FontSize = 11.5;
        _agentLog.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetRow(_agentLog, 2);
        page.Children.Add(_agentLog);

        var inputRow = BuildAgentInputRow();
        inputRow.Margin = new Thickness(12, 6, 12, 8);
        Grid.SetRow(inputRow, 3);
        page.Children.Add(inputRow);

        _subAgentPanel = BuildSubAgentPanel();
        Grid.SetRow(_subAgentPanel, 4);
        page.Children.Add(_subAgentPanel);

        return page;
    }

    private FrameworkElement BuildPermissionModePanel()
    {
        var row = new Grid { ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

        var label = NodeChrome.Text("权限模式", 11, CardPalette.ValueBrush, FontWeights.Normal);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(0, 0, 6, 0);

        _permissionModePicker = NodeChrome.Picker();
        _permissionModePicker.SelectionChanged += (_, _) => OnPermissionModeChanged();

        row.Children.Add(label);
        Grid.SetColumn(_permissionModePicker, 1);
        row.Children.Add(_permissionModePicker);
        return row;
    }

    private FrameworkElement BuildAgentChatHeader()
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var header = NodeChrome.Text("Agent 对话", 12, CardPalette.Of(CardPalette.AccentTimerPython), FontWeights.Bold);
        header.VerticalAlignment = VerticalAlignment.Center;

        _streamToggle = new CheckBox
        {
            Content = "流式",
            Foreground = CardPalette.ValueBrush,
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _streamToggle.Checked += (_, _) => _tree.UseStreamingAgentResponse = true;
        _streamToggle.Unchecked += (_, _) => _tree.UseStreamingAgentResponse = false;

        row.Children.Add(header);
        Grid.SetColumn(_streamToggle, 1);
        row.Children.Add(_streamToggle);
        return row;
    }

    private FrameworkElement BuildAgentInputRow()
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _agentInput.Background = CardPalette.FieldBrush;
        _agentInput.Foreground = CardPalette.TitleBrush;
        _agentInput.BorderBrush = CardPalette.BorderBrush;
        _agentInput.BorderThickness = new Thickness(1);
        _agentInput.CornerRadius = new CornerRadius(CardPalette.FieldRadius);
        _agentInput.Padding = new Thickness(8, 6);
        _agentInput.FontSize = 12;
        _agentInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                SendToAgent();
                e.Handled = true;
            }
        };

        var send = RunRowButton("发送", CardPalette.AccentTimerPython, visible: true, SendToAgent);
        send.Margin = new Thickness(6, 0, 0, 0);
        send.Padding = new Thickness(10, 5);

        row.Children.Add(_agentInput);
        Grid.SetColumn(send, 1);
        row.Children.Add(send);
        return row;
    }

    private FrameworkElement BuildSubAgentPanel()
    {
        _subAgentSummary.FontSize = 10;
        _subAgentSummary.Foreground = CardPalette.LabelBrush;
        _subAgentSummary.VerticalAlignment = VerticalAlignment.Center;

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = NodeChrome.Text("子代理", 12, CardPalette.Of(CardPalette.AccentTimerPython), FontWeights.Bold);
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);
        Grid.SetColumn(_subAgentSummary, 1);
        header.Children.Add(_subAgentSummary);

        _subAgentEmpty.Text = "尚无子代理。让 Agent「派一个子代理去数一数节点」试试。";
        _subAgentEmpty.Foreground = CardPalette.LabelBrush;
        _subAgentEmpty.FontSize = 10.5;
        _subAgentEmpty.TextWrapping = TextWrapping.Wrap;

        var scroller = new ScrollViewer
        {
            MaxHeight = 240,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _subAgentRows,
        };

        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(header);
        body.Children.Add(_subAgentEmpty);
        body.Children.Add(scroller);

        return new Border
        {
            Margin = new Thickness(12, 4, 12, 12),
            Padding = new Thickness(10),
            Background = CardPalette.FieldBrush,
            BorderBrush = CardPalette.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(CardPalette.FieldRadius),
            Child = body,
        };
    }

    private void AttachSubAgents(AgentHelper helper)
    {
        // 换树时先放下旧的：面板属于这一份视图，不跟着树跑。
        DetachSubAgents();

        if (helper.SubAgents is not { } scope)
        {
            _subAgentPanel.Visibility = Visibility.Collapsed;
            return;
        }

        // 必须建在 UI 线程上：树捕获的是它把重建投递回的那个上下文。
        _subAgentTree = new SubAgentTreeViewModel(scope);
        _subAgentPanel.Visibility = Visibility.Visible;
        StartSubAgentTick();
        RebuildSubAgentRows();
    }

    // 时钟由宿主提供 —— 库有意不持有定时器。一秒足够让「分/秒」看起来是活的。
    private void StartSubAgentTick()
    {
        _subAgentTick ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _subAgentTick.Tick -= OnSubAgentTick;
        _subAgentTick.Tick += OnSubAgentTick;
        _subAgentTick.Start();
    }

    private void OnSubAgentTick(object? sender, EventArgs e)
    {
        _subAgentTree?.TickElapsed();
        RebuildSubAgentRows();
    }

    private void DetachSubAgents()
    {
        if (_subAgentTick is not null)
        {
            _subAgentTick.Stop();
            _subAgentTick.Tick -= OnSubAgentTick;
        }

        // 只退订：它显示的那些子代理仍在自己跑，收起面板不是对它们的决定。
        _subAgentTree?.Dispose();
        _subAgentTree = null;
        _subAgentRows.Children.Clear();

        if (_subAgentPanel is not null) _subAgentPanel.Visibility = Visibility.Collapsed;
    }

    // Jalium 没有 TreeView 的数据模板；这里把层级压平，缩进由 Depth 算。
    private void RebuildSubAgentRows()
    {
        if (_subAgentTree is null) return;

        var parts = new List<string>
        {
            $"运行中 {_subAgentTree.RunningCount}",
            $"共 {_subAgentTree.TotalCount} 个",
        };
        if (_subAgentTree.SubtreeTokens > 0) parts.Add($"{_subAgentTree.SubtreeTokensText} tokens");
        if (_subAgentTree.HasFailed) parts.Add($"失败 {_subAgentTree.FailedCount}");
        _subAgentSummary.Text = string.Join(" · ", parts);

        _subAgentEmpty.Visibility = _subAgentTree.IsEmpty ? Visibility.Visible : Visibility.Collapsed;

        _subAgentRows.Children.Clear();
        foreach (var root in _subAgentTree.Roots) AddSubAgentRow(root, 0);
    }

    private void AddSubAgentRow(SubAgentTreeNodeViewModel node, int depth)
    {
        var row = new StackPanel { Spacing = 1, Margin = new Thickness(depth * 12, 0, 0, 4) };

        var title = NodeChrome.Text(node.Title, 11, CardPalette.TitleBrush, FontWeights.SemiBold);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        row.Children.Add(title);

        var meta = new List<string>();
        if (node.HasDuration) meta.Add(node.DurationText);
        if (node.HasTokens) meta.Add($"{node.TokensText} tokens");
        if (node.ShowStateText) meta.Add(node.StateText);
        if (node.HasDroppedRequests) meta.Add($"有 {node.DroppedRequestCount} 项请求被拒");

        if (meta.Count > 0)
        {
            var detail = NodeChrome.Text(string.Join(" · ", meta), 10, CardPalette.LabelBrush, FontWeights.Normal);
            detail.TextWrapping = TextWrapping.Wrap;
            row.Children.Add(detail);
        }

        _subAgentRows.Children.Add(row);
        foreach (var child in node.Children) AddSubAgentRow(child, depth + 1);
    }

    private void SendToAgent()
    {
        var text = _agentInput.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        _tree.AskCommand.Execute(text);
        _agentInput.Text = string.Empty;
    }

    // ── Tree load / subscription ────────────────────────────────────────────

    private void LoadNetworkDemo()
    {
        UnbindTree(_boundTree);

        // 正在跑的会话不能 Dispose：它还要往自己的日志文件里写。空闲的才放掉。
        if (_demo is { } old && !old.Tree.IsWorkflowRunning) old.Dispose();

        _demo = WorkflowDemoSession.Create();
        BindTree(_demo.Tree);
        _demo.Tree.Layout.UpdateCommand.Execute(null);
        CenterViewport();
        InitializeMcp();
    }

    private void BindTree(TreeViewModel tree)
    {
        _tree = tree;
        _boundTree = tree;

        _surface.SetTree(tree);
        _elementCatalog.SetTree(tree);
        _executionLog.ItemsSource = tree.ExecutionLog;
        _agentLog.ItemsSource = tree.AgentLog;

        tree.ExecutionLog.CollectionChanged += OnExecutionLogChanged;
        tree.AgentLog.CollectionChanged += OnAgentLogChanged;
        tree.Nodes.CollectionChanged += OnTreeCountsChanged;
        tree.Links.CollectionChanged += OnTreeCountsChanged;
        tree.GetHelper().VisibleItems.CollectionChanged += OnTreeCountsChanged;
        if (tree.Layout is INotifyPropertyChanged layout) layout.PropertyChanged += OnLayoutChanged;
        tree.PropertyChanged += OnTreePropertyChanged;
        tree.Runs.Changed += OnRunsChanged;

        if (tree.GetHelper() is AgentHelper helper)
        {
            _surfaceRefresh ??= new CoalescedRefresh(
                RefreshSurface,
                action => _uiDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => action())));

            helper.SelectionHandler = args => AgentDialogs.ShowSelectionAsync(_uiDispatcher, args);
            helper.ConfirmationHandler = args => AgentDialogs.ShowConfirmationAsync(_uiDispatcher, args);
            helper.ToolCalled += OnAgentToolCalled;
            helper.VisualRefreshRequested += OnVisualRefreshRequested;
            InitializePermissionMode();
            AttachSubAgents(helper);
        }
        else
        {
            DetachSubAgents();
        }

        if (_streamToggle is not null) _streamToggle.IsChecked = tree.UseStreamingAgentResponse;

        UpdateElementStatus();
        RefreshInspectorSelection();
        UpdateStatusBar();
        ScheduleRefresh();
    }

    private void UnbindTree(TreeViewModel? tree)
    {
        if (tree is null) return;

        tree.ExecutionLog.CollectionChanged -= OnExecutionLogChanged;
        tree.AgentLog.CollectionChanged -= OnAgentLogChanged;
        tree.Nodes.CollectionChanged -= OnTreeCountsChanged;
        tree.Links.CollectionChanged -= OnTreeCountsChanged;
        tree.GetHelper().VisibleItems.CollectionChanged -= OnTreeCountsChanged;
        if (tree.Layout is INotifyPropertyChanged layout) layout.PropertyChanged -= OnLayoutChanged;
        tree.PropertyChanged -= OnTreePropertyChanged;
        tree.Runs.Changed -= OnRunsChanged;

        if (tree.GetHelper() is AgentHelper helper)
        {
            helper.SelectionHandler = null;
            helper.ConfirmationHandler = null;
            helper.ToolCalled -= OnAgentToolCalled;
            helper.VisualRefreshRequested -= OnVisualRefreshRequested;
        }

        DetachSubAgents();
        UnsubscribeMcp();

        if (ReferenceEquals(_boundTree, tree)) _boundTree = null;
    }

    private void OnTreeCountsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateStatusBar();

    private void OnLayoutChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CanvasLayout.Scale) or nameof(CanvasLayout.ActualSize) or nameof(CanvasLayout.ActualOffset))
        {
            UpdateStatusBar();
        }
    }

    private void OnTreePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TreeViewModel.SelectedElement) or nameof(TreeViewModel.HasSelectedElement))
        {
            RefreshInspectorSelection();
        }
        else if (e.PropertyName is nameof(TreeViewModel.IsWorkflowRunning))
        {
            UpdateStatusBar();
        }
    }

    // 运行目录会连着改聚合量；一帧合并一次，避免在按钮的点击调用栈里重建它自己。
    private void OnRunsChanged(object? sender, EventArgs e) => ScheduleRefresh();

    private void ScheduleRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;

        _uiDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _refreshQueued = false;
            RebuildRunRows();
            UpdateOutputHeader();
            RebuildCompiledStructure();
            UpdateStatusBar();
        }));
    }

    private void UpdateStatusBar()
    {
        bool running = _tree.IsWorkflowRunning;
        _statusDot.Background = CardPalette.Of(running ? CardPalette.AccentTimerPython : CardPalette.ExecOrderText);
        _statusRun.Text = running ? "运行中" : "空闲";
        _statusRun.Foreground = CardPalette.Of(running ? CardPalette.AccentTimerPython : CardPalette.ValueText);
        _statusCompiled.Text = $"编译 {_tree.CompiledStructure.Count} 段";
        _statusElements.Text = $"元素 {_tree.TotalElementText}";
        _statusVisible.Text = $"可见 {_tree.VisibleElementText}";

        var scale = _tree.Layout.Scale.Horizontal;
        _statusZoom.Text = scale > 0 ? $"{100 / scale:0}%" : "100%";
    }

    private void OnExecutionLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateOutputHeader();
        ScrollOutputToEnd();
    }

    private void OnAgentLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_agentLog.Items.Count > 0)
        {
            _agentLog.ScrollIntoView(_agentLog.Items[_agentLog.Items.Count - 1]);
        }
    }

    private void CenterViewport()
    {
        _ = _uiDispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            var layout = _tree.Layout;
            _surfaceViewer.ScrollToHorizontalOffset(Math.Max(0, layout.ActualSize.Width / 2.0 - _surfaceViewer.ViewportWidth / 2.0));
            _surfaceViewer.ScrollToVerticalOffset(Math.Max(0, layout.ActualSize.Height / 2.0 - _surfaceViewer.ViewportHeight / 2.0));
        }));
    }

    // ── Agent / surface refresh ─────────────────────────────────────────────

    private void OnAgentToolCalled() => _surfaceRefresh?.Request();

    private void OnVisualRefreshRequested() => _surfaceRefresh?.Request();

    // 合并器排到 UI 线程上跑的就是这里；线程跳在合并器那边。
    private void RefreshSurface()
    {
        _surface.InvalidateVisual();
        _surface.Changed?.Invoke();
    }

    private void InitializePermissionMode()
    {
        if (_permissionModePicker is null) return;
        if (_tree.GetHelper() is not AgentHelper helper) return;

        _permissionModePicker.ItemsSource = Enum.GetValues<AgentPermissionMode>();
        _permissionModePicker.SelectedItem = helper.PermissionMode;
    }

    private void OnPermissionModeChanged()
    {
        if (_permissionModePicker?.SelectedItem is not AgentPermissionMode mode) return;
        if (_tree.GetHelper() is not AgentHelper helper) return;

        helper.PermissionMode = mode;
        helper.Scope?.SetPermissionMode(mode);
    }

    // ── MCP status ──────────────────────────────────────────────────────────

    private void InitializeMcp()
    {
        UnsubscribeMcp();
        if (_tree.GetHelper() is not AgentHelper helper) return;

        helper.Mcp.WithSynchronizationContext(SynchronizationContext.Current);
        _mcpStatus = helper.Mcp.Status;
        _mcpStatus.Servers.CollectionChanged += OnMcpServersChanged;
        RenderMcpStatus(_mcpStatus);
        _ = helper.LoadMcpServersAsync();
    }

    private void UnsubscribeMcp()
    {
        if (_mcpStatus is null) return;

        _mcpStatus.Servers.CollectionChanged -= OnMcpServersChanged;
        foreach (var server in _mcpServerSubs) server.PropertyChanged -= OnMcpServerChanged;
        _mcpServerSubs.Clear();
        _mcpStatus = null;
    }

    private void OnMcpServersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_mcpStatus is null) return;

        if (e.NewItems is not null)
        {
            foreach (McpServerStatusViewModel server in e.NewItems)
            {
                if (_mcpServerSubs.Add(server)) server.PropertyChanged += OnMcpServerChanged;
            }
        }

        RenderMcpStatus(_mcpStatus);
    }

    private void OnMcpServerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_mcpStatus is not null) RenderMcpStatus(_mcpStatus);
    }

    private void RenderMcpStatus(McpStatusViewModel status)
    {
        _mcpSummary.Text = $"存活 {status.ConnectedCount} · 错误 {status.ErrorCount}";
        _mcpServers.Children.Clear();
        foreach (var server in status.Servers)
        {
            var dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center,
                Background = CardPalette.Of(server.IsConnected
                    ? CardPalette.ExecOrderText
                    : server.IsError
                        ? CardPalette.GhostStopText
                        : CardPalette.AccentEnum),
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(dot);
            row.Children.Add(NodeChrome.Text(server.Name, 11, CardPalette.ValueBrush, FontWeights.SemiBold));
            row.Children.Add(NodeChrome.Text(server.StateText, 10, CardPalette.LabelBrush, FontWeights.Normal));
            row.Children.Add(NodeChrome.Text($"{server.ToolCount} tools", 10, CardPalette.Of(CardPalette.AccentTimerPython), FontWeights.Normal));
            _mcpServers.Children.Add(row);
        }
    }

    // ── Window-level input ──────────────────────────────────────────────────

    /// <summary>Window-level preview key: Delete removes the link under the pointer; Ctrl +/- zooms about the
    /// viewport center. Plain +/- stays unhandled so it cannot fire by accident.</summary>
    protected override bool OnPreviewWindowKeyDown(Key key, ModifierKeys modifiers, bool isRepeat)
    {
        // Delete 删掉指针下的那条连线。不看焦点而看悬停：指针搭在连线上就说明这一下是冲它来的。
        if (key == Key.Delete)
        {
            return _surface.DeleteSelectedLink();
        }

        // Scale 是塌缩系数：放大是除、缩小是乘。
        if (modifiers == ModifierKeys.Control)
        {
            if (key == Key.Add || key == Key.OemPlus)
            {
                ZoomBy(1 / 1.1);
                return true;
            }

            if (key == Key.Subtract || key == Key.OemMinus)
            {
                ZoomBy(1.1);
                return true;
            }
        }

        return base.OnPreviewWindowKeyDown(key, modifiers, isRepeat);
    }

    /// <summary>Window-level preview wheel: Ctrl + wheel zooms about the viewport center.</summary>
    protected override bool OnPreviewWindowMouseWheel(int delta, Point position)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            ZoomBy(delta > 0 ? 1 / 1.1 : 1.1);
            return true;
        }

        return base.OnPreviewWindowMouseWheel(delta, position);
    }

    /// <summary>Zooms about the world point under the viewport center so it stays on-screen. A plain Scale
    /// change would collapse every node toward the world origin, so the pivot is captured, applied, and the
    /// scroll re-centered after layout has adopted the new extent.</summary>
    private void ZoomBy(double factor)
    {
        var next = System.Math.Max(0.1, System.Math.Min(10, _tree.Layout.Scale.Horizontal * factor));
        var layout = _tree.Layout;

        if (layout.ZoomCenter == ZoomCenter.ViewportCenter)
        {
            var (wx, wy) = WorkflowSurfaceMath.WorldAtViewportCenter(
                _surfaceViewer.HorizontalOffset, _surfaceViewer.VerticalOffset,
                _surfaceViewer.ViewportWidth, _surfaceViewer.ViewportHeight, layout);
            layout.CollapsePivot = new Anchor(wx, wy, 0);
            layout.Scale = new Scale(next, next);

            // 先让视图器采纳新的 extent，再读 ScrollableWidth/Height；否则会按旧 extent 夹一次，下一格就漂。
            _surface.UpdateLayout();
            _surfaceViewer.UpdateLayout();

            var (tx, ty) = WorkflowSurfaceMath.PivotCenterScroll(wx, wy, layout,
                _surfaceViewer.ViewportWidth, _surfaceViewer.ViewportHeight);
            var maxH = _surfaceViewer.ScrollableWidth;
            var maxV = _surfaceViewer.ScrollableHeight;

            var newX = WorkflowSurfaceMath.ClampScrollOffset(tx, maxH, layout, horizontal: true);
            var newY = WorkflowSurfaceMath.ClampScrollOffset(ty, maxV, layout, horizontal: false);
            if (Math.Abs(newX - tx) > double.Epsilon || Math.Abs(newY - ty) > double.Epsilon)
            {
                _surface.UpdateLayout();
                _surfaceViewer.UpdateLayout();
                maxH = _surfaceViewer.ScrollableWidth;
                maxV = _surfaceViewer.ScrollableHeight;
            }

            _surfaceViewer.ScrollToHorizontalOffset(WorkflowSurfaceMath.ClampValue(tx, 0, maxH));
            _surfaceViewer.ScrollToVerticalOffset(WorkflowSurfaceMath.ClampValue(ty, 0, maxV));
        }
        else
        {
            layout.Scale = new Scale(next, next);
        }
    }

    // ── Save / Select ───────────────────────────────────────────────────────

    private void SaveWorkflow()
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存 Workflow.json",
            Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            DefaultExt = ".json",
            FileName = "Workflow.json",
        };
        if (dialog.ShowDialog() == true)
        {
            _tree.SaveCommand.Execute(dialog.FileName);
        }
    }

    private async Task SelectWorkflowAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择工作流文件",
            Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            DefaultExt = ".json",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true) return;

        string json;
        try
        {
            json = await File.ReadAllTextAsync(dialog.FileName);
        }
        catch (Exception)
        {
            return;
        }

        if (!json.TryDeserialize<TreeViewModel>(out var result) || result is null) return;

        // 视图位置由保存下来的布局自己恢复；先让布局跑一遍，再挂到表面上。
        result.Layout.UpdateCommand.Execute(null);

        UnbindTree(_boundTree);

        // 同 LoadNetworkDemo：跑着的会话留给它自己收尾，空闲的才关日志。
        if (_demo is { } old && !old.Tree.IsWorkflowRunning) old.Dispose();
        _demo = null;

        BindTree(result);
        InitializeMcp();
    }

    // ── Shared chrome ───────────────────────────────────────────────────────

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text,
        Foreground = CardPalette.ValueBrush,
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(12, 14, 12, 6),
    };

    private static Button SidebarIconButton(RailIconKind kind, string tooltip)
    {
        var label = new RailIcon
        {
            Kind = kind,
            Width = 16,
            Height = 16,
            Foreground = CardPalette.LabelBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var button = new Button
        {
            Width = 26,
            Height = 26,
            MinWidth = 0,
            MinHeight = 0,
            Margin = new Thickness(0, 0, 4, 0),
            Padding = new Thickness(0),
            Background = CardPalette.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(3),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = label,
            ToolTip = tooltip,
            Cursor = Cursors.Hand,
        };
        button.MouseEnter += (_, _) => button.Background = CardPalette.HoverBrush;
        button.MouseLeave += (_, _) => button.Background = CardPalette.Transparent;
        return button;
    }

    private static Button RunRowButton(string text, Color color, bool visible, Action action)
    {
        var button = NodeChrome.Ghost(text, color);
        button.FontSize = 11;
        button.Padding = new Thickness(6, 1);
        button.MinWidth = 0;
        button.MinHeight = 0;
        button.Margin = new Thickness(0, 0, 0, 3);
        button.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        button.Click += (_, _) => action();
        return button;
    }

    // 检查器里的卡：比面板抬一档的底 + 发丝边 + 6 圆角。
    private static Border Card(FrameworkElement content) => new()
    {
        Margin = new Thickness(12, 0, 12, 0),
        Padding = new Thickness(10),
        Background = CardPalette.FieldBrush,
        BorderBrush = CardPalette.BorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(CardPalette.FieldRadius),
        Child = content,
    };

    private static Border Section(string title, FrameworkElement content)
    {
        var panel = new StackPanel { Spacing = 6 };
        if (title.Length > 0)
        {
            panel.Children.Add(NodeChrome.Text(title, 12, CardPalette.Of(CardPalette.AccentTimerPython), FontWeights.Bold));
        }

        panel.Children.Add(content);
        return Card(panel);
    }

    private static TabItem MakeTab(string header, FrameworkElement content) => new()
    {
        Header = header,
        Content = content,
        Foreground = CardPalette.ValueBrush,
        IndicatorBrush = CardPalette.Of(CardPalette.AccentTimerPython),
        SelectedBackground = CardPalette.FieldBrush,
        HoverBackground = CardPalette.HoverBrush,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
    };

    // 选中的标签用正文色，其余用次要色；指示条由 TabItem 自己的 IndicatorBrush 画。
    private static void ApplyTabState(TabControl tabs)
    {
        for (var i = 0; i < tabs.Items.Count; i++)
        {
            if (tabs.Items[i] is not TabItem tab) continue;
            tab.Foreground = i == tabs.SelectedIndex ? CardPalette.TitleBrush : CardPalette.ValueBrush;
        }
    }
}
