using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Views.Workflow;
using Demo.Workflow;
using Easel.Controls;
using Easel.Visuals;
using Easel.Visuals.Controls;
using VeloxDev.AI;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;
using Wf = VeloxDev.WorkflowSystem;
using VisualPanel = Easel.Visuals.Controls.Panel;
using VisualTextBox = Easel.Visuals.Controls.TextBox;
using VisualRichTextBox = Easel.Visuals.Controls.RichTextBox;

namespace Demo.Views.Workbench;

/// <summary>
/// The workbench shell: one visual tree inside one <see cref="VisualHost"/>. The title strip, activity bar,
/// sidebar, editor tabs, inspector, status bar and the workflow surface itself are all Easel elements; the
/// workflow behavior attaches to the host and resolves its parts inside the named surface subtree.
/// </summary>
public sealed partial class WorkbenchView : WorkbenchViewBase
{
    private readonly VisualHost _host;
    private readonly Dictionary<string, ActivityButton> _activityButtons = [];
    private readonly System.Windows.Forms.Timer _uiTimer;

    private WorkflowDemoSession? _demo;
    private CoalescedRefresh? _surfaceRefresh;
    private Wf.WorkflowInput? _surfaceInput;
    private VisualContextMenu? _nodeMenu;
    private Wf.INodeComponent? _nodeMenuTarget;
    private readonly List<(VisualMenuItem Item, double Tolerance)> _toleranceItems = [];
    private double _nodeLayoutTolerance = 8d;
    private string _sidebarActivity = "explorer";
    private string _inspectorPage = "properties";
    private bool _dialogOpen;

    /// <summary>Creates the shell; the host must be passed so the behavior can be attached after the root exists.</summary>
    public WorkbenchView(VisualHost host)
    {
        _host = host;
        InitializeComponent();
        WorkbenchDispatcher.Initialize(host);

        BuildMenu();
        BuildActivityBar();
        WireShell();
        ConfigureSurface();

        _uiTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _uiTimer.Tick += (_, _) =>
        {
            UpdateZoom();
            AgentPage.SyncSubAgents();
        };
        _uiTimer.Start();
    }

    /// <summary>
    /// Starts the workflow surface and loads the demo. Call after <c>host.Root = this</c>: the behavior resolves
    /// the named surface subtree from the root, so the root must be assigned first.
    /// </summary>
    public void Start()
    {
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetIsEnabled(_host, true);
        ExplorerPage.SetMinimapState(WorkflowSurface.MinimapOverlay.IsMinimapVisible);
        LoadDemo(WorkflowDemoSession.Create());
    }

    /// <summary>Closes the current run tree and the surface before the window goes away.</summary>
    public async Task ShutdownAsync()
    {
        _uiTimer.Stop();
        _uiTimer.Dispose();
        DetachSurfaceInput();
        _nodeMenu?.Close();
        _nodeMenu?.Dispose();
        _nodeMenu = null;
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetIsEnabled(_host, false);

        if (_demo is not null)
        {
            await _demo.Tree.GetHelper().CloseAsync();
            _demo.Dispose();
            _demo = null;
        }
    }

    // ── Surface wiring ───────────────────────────────────────────────────────

    private void ConfigureSurface()
    {
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetSurfaceName(_host, "WorkflowSurface");
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetCanvasName(_host, "PART_Canvas");
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetScrollViewerName(_host, "PART_ScrollViewer");
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetGridDecoratorName(_host, "PART_GridDecorator");
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetMinimapOverlayName(_host, "PART_MinimapOverlay");
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetPointerPressSourceName(_host, "PART_SurfaceBorder");
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetLinkMenuKey(_host, "LinkContextMenu");
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetZoomEnabled(_host, true);
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetVisualLinkColor(_host, Color.FromArgb(0xCC, 0x6E, 0x8B, 0xFF));
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetVisualLinkThickness(_host, 2f);
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetVisualLinkHoverColor(_host, Color.White);
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetVisualLinkHoverThickness(_host, 3.5f);
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetVisualLinkVirtualColor(_host, Color.FromArgb(0xCC, 0x6E, 0x8B, 0xFF));
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetVisualLinkGlow(_host, true);
    }

    private void WireShell()
    {
        SidebarSplitter.Target = SidebarPane;
        InspectorSplitter.Target = InspectorPane;

        ExplorerPage.SaveRequested += (_, _) => SaveWorkflow();
        ExplorerPage.LoadRequested += async (_, _) => await LoadWorkflowAsync();
        ExplorerPage.UndoRequested += (_, _) => _demo?.Tree.UndoCommand.Execute(null);
        ExplorerPage.RedoRequested += (_, _) => _demo?.Tree.RedoCommand.Execute(null);
        ExplorerPage.ReloadRequested += async (_, _) => await ExecuteAsync(ReloadWorkflowAsync, "Failed to reset demo");
        ExplorerPage.MinimapVisibilityRequested += (_, visible) => SetMinimapVisible(visible);
        ExplorerPage.SelectedElementChanged += (_, entry) =>
        {
            if (_demo is not null && !ReferenceEquals(_demo.Tree.SelectedElement, entry))
            {
                _demo.Tree.SelectedElement = entry;
            }
        };

        WorkflowSurface.InfoOverlay.CopyFeedback += (_, message) => StatusBar.HintLabel.Text = message;

        SelectActivity("explorer");
        SelectMainTab("canvas");
        SelectInspectorPage("properties");
    }

    // ── Menu and command centre ──────────────────────────────────────────────

    private void BuildMenu()
    {
        MenuBar.Foreground = WorkbenchPalette.TextSecondary;

        var file = new VisualMenuItem { Header = "文件" };
        file.Items.Add(new VisualMenuItem { Header = "保存工作流", InputGestureText = "Ctrl+S", Command = SaveWorkflow });
        file.Items.Add(new VisualMenuItem { Header = "加载工作流", InputGestureText = "Ctrl+O", Command = () => _ = LoadWorkflowAsync() });
        file.Items.Add(new VisualMenuItem { Header = "重新载入示例", Command = () => _ = ExecuteAsync(ReloadWorkflowAsync, "Failed to reset demo") });
        file.Items.Add(new VisualMenuItem { IsSeparator = true });
        file.Items.Add(new VisualMenuItem { Header = "退出", Command = () => _host.FindForm()?.Close() });
        MenuBar.Items.Add(file);

        var edit = new VisualMenuItem { Header = "编辑" };
        edit.Items.Add(new VisualMenuItem { Header = "撤销", InputGestureText = "Ctrl+Z", Command = () => _demo?.Tree.UndoCommand.Execute(null) });
        edit.Items.Add(new VisualMenuItem { Header = "重做", InputGestureText = "Ctrl+Y", Command = () => _demo?.Tree.RedoCommand.Execute(null) });
        edit.Items.Add(new VisualMenuItem { IsSeparator = true });
        edit.Items.Add(new VisualMenuItem { Header = "命令中心", InputGestureText = "Ctrl+Shift+P", Command = () => ShowCommandCenter(null) });
        MenuBar.Items.Add(edit);

        var view = new VisualMenuItem { Header = "视图" };
        view.Items.Add(new VisualMenuItem { Header = "资源管理器", Command = () => SelectActivity("explorer") });
        view.Items.Add(new VisualMenuItem { Header = "运行", Command = () => SelectActivity("run") });
        view.Items.Add(new VisualMenuItem { Header = "MCP 服务器", Command = () => SelectActivity("mcp") });
        view.Items.Add(new VisualMenuItem { IsSeparator = true });
        view.Items.Add(new VisualMenuItem { Header = "属性", Command = () => SelectInspectorPage("properties") });
        view.Items.Add(new VisualMenuItem { Header = "AI 助手", Command = () => SelectInspectorPage("agent") });
        view.Items.Add(new VisualMenuItem { Header = "执行输出", Command = () => SelectMainTab("output") });
        view.Items.Add(new VisualMenuItem { IsSeparator = true });
        view.Items.Add(new VisualMenuItem { Header = "复制画布信息", Command = CopyHud });
        MenuBar.Items.Add(view);

        var help = new VisualMenuItem { Header = "帮助" };
        help.Items.Add(new VisualMenuItem
        {
            Header = "关于 VeloxDev",
            Command = () => _ = ShowMessageAsync("关于 VeloxDev", "VeloxDev — Workflow Studio\n演示宿主：工作流画布、运行门、Agent 与 MCP 面板。"),
        });
        MenuBar.Items.Add(help);
    }

    private void OnCommandCenter(object? sender, EventArgs e) => ShowCommandCenter(CommandButton);

    private void ShowCommandCenter(VisualElement? owner)
    {
        var menu = BuildCommandMenu();
        if (owner is not null)
        {
            var bounds = owner.AbsoluteBounds;
            menu.OpenAt(owner, new PointF(bounds.Left, bounds.Bottom));
        }
        else
        {
            menu.OpenAt(this, new PointF(330, 24));
        }
    }

    private VisualContextMenu BuildCommandMenu()
    {
        var menu = new VisualContextMenu();
        menu.Items.Add(new VisualMenuItem { Header = "保存工作流", InputGestureText = "Ctrl+S", Command = SaveWorkflow });
        menu.Items.Add(new VisualMenuItem { Header = "加载工作流", InputGestureText = "Ctrl+O", Command = () => _ = LoadWorkflowAsync() });
        menu.Items.Add(new VisualMenuItem { Header = "重新载入示例", Command = () => _ = ExecuteAsync(ReloadWorkflowAsync, "Failed to reset demo") });
        menu.Items.Add(new VisualMenuItem { IsSeparator = true });
        menu.Items.Add(new VisualMenuItem { Header = "撤销", InputGestureText = "Ctrl+Z", Command = () => _demo?.Tree.UndoCommand.Execute(null) });
        menu.Items.Add(new VisualMenuItem { Header = "重做", InputGestureText = "Ctrl+Y", Command = () => _demo?.Tree.RedoCommand.Execute(null) });
        menu.Items.Add(new VisualMenuItem { IsSeparator = true });
        menu.Items.Add(new VisualMenuItem { Header = "资源管理器", Command = () => SelectActivity("explorer") });
        menu.Items.Add(new VisualMenuItem { Header = "运行", Command = () => SelectActivity("run") });
        menu.Items.Add(new VisualMenuItem { Header = "MCP 服务器", Command = () => SelectActivity("mcp") });
        menu.Items.Add(new VisualMenuItem { Header = "属性", Command = () => SelectInspectorPage("properties") });
        menu.Items.Add(new VisualMenuItem { Header = "AI 助手", Command = () => SelectInspectorPage("agent") });
        menu.Items.Add(new VisualMenuItem { Header = "执行输出", Command = () => SelectMainTab("output") });
        return menu;
    }

    // ── Activity bar and shell pages ─────────────────────────────────────────

    private void BuildActivityBar()
    {
        AddActivity(TopActivity, "explorer", "资源");
        AddActivity(TopActivity, "run", "运行");
        AddActivity(TopActivity, "mcp", "MCP");
        AddActivity(TopActivity, "output", "输出");
        AddActivity(BottomActivity, "properties", "属性");
        AddActivity(BottomActivity, "agent", "AI");
    }

    private void AddActivity(VisualPanel host, string key, string label)
    {
        var button = new ActivityButton
        {
            Text = label,
            Height = 46,
            FontSize = 8.5,
            FontFamily = CardTheme.UiFamily,
        };
        button.Click += (_, _) => SelectActivity(key);
        _activityButtons[key] = button;
        host.Add(button);
    }

    private void SelectActivity(string key)
    {
        switch (key)
        {
            case "properties":
            case "agent":
                SelectInspectorPage(key);
                return;
            case "output":
                SelectMainTab("output");
                return;
        }

        _sidebarActivity = key;
        ExplorerPage.Visibility = key == "explorer" ? Visibility.Visible : Visibility.Collapsed;
        RunPage.Visibility = key == "run" ? Visibility.Visible : Visibility.Collapsed;
        McpPage.Visibility = key == "mcp" ? Visibility.Visible : Visibility.Collapsed;
        SidebarHeader.Text = key switch
        {
            "run" => "运行",
            "mcp" => "MCP 服务器",
            _ => "资源管理器",
        };

        UpdateActivityMarkers();
    }

    private void UpdateActivityMarkers()
    {
        foreach (var (name, button) in _activityButtons)
        {
            button.IsActive = name switch
            {
                "explorer" or "run" or "mcp" => _sidebarActivity == name,
                "output" => OutputTab.IsActive,
                "properties" or "agent" => _inspectorPage == name,
                _ => false,
            };
        }
    }

    private void OnCanvasTab(object? sender, EventArgs e) => SelectMainTab("canvas");

    private void OnOutputTab(object? sender, EventArgs e) => SelectMainTab("output");

    private void SelectMainTab(string view)
    {
        var output = view == "output";
        WorkflowSurface.Visibility = output ? Visibility.Collapsed : Visibility.Visible;
        OutputPage.Visibility = output ? Visibility.Visible : Visibility.Collapsed;
        CanvasTab.IsActive = !output;
        OutputTab.IsActive = output;

        if (!output)
        {
            WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(_host);
        }

        UpdateActivityMarkers();
    }

    private void OnPropertiesTab(object? sender, EventArgs e) => SelectInspectorPage("properties");

    private void OnAgentTab(object? sender, EventArgs e) => SelectInspectorPage("agent");

    private void SelectInspectorPage(string view)
    {
        _inspectorPage = view;
        var properties = view == "properties";
        PropertiesPage.Visibility = properties ? Visibility.Visible : Visibility.Collapsed;
        AgentPage.Visibility = properties ? Visibility.Collapsed : Visibility.Visible;
        PropertiesTab.IsActive = properties;
        AgentTab.IsActive = !properties;
        UpdateActivityMarkers();
    }

    // ── Session lifecycle ────────────────────────────────────────────────────

    private void LoadDemo(WorkflowDemoSession session)
    {
        if (_demo is not null)
        {
            DetachSurfaceInput();
            _demo.Controller.PropertyChanged -= OnControllerPropertyChanged;
            _demo.Tree.Nodes.CollectionChanged -= OnNodesCollectionChanged;
            _demo.Tree.CompiledStructure.CollectionChanged -= OnCompiledStructureChanged;
            _demo.Tree.PropertyChanged -= OnTreePropertyChanged;
            _demo.Tree.Runs.PropertyChanged -= OnRunsPropertyChanged;
            _demo.Tree.GetHelper().VisibleItems.CollectionChanged -= OnVisibleItemsChanged;
            UnsubscribeHelper(_demo);
            _demo.Dispose();
        }

        _demo = session;
        _demo.Controller.PropertyChanged += OnControllerPropertyChanged;
        _demo.Tree.Nodes.CollectionChanged += OnNodesCollectionChanged;
        _demo.Tree.CompiledStructure.CollectionChanged += OnCompiledStructureChanged;
        _demo.Tree.PropertyChanged += OnTreePropertyChanged;
        _demo.Tree.Runs.PropertyChanged += OnRunsPropertyChanged;
        _demo.Tree.GetHelper().VisibleItems.CollectionChanged += OnVisibleItemsChanged;
        SubscribeHelper(_demo);

        ExplorerPage.SetTree(_demo.Tree);
        ExplorerPage.SetSelectedElement(_demo.Tree.SelectedElement);
        PropertiesPage.Tree = _demo.Tree;
        RunPage.Catalog = _demo.Tree.Runs;
        OutputPage.SetSession(_demo);
        AgentPage.SetSession(_demo);
        McpPage.SetHelper(_demo.Tree.GetHelper() as AgentHelper);

        WorkflowBehaviors.WorkflowSurfaceBehavior.SetWorkflowTree(_host, _demo.Tree);
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(_host);
        AttachSurfaceInput(_demo.Tree);

        UpdateWorkbenchStatus();
    }

    private void SubscribeHelper(WorkflowDemoSession session)
    {
        if (session.Tree.GetHelper() is not AgentHelper helper)
        {
            return;
        }

        _surfaceRefresh ??= new CoalescedRefresh(
            () => WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(_host),
            action => _host.BeginInvoke(action));

        helper.SelectionHandler = ShowSelectionDialogAsync;
        helper.ConfirmationHandler = ShowConfirmationDialogAsync;
        helper.ToolCalled += OnAgentToolCalled;
        helper.VisualRefreshRequested += OnAgentToolCalled;
    }

    private void UnsubscribeHelper(WorkflowDemoSession session)
    {
        if (session.Tree.GetHelper() is not AgentHelper helper)
        {
            return;
        }

        helper.SelectionHandler = null;
        helper.ConfirmationHandler = null;
        helper.ToolCalled -= OnAgentToolCalled;
        helper.VisualRefreshRequested -= OnAgentToolCalled;
    }

    private void OnAgentToolCalled() => _surfaceRefresh?.Request();

    private async Task ReloadWorkflowAsync()
    {
        var previous = _demo;
        if (previous is not null)
        {
            _demo = null;
            await previous.Tree.GetHelper().CloseAsync();
            previous.Dispose();
        }

        LoadDemo(WorkflowDemoSession.Create());
    }

    private async Task ExecuteAsync(Func<Task> action, string title)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(title, ex.Message);
        }
    }

    // ── Files ────────────────────────────────────────────────────────────────

    private void SaveWorkflow()
    {
        if (_demo is null)
        {
            return;
        }

        using var dialog = new FolderBrowserDialog();
        if (dialog.ShowDialog(_host) == DialogResult.OK)
        {
            var path = System.IO.Path.Combine(dialog.SelectedPath, "Workflow.json");
            _demo.Tree.SaveCommand.Execute(path);
            _ = ShowMessageAsync("保存成功", $"Workflow Saved At {dialog.SelectedPath}");
        }
    }

    private async Task LoadWorkflowAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择工作流文件",
            Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            DefaultExt = ".json",
            CheckFileExists = true,
            CheckPathExists = true,
        };

        if (dialog.ShowDialog(_host) != DialogResult.OK)
        {
            return;
        }

        try
        {
            using var stream = System.IO.File.OpenRead(dialog.FileName);
            using var reader = new System.IO.StreamReader(stream);
            var json = await reader.ReadToEndAsync();

            var result = json.Deserialize<TreeViewModel>();
            if (result is null
                || result.Layout is null
                || result.Nodes is null
                || result.Links is null
                || result.VirtualLink is null
                || result.ExecutionLog is null
                || result.AgentLog is null
                || result.CompiledStructure is null)
            {
                await ShowMessageAsync("Error", "Invalid workflow file: required fields are missing or null.");
                return;
            }

            result.Layout = result.Layout.AdaptTo(new VeloxDev.WorkflowSystem.Size(1920, 1080));
            LoadDemo(WorkflowDemoSession.FromTree(result));
            await ShowMessageAsync("Load Succeeded", $"Workflow loaded successfully from {dialog.FileName}.");
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Error", $"Failed to load file: {ex.GetType().Name}\n{ex.Message}");
        }
    }

    // ── Copy HUD / minimap ───────────────────────────────────────────────────

    private void CopyHud()
    {
        var lines = WorkflowSurface.InfoOverlay.Lines;
        if (lines.Count == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, lines));
            StatusBar.HintLabel.Text = "已复制画布信息";
        }
        catch (ExternalException)
        {
            StatusBar.HintLabel.Text = "复制失败：剪贴板被占用";
        }
        catch (ThreadStateException)
        {
            StatusBar.HintLabel.Text = "复制失败：剪贴板不可用";
        }
    }

    private void SetMinimapVisible(bool visible)
    {
        var overlay = WorkflowSurface.MinimapOverlay;
        overlay.IsMinimapVisible = visible;
        overlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        overlay.InvalidateVisual();
        ExplorerPage.SetMinimapState(visible);
    }

    // ── Status ───────────────────────────────────────────────────────────────

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ControllerViewModel.IsActive))
        {
            WorkbenchDispatcher.Post(UpdateWorkbenchStatus);
        }
    }

    private void OnNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => WorkbenchDispatcher.Post(UpdateWorkbenchStatus);

    private void OnVisibleItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => WorkbenchDispatcher.Post(UpdateWorkbenchStatus);

    private void OnCompiledStructureChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => WorkbenchDispatcher.Post(UpdateWorkbenchStatus);

    private void OnRunsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => WorkbenchDispatcher.Post(UpdateWorkbenchStatus);

    private void OnTreePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        WorkbenchDispatcher.Post(() =>
        {
            UpdateWorkbenchStatus();
            if (e.PropertyName is nameof(TreeViewModel.SelectedElement) or nameof(TreeViewModel.HasSelectedElement))
            {
                ExplorerPage.SetSelectedElement(_demo?.Tree.SelectedElement);
            }
        });
    }

    private void UpdateWorkbenchStatus()
    {
        var tree = _demo?.Tree;
        var running = tree?.IsWorkflowRunning == true;

        StatusBar.SetState(running);
        StatusBar.CompiledLabel.Text = $"编译 {tree?.CompiledStructure.Count ?? 0} 段";
        StatusBar.ElementsLabel.Text = $"元素 {tree?.TotalElementText ?? "0"}";
        StatusBar.VisibleLabel.Text = $"可见 {tree?.VisibleElementText ?? "0"}";
        MainHint.Text = $"Ctrl+滚轮缩放 · {tree?.TotalElementText ?? "0"}";
        UpdateZoom();
    }

    private void UpdateZoom()
    {
        var scale = WorkflowBehaviors.WorkflowSurfaceBehavior.GetZoom(_host);
        if (scale <= 0d)
        {
            scale = 1d;
        }

        StatusBar.ZoomLabel.Text = $"{scale * 100d:0}%";
    }

    // ── Workflow surface input ───────────────────────────────────────────────

    private void AttachSurfaceInput(Wf.ITreeComponent tree)
    {
        DetachSurfaceInput();

        var input = Wf.WorkflowInput.For(tree);
        _surfaceInput = input;
        if (tree.GetHelper() is Wf.IInputEvents events)
        {
            events.Input.PointerPressed += OnSurfacePointerPressed;
            events.Input.KeyDown += OnSurfaceKeyDown;
            events.Input.PointerWheelChanged += OnSurfacePointerWheel;
        }
    }

    private void DetachSurfaceInput()
    {
        if (_surfaceInput is not { } input)
        {
            return;
        }

        if (input.Tree.GetHelper() is Wf.IInputEvents events)
        {
            events.Input.PointerPressed -= OnSurfacePointerPressed;
            events.Input.KeyDown -= OnSurfaceKeyDown;
            events.Input.PointerWheelChanged -= OnSurfacePointerWheel;
        }

        // Clear the routed hover target before the tree goes away; otherwise the last link stays lit.
        input.Route(new Wf.PointerExitedEventArgs(
            new Wf.Anchor(), Wf.InputModifiers.None, this, null, new Wf.WorkflowEventHandle()));
        _surfaceInput = null;
    }

    private void OnSurfacePointerPressed(object? sender, Wf.PointerPressedEventArgs e)
    {
        if (e.Handle.PreventDefault)
        {
            return;
        }

        if (e.Button == Wf.MouseButton.Right
            && e.Target is Wf.INodeComponent node
            && ShowNodeLayoutMenu(node, e.Position))
        {
            e.Handle.PreventDefault = true;
            return;
        }

        if (e.Modifiers.HasFlag(Wf.InputModifiers.Control))
        {
            if (e.Target is Wf.INodeComponent selected)
            {
                AgentSelection.Toggle(selected);
            }

            e.Handle.PreventDefault = true;
        }
        else if (e.Target is null && e.Modifiers.HasFlag(Wf.InputModifiers.Shift))
        {
            // Shift-drag on the empty canvas belongs to the host.
            e.Handle.PreventDefault = true;
        }
    }

    private void OnSurfaceKeyDown(object? sender, Wf.KeyDownEventArgs e)
    {
        if (_surfaceInput is not { } input || e.Handle.PreventDefault)
        {
            return;
        }

        if (e.Key == Wf.InputKey.Delete)
        {
            if (input.HoveredLink is not { } hovered)
            {
                return;
            }

            if (hovered.DeleteCommand.CanExecute(null))
            {
                hovered.DeleteCommand.Execute(null);
            }

            e.Handle.PreventDefault = true;
            return;
        }

        if (!e.Modifiers.HasFlag(Wf.InputModifiers.Control))
        {
            return;
        }

        var redo = e.Modifiers.HasFlag(Wf.InputModifiers.Shift);
        if (e.Key == Wf.InputKey.Z && !redo)
        {
            input.Tree.UndoCommand.Execute(null);
        }
        else if (e.Key == Wf.InputKey.Y || (e.Key == Wf.InputKey.Z && redo))
        {
            input.Tree.RedoCommand.Execute(null);
        }
        else
        {
            return;
        }

        e.Handle.PreventDefault = true;
    }

    private void OnSurfacePointerWheel(object? sender, Wf.PointerWheelEventArgs e)
    {
        if (!e.Modifiers.HasFlag(Wf.InputModifiers.Shift) || _surfaceInput is not { } input)
        {
            return;
        }

        e.Handle.PreventDefault = true;
        input.Scroller?.ScrollBy(-e.DeltaY, 0d);
    }

    // ── Node right-click layout menu ─────────────────────────────────────────

    private bool ShowNodeLayoutMenu(Wf.INodeComponent node, Wf.Anchor position)
    {
        var surfacePoint = WorkflowBehaviors.WorkflowSurfaceBehavior.WorldToSurface(
            _host,
            new PointF((float)position.Horizontal, (float)position.Vertical));

        var hit = _host.FindElementAt(surfacePoint);
        if (IsInsideTextInput(hit))
        {
            return false;
        }

        _nodeMenuTarget = node;
        var menu = EnsureNodeMenu();
        RefreshToleranceChecks();
        menu.Refresh();
        menu.OpenAt(ResolveNodeView(hit, node) ?? (VisualElement)WorkflowSurface, surfacePoint);
        return true;
    }

    private static VisualElement? ResolveNodeView(VisualElement? hit, Wf.INodeComponent node)
    {
        for (var current = hit; current is not null; current = current.ParentElement)
        {
            if (ReferenceEquals(current.DataContext, node))
            {
                return current;
            }
        }

        return null;
    }

    private static bool IsInsideTextInput(VisualElement? hit)
    {
        for (var current = hit; current is not null; current = current.ParentElement)
        {
            if (current is VisualTextBox or VisualRichTextBox)
            {
                return true;
            }

            if (current is WorkflowSurfaceView)
            {
                break;
            }
        }

        return false;
    }

    private VisualContextMenu EnsureNodeMenu()
    {
        if (_nodeMenu is not null)
        {
            return _nodeMenu;
        }

        var menu = new VisualContextMenu();

        var layout = new VisualMenuItem { Header = "自动布局" };
        foreach (var (label, traversal) in WorkflowLayoutDemo.Traversals)
        {
            var item = new VisualMenuItem { Header = label };
            item.Command = () => ApplyNodeLayout(traversal);
            layout.Items.Add(item);
        }

        menu.Items.Add(layout);
        menu.Items.Add(new VisualMenuItem { IsSeparator = true });

        var tolerance = new VisualMenuItem { Header = "布局容差" };
        foreach (var (label, value) in WorkflowLayoutDemo.Tolerances)
        {
            var item = new VisualMenuItem { Header = label };
            item.Command = () =>
            {
                _nodeLayoutTolerance = value;
                RefreshToleranceChecks();
                StatusBar.HintLabel.Text = $"布局容差 = {value}";
            };
            _toleranceItems.Add((item, value));
            tolerance.Items.Add(item);
        }

        menu.Items.Add(tolerance);
        _nodeMenu = menu;
        RefreshToleranceChecks();
        return menu;
    }

    private void RefreshToleranceChecks()
    {
        foreach (var (item, value) in _toleranceItems)
        {
            item.IsChecked = Math.Abs(_nodeLayoutTolerance - value) < 1e-9d;
        }
    }

    private void ApplyNodeLayout(WorkflowLayoutTraversal traversal)
    {
        if (_nodeMenuTarget is not { } node)
        {
            return;
        }

        try
        {
            var result = WorkflowLayoutDemo.Arrange(node, traversal, _nodeLayoutTolerance);
            StatusBar.HintLabel.Text = WorkflowLayoutDemo.Describe(result, $"从 {node.GetType().Name}");
            WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(_host);
        }
        catch (Exception ex)
        {
            StatusBar.HintLabel.Text = $"布局失败：{ex.Message}";
        }
    }

    // ── Agent dialogs (in-app overlay) ───────────────────────────────────────

    private Task ShowSelectionDialogAsync(AgentSelectionEventArgs args)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WorkbenchDispatcher.Post(() =>
        {
            var view = new AgentSelectionView();
            view.Configure(args.Prompt, args.Options, args.FreeTextPrompt, args.AllowMultiSelect);
            view.Completed += (_, _) =>
            {
                if (!view.Cancelled)
                {
                    args.SelectedOption = view.ChosenOption;
                    args.SelectedOptions = view.ChosenOptions;
                    args.FreeTextResponse = view.FreeTextResponse;
                }

                CloseDialog(view);
                completion.TrySetResult();
            };
            OpenDialog(view);
        });
        return completion.Task;
    }

    private Task ShowConfirmationDialogAsync(AgentConfirmationEventArgs args)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WorkbenchDispatcher.Post(() =>
        {
            var view = new AgentConfirmationView();
            view.Configure(args.OperationKey, args.Description);
            view.Completed += (_, _) =>
            {
                args.Result = view.Result;
                CloseDialog(view);
                completion.TrySetResult();
            };
            OpenDialog(view);
        });
        return completion.Task;
    }

    private Task ShowMessageAsync(string title, string body)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WorkbenchDispatcher.Post(() =>
        {
            var view = new MessageView();
            view.SetMessage(title, body);
            view.Dismissed += (_, _) =>
            {
                CloseDialog(view);
                completion.TrySetResult();
            };
            OpenDialog(view);
        });
        return completion.Task;
    }

    private void OpenDialog(VisualElement view)
    {
        if (_dialogOpen)
        {
            CloseDialog(DialogContent.Content as VisualElement);
        }

        _dialogOpen = true;
        DialogContent.Content = view;
        DialogOverlay.Visibility = Visibility.Visible;
    }

    private void CloseDialog(VisualElement? view)
    {
        if (!ReferenceEquals(DialogContent.Content, view))
        {
            return;
        }

        DialogContent.Content = null;
        DialogOverlay.Visibility = Visibility.Collapsed;
        _dialogOpen = false;
        view?.Dispose();
    }

    // ── Keyboard shortcuts ───────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnKeyDown(VisualKeyEventArgs args)
    {
        var control = (args.Modifiers & Keys.Control) != 0;
        var shift = (args.Modifiers & Keys.Shift) != 0;

        if (control && shift && args.KeyCode == Keys.P)
        {
            ShowCommandCenter(CommandButton);
            args.Handled = true;
            return;
        }

        if (control && !shift)
        {
            switch (args.KeyCode)
            {
                case Keys.S:
                    SaveWorkflow();
                    args.Handled = true;
                    return;
                case Keys.O:
                    _ = LoadWorkflowAsync();
                    args.Handled = true;
                    return;
                case Keys.Y:
                    // 画布焦点时这两键已由表面的 Core 路由处理，外壳再执行会撤销两次。
                    if (IsFocusInsideSurface())
                    {
                        break;
                    }

                    _demo?.Tree.RedoCommand.Execute(null);
                    args.Handled = true;
                    return;
                case Keys.Z:
                    if (IsFocusInsideSurface() || _host.FocusedElement is VisualTextBox or VisualRichTextBox)
                    {
                        break;
                    }

                    _demo?.Tree.UndoCommand.Execute(null);
                    args.Handled = true;
                    return;
            }
        }

        base.OnKeyDown(args);
    }

    private bool IsFocusInsideSurface()
    {
        for (var current = _host.FocusedElement; current is not null; current = current.ParentElement)
        {
            if (ReferenceEquals(current, WorkflowSurface))
            {
                return true;
            }
        }

        return false;
    }
}
