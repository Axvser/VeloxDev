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
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;

namespace Demo;

/// <summary>The full WorkflowSystem demo on Jalium: the voltage-analysis chain
/// (WorkflowDemoSession.Create) rendered through the SAME NodeEditorSurface the trimmed demo uses
/// (drag / connect / pan / auto-grow / minimap all identical), plus a control sidebar (Controller
/// Compile/Run/Stop/Close, Undo/Redo/Save/Select/Load, node counts, Agent chat, MCP status,
/// execution log). The Agent/MCP panels mirror the WPF/Avalonia full demos; the Agent only responds
/// when an OpenAI-compatible key is configured.</summary>
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

    private TreeViewModel _tree = new();
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
        surface = new NodeEditorSurface();
        viewer = new ScrollViewer
        {
            Content = surface,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            PanningMode = PanningMode.None, // surface handles mouse-pan itself
        };
        surface.AttachScrollViewer(viewer);

        var minimap = new Minimap(surface, viewer)
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 40, 16, 0),
        };

        // The grid + ruler bands are drawn by the NodeEditorSurface's own OnRender/OnPostRender
        // (absolute-floating rulers, viewport-fixed), like the Trimmed demo.
        void RefreshOverlays() => minimap.Update();

        viewer.ScrollChanged += (_, _) => RefreshOverlays();
        surface.Changed += RefreshOverlays;

        var root = new Grid();
        root.Children.Add(viewer);
        root.Children.Add(minimap);
        return root;
    }

    // ── Sidebar ────────────────────────────────────────────────────────────

    private FrameworkElement BuildSidebar()
    {
        var panel = new StackPanel { Spacing = 10 };

        static Button ActionButton(string text) => new()
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
    /// Zoom the workspace with + / - ; each node collapses toward the world origin by 1/scale
    /// (the Core Anchor/Size getters).</summary>
    protected override bool OnPreviewWindowKeyDown(Key key, ModifierKeys modifiers, bool isRepeat)
    {
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
    /// Ctrl + wheel zooms the workspace (each node collapses toward the origin by 1/scale).</summary>
    protected override bool OnPreviewWindowMouseWheel(int delta, Point position)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            ZoomBy(delta > 0 ? 1 / 1.1 : 1.1);
            return true;
        }

        return base.OnPreviewWindowMouseWheel(delta, position);
    }

    private void ZoomBy(double factor)
    {
        var next = System.Math.Max(0.1, System.Math.Min(10, _tree.Layout.Scale.Horizontal * factor));
        _tree.Layout.Scale = new Scale(next, next);
    }

    private void LoadNetworkDemo()
    {
        UnsubscribeTree(_tree);
        _tree = WorkflowDemoSession.Create().Tree;
        _surface.SetTree(_tree);
        SubscribeTree(_tree);
        UpdateCounts();
        CenterViewport();
    }

    private void SubscribeTree(TreeViewModel vm)
    {
        vm.ExecutionLog.CollectionChanged += OnExecutionLogChanged;
        vm.AgentLog.CollectionChanged += OnAgentLogChanged;
        vm.Nodes.CollectionChanged += OnTreeCollectionsChanged;
        if (vm.GetHelper() is AgentHelper helper)
        {
            helper.SelectionHandler = args => AgentDialogs.ShowSelectionAsync(_uiDispatcher, args);
            helper.ConfirmationHandler = args => AgentDialogs.ShowConfirmationAsync(_uiDispatcher, args);
            helper.ToolCalled += OnAgentToolCalled;
            helper.VisualRefreshRequested += OnVisualRefreshRequested;
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

    private void OnAgentToolCalled() => RefreshSurface();

    private void OnVisualRefreshRequested() => RefreshSurface();

    private void RefreshSurface()
    {
        _uiDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _surface.InvalidateVisual();
            _surface.Changed?.Invoke();
        }));
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

        var vpX = result.Layout.ViewportOffset.Horizontal;
        var vpY = result.Layout.ViewportOffset.Vertical;

        UnsubscribeTree(_tree);
        _tree = result;
        _surface.SetTree(_tree);
        SubscribeTree(_tree);
        _ = _uiDispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            var offset = _tree.Layout.ActualOffset;
            if (vpX > 0 || vpY > 0)
            {
                _surfaceViewer.ScrollToHorizontalOffset(Math.Max(0, vpX + offset.Horizontal));
                _surfaceViewer.ScrollToVerticalOffset(Math.Max(0, vpY + offset.Vertical));
            }
            else
            {
                CenterViewport();
            }
        }));
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
