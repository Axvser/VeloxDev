using System.Collections.Specialized;
using System.IO;
using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Views.Workflow;
using Demo.Workflow;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using Microsoft.Win32;
using VeloxDev.AI;
using VeloxDev.AI.MCP;
using VeloxDev.AI.Safety;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

namespace Demo;

/// <summary>The full WorkflowSystem demo on Jalium: the voltage-analysis chain
/// (WorkflowDemoSession.Create) rendered through the SAME NodeEditorSurface the trimmed demo uses
/// (drag / connect / pan / auto-grow / minimap all identical), plus a control sidebar (Controller
/// Compile/Run/Stop/Close, Undo/Redo/Save/Select/Load, node counts, run controls (pause gate +
/// checkpoint resume), Agent chat, MCP status, execution log). The Agent/MCP panels mirror the
/// WPF/Avalonia full demos; the Agent only responds when an OpenAI-compatible key is configured.</summary>
internal sealed class MainWindow : Window
{
    private readonly NodeEditorSurface _surface;
    private readonly ScrollViewer _surfaceViewer;
    private readonly Dispatcher _uiDispatcher;
    private readonly TextBlock _nodeCount = new();
    private readonly TextBlock _visibleCount = new();
    private readonly ListBox _executionLog = new();
    private readonly ListBox _agentLog = new();
    private readonly TextBox _agentInput = new();
    private readonly TextBlock _mcpSummary = new();
    private readonly StackPanel _mcpServers = new() { Spacing = 3 };
    private readonly TextBlock _runGateState = new();

    private TreeViewModel _tree = new();
    private WorkflowDemoSession? _demo;

    // 一次 Agent 对话里每个工具调用都要求刷新，而刷新是全量的（重新定位命名控件、跑布局、重算可见区）。
    // 合并之后一轮只刷新一次 —— 刷新本身是幂等的，后来的请求要的正是前一次即将看到的状态。
    private CoalescedRefresh? _surfaceRefresh;
    private Button? _continueFromCheckpoint;
    private ComboBox? _permissionModePicker;
    private McpStatusViewModel? _mcpStatus;
    private readonly HashSet<McpServerStatusViewModel> _mcpServerSubs = new();

    public MainWindow()
    {
        Title = "VeloxDev Workflow - Jalium";
        Width = 1280;
        Height = 820;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

        _uiDispatcher = Dispatcher;
        var surfaceArea = BuildSurfaceArea(out _surface, out _surfaceViewer);
        var sidebar = BuildSidebar();
        var splitter = new GridSplitter
        {
            Width = 5,
            Background = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
            ResizeDirection = GridResizeDirection.Columns,
        };

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.FromStar(2) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.FromStar(8) });
        sidebar.Margin = new Thickness(16, 20, 0, 20);
        Grid.SetColumn(sidebar, 0);
        Grid.SetColumn(splitter, 1);
        surfaceArea.Margin = new Thickness(0, 20, 20, 20);
        Grid.SetColumn(surfaceArea, 2);
        root.Children.Add(sidebar);
        root.Children.Add(splitter);
        root.Children.Add(surfaceArea);
        Content = root;

        LoadNetworkDemo();
        InitializeMcp();
    }

    // ── Surface (the trimmed demo's NodeEditorSurface composition) ─────────

    private static FrameworkElement BuildSurfaceArea(out NodeEditorSurface surface, out ScrollViewer viewer)
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

        var minimap = new Minimap(viewerControl)
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 40, 16, 0),
        };

        // Realtime canvas-info decorator layer (floating text HUD): anchored bottom-left, hit-test
        // transparent. It subscribes to the Core model itself (Layout.ActualSize / Scale / helper
        // VisibleItems) and is fed the same scroll + content-offset + viewport numbers as the minimap,
        // so the read-out stays live while panning / zooming / dragging beneath it.
        var info = new InfoOverlay
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(16, 0, 0, 18),
        };

        // The grid + ruler bands are drawn by the NodeEditorSurface's own OnRender/OnPostRender
        // (absolute-floating rulers, viewport-fixed), like the Trimmed demo. Feed the minimap (the
        // overlay base repaints from these and drag-pans through its ScrollViewer) and the HUD on every
        // scroll / model change — the same feed the Trimmed window gives its overlays.
        void RefreshOverlays()
        {
            minimap.WorkflowTree = surfaceView.Tree;
            minimap.ContentOffsetX = surfaceView.OriginX;
            minimap.ContentOffsetY = surfaceView.OriginY;
            minimap.ScrollOffsetX = viewerControl.HorizontalOffset;
            minimap.ScrollOffsetY = viewerControl.VerticalOffset;
            minimap.ViewportWidth = viewerControl.ViewportWidth;
            minimap.ViewportHeight = viewerControl.ViewportHeight;

            info.WorkflowTree = surfaceView.Tree;
            info.ContentOffsetX = surfaceView.OriginX;
            info.ContentOffsetY = surfaceView.OriginY;
            info.ScrollOffsetX = viewerControl.HorizontalOffset;
            info.ScrollOffsetY = viewerControl.VerticalOffset;
            info.ViewportWidth = viewerControl.ViewportWidth;
            info.ViewportHeight = viewerControl.ViewportHeight;
        }

        // SizeChanged catches the viewer's first measure (Jalium may not fire ScrollChanged on the
        // initial layout).
        viewerControl.ScrollChanged += (_, _) => RefreshOverlays();
        viewerControl.SizeChanged += (_, _) => RefreshOverlays();
        surfaceView.Changed += RefreshOverlays;

        var root = new Grid();
        root.Children.Add(viewerControl);
        root.Children.Add(minimap);
        root.Children.Add(info);

        surface = surfaceView;
        viewer = viewerControl;
        return root;
    }

    // ── Sidebar ────────────────────────────────────────────────────────────

    private FrameworkElement BuildSidebar()
    {
        var panel = new StackPanel { Spacing = 10 };

        // ── Workflow actions ───────────────────────────────────────────────
        var undo = ActionButton("Undo");
        undo.Click += (_, _) => _tree.UndoCommand.Execute(null);
        var redo = ActionButton("Redo");
        redo.Click += (_, _) => _tree.RedoCommand.Execute(null);
        var save = ActionButton("Save");
        save.Click += (_, _) => SaveWorkflow();
        var select = ActionButton("Select");
        select.Click += (_, _) => _ = SelectWorkflowAsync();
        var load = ActionButton("Load Workflow Demo");
        load.Click += (_, _) => LoadNetworkDemo();
        panel.Children.Add(Section("Actions", new StackPanel { Spacing = 8, Children = { undo, redo, save, select, load } }));

        panel.Children.Add(new TextBlock { Text = "节点总数：", Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E)), Margin = new Thickness(0, 6, 0, 0) });
        _nodeCount.Foreground = new SolidColorBrush(Colors.White);
        panel.Children.Add(_nodeCount);
        panel.Children.Add(new TextBlock { Text = "连线总数：", Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E)), Margin = new Thickness(0, 4, 0, 0) });
        _visibleCount.Foreground = new SolidColorBrush(Colors.White);
        panel.Children.Add(_visibleCount);

        panel.Children.Add(BuildPermissionModePanel());
        panel.Children.Add(BuildRunControlsPanel());
        panel.Children.Add(BuildAgentChatPanel());
        panel.Children.Add(BuildMcpPanel());
        panel.Children.Add(BuildExecutionLogPanel());

        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = panel,
        };
        return scroller;
    }

    private static Button ActionButton(string text) => new()
    {
        Content = text,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Height = 40,
        FontSize = 13,
        Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)),
        Foreground = new SolidColorBrush(Colors.White),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x4B, 0x4B, 0x4B)),
        BorderThickness = new Thickness(1),
    };

    // 权限模式：会话级设置，所以留在侧栏上部 —— 对话面板在侧栏底部，下拉在那里会被窗口底边裁掉，
    // 五个模式只剩第一项点得到。它也不是 AgentModes 的 build/plan 那一对：那一对改的是模型被告知什么，
    // 这一个改的是什么允许跑。
    private FrameworkElement BuildPermissionModePanel()
    {
        var row = new Grid { ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

        var label = new TextBlock
        {
            Text = "权限模式",
            Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 0);

        _permissionModePicker = new ComboBox
        {
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4B, 0x4B, 0x4B)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _permissionModePicker.SelectionChanged += (_, _) => OnPermissionModeChanged();
        Grid.SetColumn(_permissionModePicker, 1);

        row.Children.Add(label);
        row.Children.Add(_permissionModePicker);
        return row;
    }

    /// <summary>
    /// Fills the permission-mode picker with every mode, and starts it on the one the helper was built with.
    /// </summary>
    /// <remarks>
    /// Selecting the current mode fires the change handler, which sets the same mode again —
    /// <c>SetPermissionMode</c> reports that nothing moved and does nothing. Cheaper than arranging not to.
    /// The picker itself is built in the constructor, before any tree exists, so its items are filled here
    /// instead — the first moment there is a helper to read the initial mode from.
    /// <para>
    /// Re-run on every tree swap rather than once at construction: a tree loaded from a file arrives with its
    /// own helper at that helper's default mode, and a picker still showing the previous one would be a lie
    /// about the session on screen.
    /// </para>
    /// </remarks>
    private void InitializePermissionMode()
    {
        if (_permissionModePicker is null) return;
        if (_tree.GetHelper() is not AgentHelper helper) return;

        _permissionModePicker.ItemsSource = Enum.GetValues<AgentPermissionMode>();
        _permissionModePicker.SelectedItem = helper.PermissionMode;
    }

    /// <summary>
    /// Moves the session to the mode the user picked. There is nothing to rebuild: the gate reads the policy
    /// per call, so the very next tool call obeys the new mode, and the prompt is re-rendered next turn.
    /// </summary>
    private void OnPermissionModeChanged()
    {
        if (_permissionModePicker?.SelectedItem is not AgentPermissionMode mode) return;
        if (_tree.GetHelper() is not AgentHelper helper) return;

        helper.PermissionMode = mode;
        helper.Scope?.SetPermissionMode(mode);
    }

    /// <summary>The two capabilities a run cannot press by itself: the pause gate and the checkpoint a
    /// later run carries on from. Both live on the session, so these controls act on the window's
    /// <see cref="WorkflowDemoSession"/> rather than on the tree — the way the Avalonia demo's sidebar
    /// panel does.</summary>
    private FrameworkElement BuildRunControlsPanel()
    {
        var body = new StackPanel { Spacing = 6 };

        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var header = new TextBlock { Text = "运行控制", Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0xC8, 0xFF)), FontWeight = FontWeights.Bold };
        _runGateState.Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E));
        _runGateState.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(header, 0);
        Grid.SetColumn(_runGateState, 1);
        headerRow.Children.Add(header);
        headerRow.Children.Add(_runGateState);
        body.Children.Add(headerRow);

        var pause = ActionButton("Pause");
        pause.Click += (_, _) => PauseWorkflow();
        var resume = ActionButton("Resume");
        resume.Click += (_, _) => ResumeWorkflow();
        var gateRow = new Grid { ColumnSpacing = 6 };
        gateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        gateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        Grid.SetColumn(pause, 0);
        Grid.SetColumn(resume, 1);
        gateRow.Children.Add(pause);
        gateRow.Children.Add(resume);
        body.Children.Add(gateRow);

        _continueFromCheckpoint = ActionButton("从检查点继续");
        _continueFromCheckpoint.IsEnabled = false;
        _continueFromCheckpoint.Click += (_, _) => _ = ContinueFromCheckpointAsync();
        body.Children.Add(_continueFromCheckpoint);

        return Section("", body);
    }

    private void PauseWorkflow()
    {
        if (_demo is null)
        {
            return;
        }

        _demo.Gate.Pause();
        _runGateState.Text = "已暂停：停在下一个节点边界";
    }

    private void ResumeWorkflow()
    {
        if (_demo is null)
        {
            return;
        }

        _demo.Gate.Resume();
        _runGateState.Text = "运行中";
    }

    private async Task ContinueFromCheckpointAsync()
    {
        if (_demo is null)
        {
            return;
        }

        _runGateState.Text = "从检查点继续…";
        await _demo.Controller.ResumeCommand.ExecuteAsync(null);
    }

    // A checkpoint is only on disk once a run has ended, so this is refreshed when a run's command exits.
    private void RefreshRunControls()
    {
        if (_continueFromCheckpoint is not null)
        {
            _continueFromCheckpoint.IsEnabled = _demo?.HasCheckpoint == true;
        }

        _runGateState.Text = _demo?.Gate.IsPaused == true ? "已暂停" : "空闲";
    }

    private FrameworkElement BuildAgentChatPanel()
    {
        var body = new StackPanel { Spacing = 6 };

        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var header = new TextBlock { Text = "Agent 对话", Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0xC8, 0xFF)), FontWeight = FontWeights.Bold };
        var streamToggle = new CheckBox { Content = "流式", Foreground = new SolidColorBrush(Colors.White), IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        streamToggle.Checked += (_, _) => _tree.UseStreamingAgentResponse = true;
        streamToggle.Unchecked += (_, _) => _tree.UseStreamingAgentResponse = false;
        Grid.SetColumn(header, 0);
        Grid.SetColumn(streamToggle, 1);
        headerRow.Children.Add(header);
        headerRow.Children.Add(streamToggle);
        body.Children.Add(headerRow);

        _agentLog.MaxHeight = 220;
        body.Children.Add(_agentLog);

        var inputRow = new Grid();
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _agentInput.Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x11, 0x17));
        _agentInput.Foreground = new SolidColorBrush(Colors.White);
        _agentInput.Padding = new Thickness(8, 6);
        _agentInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                SendToAgent();
                e.Handled = true;
            }
        };
        var sendBtn = new Button
        {
            Content = "发送",
            Margin = new Thickness(4, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x34, 0x60)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0xC8, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x7E, 0xC8, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 5),
        };
        sendBtn.Click += (_, _) => SendToAgent();
        Grid.SetColumn(_agentInput, 0);
        Grid.SetColumn(sendBtn, 1);
        inputRow.Children.Add(_agentInput);
        inputRow.Children.Add(sendBtn);
        body.Children.Add(inputRow);

        return Section("", body);
    }

    private FrameworkElement BuildMcpPanel()
    {
        var body = new StackPanel { Spacing = 6 };

        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var header = new TextBlock { Text = "MCP 服务器", Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0xC8, 0xFF)), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        _mcpSummary.Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E));
        _mcpSummary.VerticalAlignment = VerticalAlignment.Center;
        var reloadBtn = new Button
        {
            Content = "重载",
            Padding = new Thickness(8, 2),
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x34, 0x60)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0xC8, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x7E, 0xC8, 0xFF)),
            BorderThickness = new Thickness(1),
        };
        reloadBtn.Click += (_, _) =>
        {
            if (_tree.GetHelper() is AgentHelper helper)
            {
                _ = helper.LoadMcpServersAsync();
            }
        };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(_mcpSummary);
        right.Children.Add(reloadBtn);
        Grid.SetColumn(header, 0);
        Grid.SetColumn(right, 1);
        headerRow.Children.Add(header);
        headerRow.Children.Add(right);
        body.Children.Add(headerRow);

        _mcpServers.Margin = new Thickness(0, 2, 0, 0);
        body.Children.Add(_mcpServers);

        return Section("", body);
    }

    private FrameworkElement BuildExecutionLogPanel()
    {
        var body = new StackPanel { Spacing = 6 };
        var header = new TextBlock { Text = "实际执行顺序", Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0xC8, 0xFF)), FontWeight = FontWeights.Bold };
        body.Children.Add(header);
        body.Children.Add(_executionLog);
        return Section("", body);
    }

    private static Border Section(string title, FrameworkElement content)
    {
        var panel = new StackPanel { Spacing = 6 };
        if (title.Length > 0)
        {
            panel.Children.Add(new TextBlock { Text = title, Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0xC8, 0xFF)), FontWeight = FontWeights.Bold });
        }

        panel.Children.Add(content);
        return new Border
        {
            Margin = new Thickness(0, 4, 0, 0),
            Padding = new Thickness(10),
            Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0x4C, 0x4C)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = panel,
        };
    }

    // ── Workflow load ───────────────────────────────────────────────────────

    /// <summary>Window-level preview key: fires for every key regardless of which child has focus.
    /// Delete removes the link under the pointer; zoom the workspace with + / - (the viewport center is
    /// held fixed — ViewportCenter zoom keeps the world point under the viewport center on-screen while
    /// scaling).</summary>
    protected override bool OnPreviewWindowKeyDown(Key key, ModifierKeys modifiers, bool isRepeat)
    {
        // Delete 删掉指针下的那条连线。表面自己也会处理，这里是兜底：焦点可能不在它身上
        // （比如刚在侧栏的 Agent 输入框里打过字，或者表面没能拿到焦点）。
        // 这里不看焦点而是看有没有选中：悬停即选中，所以「指针搭在连线上」本身就说明了这一下 Delete
        // 是冲那条连线来的；指针不在连线上时没有选中，按键原样落回输入框
        if (key == Key.Delete)
        {
            return _surface.DeleteSelectedLink();
        }

        // Ctrl + '+' zooms in, Ctrl + '-' zooms out (mirrors Ctrl + wheel; plain +/- stays unhandled
        // so it can't fire by accident). Scale is a collapse factor — higher Scale renders nodes smaller
        // (zoom out) — so zoom-in divides Scale and zoom-out multiplies it.
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

    /// <summary>Window-level preview wheel: fires for every wheel event regardless of focus/routing.
    /// Ctrl + wheel zooms the workspace while holding the viewport center fixed.</summary>
    protected override bool OnPreviewWindowMouseWheel(int delta, Point position)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            ZoomBy(delta > 0 ? 1 / 1.1 : 1.1);
            return true;
        }

        return base.OnPreviewWindowMouseWheel(delta, position);
    }

    /// <summary>
    /// Zooms about the world point currently under the viewport center so that point stays on-screen
    /// (the Core <see cref="ZoomCenter.ViewportCenter"/> contract). A plain Scale change would collapse
    /// every node toward the world origin, so a node centered under the viewport would visibly drift
    /// off-center on every notch — capture the pivot, collapse about it and re-center the scroll.
    /// Scale is a collapse factor: higher Scale renders nodes smaller (zoom out), so zoom-in divides
    /// Scale and zoom-out multiplies it.
    /// </summary>
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

            // Re-layout so the ScrollViewer adopts the new extent (zoom-in auto-extends the canvas)
            // BEFORE reading ScrollableWidth/Height. Otherwise the clamp lands against the stale extent
            // and the next wheel tick re-captures the off-center pivot — the compounding drift reads
            // as zoom jitter.
            _surface.UpdateLayout();
            _surfaceViewer.UpdateLayout();

            var (tx, ty) = WorkflowSurfaceMath.PivotCenterScroll(wx, wy, layout,
                _surfaceViewer.ViewportWidth, _surfaceViewer.ViewportHeight);
            var maxH = _surfaceViewer.ScrollableWidth;
            var maxV = _surfaceViewer.ScrollableHeight;

            // Overscroll-expand the canvas so the pivot is always reachable; a plain clamp would push
            // the pivot off-center and drift on each notch. The canvas geometry is untouched by the
            // zoom (ActualOffset == NegativeOffset, fixed) — only the scroll moves.
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
            _tree.Layout.Scale = new Scale(next, next);
        }
    }

    private void LoadNetworkDemo()
    {
        UnsubscribeTree(_tree);
        _demo = WorkflowDemoSession.Create();
        _tree = _demo.Tree;
        _surface.SetTree(_tree);
        SubscribeTree(_tree);
        UpdateCounts();
        CenterViewport();

        // A run writes its checkpoint on the way out — the continue control follows both commands' exit.
        // Exited is raised on a pool thread, so the refresh lands back on ours.
        _demo.Controller.RunCommand.Exited += _ => _uiDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RefreshRunControls));
        _demo.Controller.ResumeCommand.Exited += _ => _uiDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RefreshRunControls));
        RefreshRunControls();
    }

    private void SubscribeTree(TreeViewModel vm)
    {
        vm.ExecutionLog.CollectionChanged += OnExecutionLogChanged;
        vm.AgentLog.CollectionChanged += OnAgentLogChanged;
        vm.Nodes.CollectionChanged += OnTreeCollectionsChanged;
        if (vm.GetHelper() is AgentHelper helper)
        {
            // 一个 surface 一个合并器：换树会重新走到这里，但合并器跟着窗口走，不该重建。
            _surfaceRefresh ??= new CoalescedRefresh(
                RefreshSurface,
                action => _uiDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => action())));

            helper.SelectionHandler = args => AgentDialogs.ShowSelectionAsync(_uiDispatcher, args);
            helper.ConfirmationHandler = args => AgentDialogs.ShowConfirmationAsync(_uiDispatcher, args);
            helper.ToolCalled += OnAgentToolCalled;
            helper.VisualRefreshRequested += OnVisualRefreshRequested;
            InitializePermissionMode();
        }

        _executionLog.ItemsSource = vm.ExecutionLog;
        _agentLog.ItemsSource = vm.AgentLog;
    }

    private void UnsubscribeTree(TreeViewModel vm)
    {
        vm.ExecutionLog.CollectionChanged -= OnExecutionLogChanged;
        vm.AgentLog.CollectionChanged -= OnAgentLogChanged;
        vm.Nodes.CollectionChanged -= OnTreeCollectionsChanged;
        if (vm.GetHelper() is AgentHelper helper)
        {
            helper.SelectionHandler = null;
            helper.ConfirmationHandler = null;
            helper.ToolCalled -= OnAgentToolCalled;
            helper.VisualRefreshRequested -= OnVisualRefreshRequested;
        }
    }

    private void UpdateCounts()
    {
        _nodeCount.Text = _tree.Nodes.Count.ToString();
        _visibleCount.Text = _tree.Links.Count.ToString();
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

    private void OnTreeCollectionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateCounts();

    private void OnExecutionLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_executionLog.Items.Count > 0)
        {
            _executionLog.ScrollIntoView(_executionLog.Items[^1]);
        }
    }

    private void OnAgentLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_agentLog.Items.Count > 0)
        {
            _agentLog.ScrollIntoView(_agentLog.Items[^1]);
        }
    }

    private void SendToAgent()
    {
        var text = _agentInput.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        _tree.AskCommand.Execute(text);
        _agentInput.Text = string.Empty;
    }

    private void OnAgentToolCalled() => _surfaceRefresh?.Request();

    private void OnVisualRefreshRequested() => _surfaceRefresh?.Request();

    // 合并器排到 UI 线程上跑的就是这里；线程跳在合并器那边。
    private void RefreshSurface()
    {
        _surface.InvalidateVisual();
        _surface.Changed?.Invoke();
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
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(dialog.FileName);
        }
        catch (Exception)
        {
            return;
        }

        if (!json.TryDeserialize<TreeViewModel>(out var result) || result is null)
        {
            return;
        }

        UnsubscribeTree(_tree);
        _tree = result;
        // The surface restores the saved viewport position by itself when the tree is attached here.
        _surface.SetTree(_tree);
        SubscribeTree(_tree);

        // A tree from a file has no session behind it — no gate, no checkpoint store — so the run controls
        // have nothing to act on and fall back to their idle state.
        _demo = null;
        RefreshRunControls();
    }

    // ── MCP ─────────────────────────────────────────────────────────────────

    private void InitializeMcp()
    {
        UnsubscribeMcp();
        if (_tree.GetHelper() is not AgentHelper helper)
        {
            return;
        }

        helper.Mcp.WithSynchronizationContext(SynchronizationContext.Current);
        _mcpStatus = helper.Mcp.Status;
        _mcpStatus.Servers.CollectionChanged += OnMcpServersChanged;
        RenderMcpStatus(_mcpStatus);
        _ = helper.LoadMcpServersAsync();
    }

    private void UnsubscribeMcp()
    {
        if (_mcpStatus is not null)
        {
            _mcpStatus.Servers.CollectionChanged -= OnMcpServersChanged;
            foreach (var server in _mcpServerSubs)
            {
                server.PropertyChanged -= OnMcpServerChanged;
            }

            _mcpServerSubs.Clear();
            _mcpStatus = null;
        }
    }

    private void OnMcpServersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_mcpStatus is null)
        {
            return;
        }

        if (e.NewItems is not null)
        {
            foreach (McpServerStatusViewModel server in e.NewItems)
            {
                if (_mcpServerSubs.Add(server))
                {
                    server.PropertyChanged += OnMcpServerChanged;
                }
            }
        }

        RenderMcpStatus(_mcpStatus);
    }

    private void OnMcpServerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_mcpStatus is not null)
        {
            RenderMcpStatus(_mcpStatus);
        }
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
                Background = server.IsConnected
                    ? new SolidColorBrush(Color.FromRgb(0x6B, 0xFF, 0xB8))
                    : server.IsError
                        ? new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B))
                        : new SolidColorBrush(Color.FromRgb(0xFF, 0xD1, 0x66)),
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(dot);
            row.Children.Add(new TextBlock { Text = server.Name, Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E)), FontWeight = FontWeights.SemiBold });
            row.Children.Add(new TextBlock { Text = server.StateText, Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E)), FontSize = 11 });
            row.Children.Add(new TextBlock { Text = $"{server.ToolCount} tools", Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0xC8, 0xFF)), FontSize = 11 });
            _mcpServers.Children.Add(row);
        }
    }
}
