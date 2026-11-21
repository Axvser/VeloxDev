using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Workflow;
using Microsoft.Maui.Dispatching;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using VeloxDev.AI;
using VeloxDev.AI.Safety;
using VeloxDev.AI.SubAgents;
using VeloxDev.MVVM;
using VeloxDev.Serialization;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;
using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

public partial class WorkflowView : ContentView
{
    private static readonly Color AccentColor = Color.FromArgb("#0078D4");
    private static readonly Color ActivityActiveColor = Color.FromArgb("#14FFFFFF");

    private TreeViewModel _workflowViewModel = new();
    private readonly WorkflowElementCatalog _elements = new();
    private bool _layoutRefreshPending;

    private bool _syncingElementSelection;
    private WorkflowElementKind _elementKind = WorkflowElementKind.Node;

    // The sub-agent panel is a projection of the helper's scope, rebuilt by the view. It keeps its
    // own flat list because MAUI has no hierarchical tree control.
    private SubAgentTreeViewModel? _subAgentTree;
    private IDispatcherTimer? _subAgentTick;
    private readonly ObservableCollection<SubAgentTreeNodeViewModel> _subAgentRows = [];
    private readonly HashSet<SubAgentTreeNodeViewModel> _subAgentHooked = [];

    private Page MainPage => Application.Current?.Windows[0].Page ?? throw new InvalidOperationException();

    public WorkflowView()
    {
        InitializeComponent();
        LayoutMenu.StatusSink = status => ElementStatus.Text = status;

        // Keep the canvas-info HUD current on every scroll / viewport change (it reads helper.Viewport,
        // which the surface behavior refreshes; the model events cover scale / visible counts).
        PART_ScrollViewer.Scrolled += (_, _) => InfoOverlay.Update();
        PART_ScrollViewer.SizeChanged += (_, _) => InfoOverlay.Update();

        _elements.Changed += (_, _) => ElementStatus.Text = _elements.Summary;
        _elements.SetTree(_workflowViewModel);
        ElementStatus.Text = _elements.Summary;

        SelectElementKind(WorkflowElementKind.Node);
        ShowSidebarPage("explorer");
        ShowInspectorPage("properties");
        SelectMainTab("canvas");
        MinimapToggle.IsToggled = PART_MinimapOverlay.IsMinimapVisible;
    }

    // ── Shell: panels, tabs, activity bar ───────────────────────────────────

    private static void SetTabVisual(Label label, BoxView underline, bool active)
    {
        label.TextColor = active ? Color.FromArgb("#E7E7E7") : Color.FromArgb("#6E7681");
        underline.Color = active ? AccentColor : Colors.Transparent;
    }

    private static void SetActivityVisual(Grid host, BoxView indicator, bool active)
    {
        host.BackgroundColor = active ? ActivityActiveColor : Colors.Transparent;
        indicator.Color = active ? AccentColor : Colors.Transparent;
    }

    private void OnActivityTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not string view) return;

        if (view == "output")
        {
            SelectMainTab("output");
            return;
        }

        SetSidebarVisible(true);
        ShowSidebarPage(view);
    }

    private void OnInspectorActivityTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not string view) return;

        SetInspectorVisible(true);
        ShowInspectorPage(view);
    }

    private void ShowSidebarPage(string view)
    {
        ExplorerPanel.IsVisible = view == "explorer";
        RunPanel.IsVisible = view == "run";
        McpPanel.IsVisible = view == "mcp";

        SidebarHeader.Text = view switch
        {
            "run" => "运行",
            "mcp" => "MCP 服务器",
            _ => "资源管理器",
        };

        SetActivityVisual(ActivityExplorer, ActivityExplorerIndicator, view == "explorer");
        SetActivityVisual(ActivityRun, ActivityRunIndicator, view == "run");
        SetActivityVisual(ActivityMcp, ActivityMcpIndicator, view == "mcp");
    }

    private void ShowInspectorPage(string view)
    {
        PropertiesPanel.IsVisible = view == "properties";
        AgentPanel.IsVisible = view == "agent";

        SetTabVisual(PropertiesTabLabel, PropertiesTabUnderline, view == "properties");
        SetTabVisual(AgentTabLabel, AgentTabUnderline, view == "agent");
        SetActivityVisual(ActivityProperties, ActivityPropertiesIndicator, view == "properties");
        SetActivityVisual(ActivityAgent, ActivityAgentIndicator, view == "agent");
    }

    private void SelectMainTab(string view)
    {
        WorkflowContainer.IsVisible = view == "canvas";
        OutputPanel.IsVisible = view == "output";

        SetTabVisual(CanvasTabLabel, CanvasTabUnderline, view == "canvas");
        SetTabVisual(OutputTabLabel, OutputTabUnderline, view == "output");
        SetActivityVisual(ActivityOutput, ActivityOutputIndicator, view == "output");

        // The log grows while this page is hidden; MAUI's CollectionView does not apply a scroll
        // request made against an unmeasured list, so follow the tail again on the way in.
        if (view == "output") ScrollExecutionLogToEnd();
    }

    private void ScrollExecutionLogToEnd()
    {
        var count = _workflowViewModel.ExecutionLog.Count;
        if (count == 0) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            ScrollExecutionLogToTail(count);
            // 第一次请求可能在隐藏的 CollectionView 还没被测量时到达（尤其是运行结束后才切到输出页）；
            // 等这一轮布局落定再补一次，尾行才真的在视口里。
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(80), () => ScrollExecutionLogToTail(count));
        });
    }

    private void ScrollExecutionLogToTail(int count)
    {
        try { ExecutionLogScroller?.ScrollTo(count - 1, position: ScrollToPosition.End, animate: false); }
        catch { /* UI exceptions from auto-scroll are non-fatal */ }
    }

    private void OnMainTabTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string view) SelectMainTab(view);
    }

    private void OnInspectorTabTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string view) ShowInspectorPage(view);
    }

    private void OnToggleSidebar(object? sender, EventArgs e)
        => SetSidebarVisible(!SidebarHost.IsVisible);

    private void OnToggleInspector(object? sender, EventArgs e)
        => SetInspectorVisible(!InspectorHost.IsVisible);

    private void SetSidebarVisible(bool visible)
    {
        SidebarHost.IsVisible = visible;
        WorkbenchGrid.ColumnDefinitions[1].Width = new GridLength(visible ? 272 : 0);
        WorkbenchGrid.ColumnDefinitions[2].Width = new GridLength(visible ? 1 : 0);
    }

    private void SetInspectorVisible(bool visible)
    {
        InspectorHost.IsVisible = visible;
        WorkbenchGrid.ColumnDefinitions[4].Width = new GridLength(visible ? 1 : 0);
        WorkbenchGrid.ColumnDefinitions[5].Width = new GridLength(visible ? 348 : 0);
    }

    // MAUI has no menu-bar control and Button has no click-opened Flyout, so each title-strip menu
    // opens the platform action sheet. The command centre is the same list without a section title.
    private async void OnFileMenu(object? sender, EventArgs e)
        => await ShowMenuAsync("文件", "保存工作流", "加载工作流", "重新载入示例", "退出");

    private async void OnEditMenu(object? sender, EventArgs e)
        => await ShowMenuAsync("编辑", "撤销", "重做", "命令中心");

    private async void OnViewMenu(object? sender, EventArgs e)
        => await ShowMenuAsync("视图", "资源管理器", "运行", "MCP 服务器", "属性", "AI 助手", "执行输出", "切换侧栏", "切换右侧面板");

    private async void OnHelpMenu(object? sender, EventArgs e)
        => await ShowMenuAsync("帮助", "关于 VeloxDev");

    private async void OnOpenCommandCenter(object? sender, TappedEventArgs e)
        => await ShowMenuAsync("命令中心",
            "保存工作流", "加载工作流", "重新载入示例", "撤销", "重做",
            "资源管理器", "运行", "MCP 服务器", "属性", "AI 助手", "执行输出");

    private async Task ShowMenuAsync(string title, params string[] commands)
    {
        var choice = await MainPage.DisplayActionSheetAsync(title, "取消", null, commands);
        if (choice is null || choice == "取消") return;
        ExecuteMenuCommand(choice);
    }

    private void ExecuteMenuCommand(string command)
    {
        switch (command)
        {
            case "保存工作流":
                OnSaveClicked(null, EventArgs.Empty);
                break;
            case "加载工作流":
                OnSelectClicked(null, EventArgs.Empty);
                break;
            case "重新载入示例":
                LoadNetworkDemo();
                break;
            case "退出":
                Application.Current?.Quit();
                break;
            case "撤销":
                OnUndoClicked(null, EventArgs.Empty);
                break;
            case "重做":
                OnRedoClicked(null, EventArgs.Empty);
                break;
            case "命令中心":
                _ = ShowMenuAsync("命令中心",
                    "保存工作流", "加载工作流", "重新载入示例", "撤销", "重做",
                    "资源管理器", "运行", "MCP 服务器", "属性", "AI 助手", "执行输出");
                break;
            case "资源管理器":
            case "运行":
            case "MCP 服务器":
                SetSidebarVisible(true);
                ShowSidebarPage(command switch
                {
                    "运行" => "run",
                    "MCP 服务器" => "mcp",
                    _ => "explorer",
                });
                break;
            case "属性":
            case "AI 助手":
                SetInspectorVisible(true);
                ShowInspectorPage(command == "属性" ? "properties" : "agent");
                break;
            case "执行输出":
                SelectMainTab("output");
                break;
            case "切换侧栏":
                SetSidebarVisible(!SidebarHost.IsVisible);
                break;
            case "切换右侧面板":
                SetInspectorVisible(!InspectorHost.IsVisible);
                break;
            case "关于 VeloxDev":
                OnShowAbout(null, EventArgs.Empty);
                break;
        }
    }

    private void OnMinimapToggled(object? sender, ToggledEventArgs e)
        => PART_MinimapOverlay.IsMinimapVisible = e.Value;

    // ── Element catalog ─────────────────────────────────────────────────────

    private void OnElementKindTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string kind && Enum.TryParse<WorkflowElementKind>(kind, true, out var parsed))
            SelectElementKind(parsed);
    }

    private void SelectElementKind(WorkflowElementKind kind)
    {
        _elementKind = kind;

        _syncingElementSelection = true;
        try
        {
            ElementList.ItemsSource = kind switch
            {
                WorkflowElementKind.Node => _elements.Nodes,
                WorkflowElementKind.Slot => _elements.Slots,
                _ => _elements.Links,
            };

            ElementList.SelectedItem =
                _workflowViewModel.SelectedElement is { } selected && selected.Kind == kind ? selected : null;
        }
        finally
        {
            _syncingElementSelection = false;
        }

        SetTabVisual(ElementNodeTabLabel, ElementNodeTabUnderline, kind == WorkflowElementKind.Node);
        SetTabVisual(ElementSlotTabLabel, ElementSlotTabUnderline, kind == WorkflowElementKind.Slot);
        SetTabVisual(ElementLinkTabLabel, ElementLinkTabUnderline, kind == WorkflowElementKind.Link);
    }

    private void OnElementSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingElementSelection) return;
        if (ElementList.SelectedItem is WorkflowElementEntry entry)
            _workflowViewModel.SelectedElement = entry;
    }

    // The Windows CollectionView reports a tap on its item content as a child-control press, not as
    // a selection, so the row itself claims the tap. The action buttons still take their own clicks.
    private void OnElementRowTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as View)?.BindingContext is not WorkflowElementEntry entry) return;
        if (ReferenceEquals(_workflowViewModel.SelectedElement, entry)) return;

        _workflowViewModel.SelectedElement = entry;

        _syncingElementSelection = true;
        try { ElementList.SelectedItem = entry; }
        finally { _syncingElementSelection = false; }
    }

    private void SyncElementSelection()
    {
        if (_syncingElementSelection) return;

        _syncingElementSelection = true;
        try
        {
            ElementList.SelectedItem =
                _workflowViewModel.SelectedElement is { } selected && selected.Kind == _elementKind
                    ? selected
                    : null;
        }
        finally
        {
            _syncingElementSelection = false;
        }
    }

    private void OnWorkflowViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TreeViewModel.SelectedElement))
            SyncElementSelection();
    }

    private void OnLocateElement(object? sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not WorkflowElementEntry entry) return;

        ElementStatus.Text = _elements.Locate(entry)
            ? $"已定位 {entry.Label}"
            : "没有可用的表面，或该元素还没有有限坐标";
    }

    private void OnDeleteElement(object? sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not WorkflowElementEntry entry) return;
        DeleteElement(entry);
    }

    private void OnLocateSelectedElement(object? sender, EventArgs e)
    {
        if (_workflowViewModel.SelectedElement is not { } entry) return;

        ElementStatus.Text = _elements.Locate(entry)
            ? $"已定位 {entry.Label}"
            : "没有可用的表面，或该元素还没有有限坐标";
    }

    private void OnDeleteSelectedElement(object? sender, EventArgs e)
    {
        if (_workflowViewModel.SelectedElement is { } entry) DeleteElement(entry);
    }

    private void DeleteElement(WorkflowElementEntry entry)
    {
        _elements.Delete(entry);
        if (ReferenceEquals(_workflowViewModel.SelectedElement, entry))
            _workflowViewModel.SelectedElement = null;
        ElementStatus.Text = $"已删除 {entry.Label}";
    }

    // ── Run manager ─────────────────────────────────────────────────────────

    private static WorkflowRunTaskViewModel? RunTaskOf(object? sender)
        => (sender as Button)?.BindingContext as WorkflowRunTaskViewModel;

    private void OnPauseRunTask(object? sender, EventArgs e) => RunTaskOf(sender)?.Pause();

    private void OnResumeRunTask(object? sender, EventArgs e) => RunTaskOf(sender)?.Resume();

    private void OnStopRunTask(object? sender, EventArgs e) => RunTaskOf(sender)?.Stop();

    private void OnStartRunTask(object? sender, EventArgs e) => RunTaskOf(sender)?.Start();

    private void OnRemoveRunTask(object? sender, EventArgs e)
    {
        if (RunTaskOf(sender) is { } task) _workflowViewModel.Runs.Remove(task);
    }

    private void OnClearFinishedRuns(object? sender, EventArgs e)
        => _workflowViewModel.Runs.ClearFinished();

    // Same reason as the element rows: tapping the card body must select the task, not only the
    // CollectionView's own container. The per-row action buttons keep their own clicks.
    private void OnRunRowTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as View)?.BindingContext is not WorkflowRunTaskViewModel task) return;
        _workflowViewModel.Runs.Selected = task;
        RunTaskList.SelectedItem = task;
    }

    // ── Session wiring ──────────────────────────────────────────────────────

    public static readonly BindableProperty SessionProperty = BindableProperty.Create(
        nameof(Session),
        typeof(WorkflowDemoSession),
        typeof(WorkflowView),
        null,
        propertyChanged: OnSessionChanged);

    public WorkflowDemoSession? Session
    {
        get => (WorkflowDemoSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    private static void OnSessionChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var view = (WorkflowView)bindable;
        view.AttachSession((WorkflowDemoSession?)oldValue, (WorkflowDemoSession?)newValue);
    }

    private void AttachSession(WorkflowDemoSession? oldSession, WorkflowDemoSession? newSession)
    {
        if (oldSession is not null)
        {
            UnsubscribeAutoScroll(oldSession.Tree);
        }

        _workflowViewModel = newSession?.Tree ?? new TreeViewModel();
        _elements.SetTree(_workflowViewModel);
        HookLinkKeys(_workflowViewModel);
        VetoFrameworkGestures(_workflowViewModel);

        // MAUI propagates BindingContext through the visual tree automatically,
        // so setting it on the ContentView root is sufficient. Do NOT set
        // BindingContext on individual child elements — that breaks the natural
        // inheritance chain and can cause missed binding updates.
        BindingContext = _workflowViewModel;
        // Propagate the tree to the HUD explicitly so its BindingContextChanged fires even if
        // inheritance does not reach the nested overlay.
        InfoOverlay.BindingContext = _workflowViewModel;

        SyncLinkInput();
        SelectElementKind(_elementKind);
        ShowSidebarPage(RunPanel.IsVisible ? "run" : McpPanel.IsVisible ? "mcp" : "explorer");

        if (newSession is not null)
        {
            SubscribeAutoScroll(newSession.Tree);
            if (newSession.Tree.GetHelper() is AgentHelper helper)
            {
                helper.Mcp.WithSynchronizationContext(SynchronizationContext.Current);
                _ = helper.LoadMcpServersAsync();
            }
            // 会话在这里才到，所以权限模式的初值也得在这里取 —— 构造函数里还没有 helper 可读。
            InitializePermissionMode();
            newSession.Tree.Layout.UpdateCommand.Execute(null);
        }
    }

    private void LoadNetworkDemo()
    {
        var session = WorkflowDemoSession.Create();
        Session = session;
    }

    private async void OnSelectClicked(object? sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择工作流文件",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, [".json"] },
                    { DevicePlatform.Android, ["application/json"] },
                    { DevicePlatform.iOS, ["public.json"] },
                    { DevicePlatform.MacCatalyst, ["public.json"] },
                }),
            });

            if (result is null) return;

            using var stream = await result.OpenReadAsync();
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            var success = json.TryDeserialize<TreeViewModel>(out var tree);

            if (!success || tree is null)
            {
                await MainPage.DisplayAlertAsync("Load Failed", "The file format is invalid or could not be parsed.", "OK");
                return;
            }

            tree.Layout.UpdateCommand.Execute(null);

            var session = WorkflowDemoSession.FromTree(tree);
            Session = session;
        }
        catch (Exception ex)
        {
            await MainPage.DisplayAlertAsync("Error", $"Failed to load file: {ex.Message}", "OK");
        }
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        try
        {
            var filePath = Path.Combine(FileSystem.AppDataDirectory, "Workflow.json");
            if (_workflowViewModel.SaveCommand is ICommand cmd)
                cmd.Execute(filePath);
            await MainPage.DisplayAlertAsync("Save Succeeded", $"Workflow saved to: {filePath}", "OK");
        }
        catch (Exception ex)
        {
            await MainPage.DisplayAlertAsync("Error", $"Failed to save file: {ex.Message}", "OK");
        }
    }

    private void OnLoadNetworkDemoClicked(object? sender, EventArgs e) => LoadNetworkDemo();

    private void OnUndoClicked(object? sender, EventArgs e)
        => _workflowViewModel.UndoCommand.Execute(null);

    private void OnRedoClicked(object? sender, EventArgs e)
        => _workflowViewModel.RedoCommand.Execute(null);

    private void OnExitClicked(object? sender, EventArgs e)
        => Application.Current?.Quit();

    private async void OnShowAbout(object? sender, EventArgs e)
        => await MainPage.DisplayAlertAsync(
            "VeloxDev — Workflow Studio",
            "演示宿主：工作流画布、运行门、Agent 与 MCP 面板都在这一套外壳里。",
            "OK");

    /// <summary>
    /// Fills the permission-mode picker with every mode, and starts it on the one the helper was built with.
    /// </summary>
    /// <remarks>
    /// Selecting the current mode fires the change handler, which sets the same mode again —
    /// <c>SetPermissionMode</c> reports that nothing moved and does nothing. Cheaper than arranging not to.
    /// <para>
    /// Re-run on every tree swap rather than once at construction: a tree loaded from a file arrives with its
    /// own helper at that helper's default mode, and a picker still showing the previous one would be a lie
    /// about the session on screen.
    /// </para>
    /// </remarks>
    private void InitializePermissionMode()
    {
        if (_workflowViewModel.GetHelper() is not AgentHelper helper) return;

        PermissionModePicker.ItemsSource = Enum.GetValues<AgentPermissionMode>();
        PermissionModePicker.SelectedItem = helper.PermissionMode;
    }

    /// <summary>
    /// Moves the session to the mode the user picked. There is nothing to rebuild: the gate reads the policy
    /// per call, so the very next tool call obeys the new mode, and the prompt is re-rendered next turn.
    /// </summary>
    private void OnPermissionModeChanged(object? sender, EventArgs e)
    {
        if (PermissionModePicker.SelectedItem is not AgentPermissionMode mode) return;
        if (_workflowViewModel.GetHelper() is not AgentHelper helper) return;

        helper.PermissionMode = mode;
        helper.Scope?.SetPermissionMode(mode);
    }

    private void SubscribeAutoScroll(TreeViewModel vm)
    {
        vm.PropertyChanged += OnWorkflowViewModelPropertyChanged;
        vm.AgentLog.CollectionChanged += OnAgentLogChanged;
        vm.ExecutionLog.CollectionChanged += OnExecutionLogChanged;
        if (vm.GetHelper() is AgentHelper helper)
        {
            helper.SelectionHandler = ShowSelectionDialogAsync;
            helper.ConfirmationHandler = ShowConfirmationDialogAsync;
            helper.ToolCalled += OnAgentToolCalled;
            helper.VisualRefreshRequested += OnVisualRefreshRequested;

            AttachSubAgents(helper);
        }
    }

    private void UnsubscribeAutoScroll(TreeViewModel vm)
    {
        vm.PropertyChanged -= OnWorkflowViewModelPropertyChanged;
        vm.AgentLog.CollectionChanged -= OnAgentLogChanged;
        vm.ExecutionLog.CollectionChanged -= OnExecutionLogChanged;
        if (vm.GetHelper() is AgentHelper helper)
        {
            helper.SelectionHandler = null;
            helper.ConfirmationHandler = null;
            helper.ToolCalled -= OnAgentToolCalled;
            helper.VisualRefreshRequested -= OnVisualRefreshRequested;
        }

        // Outside the guard on purpose: the panel belongs to this view, so it goes when the view lets go of
        // the tree — whether or not the helper turned out to be an AgentHelper.
        DetachSubAgents();
    }

    private async void OnAgentLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        try
        {
            await Task.Yield();
            if (_workflowViewModel.AgentLog is { Count: > 0 } log)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try { AgentLogScroller?.ScrollTo(log.Count - 1, position: ScrollToPosition.End, animate: false); }
                    catch { /* UI exceptions from auto-scroll are non-fatal */ }
                });
            }
        }
        catch
        {
            // Swallow cross-thread setup exceptions.
        }
    }

    // ── Sub-agents ──────────────────────────────────────────────────────────

    /// <summary>
    /// Attaches the sub-agent panel to a helper's subsystem. The subsystem is built inside
    /// <c>ProvideAgent</c>, so it is looked for at attach time rather than assumed, and a host without
    /// a key simply has no panel.
    /// </summary>
    private void AttachSubAgents(AgentHelper helper)
    {
        DetachSubAgents();

        if (helper.SubAgents is not { } scope)
        {
            SubAgentPanel.IsVisible = false;
            return;
        }

        // Built here, on the UI thread, and not in the constructor: the tree captures the context it
        // marshals its rebuilds to, so it has to be born on the thread that will render it.
        _subAgentTree = new SubAgentTreeViewModel(scope);
        SubAgentPanel.BindingContext = _subAgentTree;
        SubAgentPanel.IsVisible = true;
        SubAgentList.ItemsSource = _subAgentRows;
        RebuildSubAgentRows();
        StartSubAgentTick();
    }

    private void RebuildSubAgentRows()
    {
        foreach (var node in _subAgentHooked)
        {
            node.Children.CollectionChanged -= OnSubAgentChildrenChanged;
            node.PropertyChanged -= OnSubAgentNodeChanged;
        }
        _subAgentHooked.Clear();
        _subAgentRows.Clear();

        if (_subAgentTree is null) return;

        AddSubAgentRow(_subAgentTree.ScopeRoot);
    }

    private void AddSubAgentRow(SubAgentTreeNodeViewModel node)
    {
        _subAgentRows.Add(node);
        node.Children.CollectionChanged += OnSubAgentChildrenChanged;
        node.PropertyChanged += OnSubAgentNodeChanged;
        _subAgentHooked.Add(node);

        if (!node.IsExpanded) return;
        foreach (var child in node.Children) AddSubAgentRow(child);
    }

    private void OnSubAgentChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RebuildSubAgentRows();

    private void OnSubAgentNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SubAgentTreeNodeViewModel.IsExpanded))
            RebuildSubAgentRows();
    }

    private void StartSubAgentTick()
    {
        _subAgentTick ??= Dispatcher.CreateTimer();
        _subAgentTick.Interval = TimeSpan.FromSeconds(1);
        _subAgentTick.Tick -= OnSubAgentTick;
        _subAgentTick.Tick += OnSubAgentTick;
        _subAgentTick.Start();
    }

    private void OnSubAgentTick(object? sender, EventArgs e) => _subAgentTree?.TickElapsed();

    private void StopSubAgentTick()
    {
        if (_subAgentTick is null) return;
        _subAgentTick.Stop();
        _subAgentTick.Tick -= OnSubAgentTick;
    }

    private void DetachSubAgents()
    {
        StopSubAgentTick();

        foreach (var node in _subAgentHooked)
        {
            node.Children.CollectionChanged -= OnSubAgentChildrenChanged;
            node.PropertyChanged -= OnSubAgentNodeChanged;
        }
        _subAgentHooked.Clear();
        _subAgentRows.Clear();

        SubAgentPanel.BindingContext = null;
        SubAgentPanel.IsVisible = false;
        _subAgentTree?.Dispose();
        _subAgentTree = null;
    }

    // ── Agent interaction dialogs ───────────────────────────────────────────

    private Task ShowSelectionDialogAsync(AgentSelectionEventArgs args)
        => MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var isMulti = args.AllowMultiSelect;

            var page = new ContentPage
            {
                BackgroundColor = Color.FromArgb("#1a1a2e"),
                Title = isMulti ? "Agent · 请多选" : "Agent · 请选择",
            };

            var stack = new VerticalStackLayout
            {
                Spacing = 8,
                Padding = new Thickness(20),
            };

            var promptLabel = new Label
            {
                Text = args.Prompt,
                TextColor = Color.FromArgb("#e0e0e0"),
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 8),
            };
            stack.Children.Add(promptLabel);

            List<CheckBox>? checkBoxes = isMulti ? [] : null;
            var freeTextEntry = new Entry
            {
                BackgroundColor = Color.FromArgb("#2d2d2d"),
                TextColor = Colors.White,
                Placeholder = "（可选）",
                PlaceholderColor = Color.FromArgb("#666666"),
            };

            foreach (var opt in args.Options)
            {
                if (isMulti)
                {
                    var cb = new CheckBox { Color = Color.FromArgb("#7ec8ff") };
                    var label = new Label { Text = opt, TextColor = Color.FromArgb("#e0e0e0"), FontSize = 13, VerticalOptions = LayoutOptions.Center };
                    var row = new HorizontalStackLayout { Spacing = 8 };
                    row.Children.Add(cb);
                    row.Children.Add(label);
                    checkBoxes!.Add(cb);
                    stack.Children.Add(row);
                }
                else
                {
                    var captured = opt;
                    var btn = new Button
                    {
                        Text = opt,
                        BackgroundColor = Color.FromArgb("#0f3460"),
                        TextColor = Color.FromArgb("#e0e0e0"),
                        BorderColor = Color.FromArgb("#7ec8ff"),
                        BorderWidth = 1, CornerRadius = 6, HeightRequest = 40,
                        HorizontalOptions = LayoutOptions.Fill,
                    };
                    btn.Clicked += (_, _) =>
                    {
                        args.SelectedOption = captured;
                        args.FreeTextResponse = string.IsNullOrWhiteSpace(freeTextEntry.Text?.Trim()) ? null : freeTextEntry.Text.Trim();
                        _ = MainPage.Navigation.PopModalAsync(true);
                    };
                    stack.Children.Add(btn);
                }
            }

            stack.Children.Add(new Label { Text = args.FreeTextPrompt, TextColor = Color.FromArgb("#b0b0b0"), FontSize = 11, Margin = new Thickness(0, 6, 0, 2) });
            stack.Children.Add(freeTextEntry);

            if (isMulti)
            {
                var confirmBtn = new Button
                {
                    Text = "确认选择",
                    BackgroundColor = Color.FromArgb("#0f3460"),
                    TextColor = Color.FromArgb("#7ec8ff"),
                    BorderColor = Color.FromArgb("#7ec8ff"),
                    BorderWidth = 1, CornerRadius = 6, HeightRequest = 40, Margin = new Thickness(0, 10, 0, 0),
                };
                confirmBtn.Clicked += (_, _) =>
                {
                    args.SelectedOptions = checkBoxes!.Where(cb => cb.IsChecked)
                        .Select(cb => (string)((Label)((HorizontalStackLayout)cb.Parent).Children[1]).Text).ToList();
                    args.FreeTextResponse = string.IsNullOrWhiteSpace(freeTextEntry.Text?.Trim()) ? null : freeTextEntry.Text.Trim();
                    _ = MainPage.Navigation.PopModalAsync(true);
                };
                stack.Children.Add(confirmBtn);

                var cancelBtn = new Button { Text = "取消", BackgroundColor = Color.FromArgb("#2a2a3e"), TextColor = Color.FromArgb("#888888"), BorderColor = Color.FromArgb("#444444"), BorderWidth = 1, CornerRadius = 6, HeightRequest = 36 };
                cancelBtn.Clicked += (_, _) => _ = MainPage.Navigation.PopModalAsync(true);
                stack.Children.Add(cancelBtn);
            }
            else
            {
                var submitBtn = new Button { Text = "使用输入", IsEnabled = false, BackgroundColor = Color.FromArgb("#0f3460"), TextColor = Color.FromArgb("#7ec8ff"), BorderColor = Color.FromArgb("#7ec8ff"), BorderWidth = 1, CornerRadius = 6, HeightRequest = 36, Margin = new Thickness(0, 8, 0, 0) };
                freeTextEntry.TextChanged += (_, _) => submitBtn.IsEnabled = !string.IsNullOrWhiteSpace(freeTextEntry.Text);
                submitBtn.Clicked += (_, _) =>
                {
                    args.SelectedOption = null;
                    args.FreeTextResponse = string.IsNullOrWhiteSpace(freeTextEntry.Text?.Trim()) ? null : freeTextEntry.Text.Trim();
                    _ = MainPage.Navigation.PopModalAsync(true);
                };
                stack.Children.Add(submitBtn);

                var cancelBtn = new Button { Text = "取消（不选择）", BackgroundColor = Color.FromArgb("#2a2a3e"), TextColor = Color.FromArgb("#888888"), BorderColor = Color.FromArgb("#444444"), BorderWidth = 1, CornerRadius = 6, HeightRequest = 36, Margin = new Thickness(0, 8, 0, 0) };
                cancelBtn.Clicked += (_, _) =>
                {
                    args.SelectedOption = null;
                    args.FreeTextResponse = string.IsNullOrWhiteSpace(freeTextEntry.Text?.Trim()) ? null : freeTextEntry.Text.Trim();
                    _ = MainPage.Navigation.PopModalAsync(true);
                };
                stack.Children.Add(cancelBtn);
            }

            page.Content = new ScrollView { Content = stack };
            await MainPage.Navigation.PushModalAsync(page, true);
        });

    private Task ShowConfirmationDialogAsync(AgentConfirmationEventArgs args)
        => MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var allow = await MainPage.DisplayAlertAsync(
                "Agent · 操作确认", $"[Operation] {args.OperationKey}\n\n{args.Description}", "允许", "拒绝");

            if (!allow) { args.Result = AgentConfirmationResult.Deny; return; }

            args.Result = await MainPage.DisplayAlertAsync(
                "Agent · 授权范围", "是否在本次会话中始终允许该操作？", "始终允许", "仅同意一次")
                ? AgentConfirmationResult.AllowAlways : AgentConfirmationResult.AllowOnce;
        });

    private void OnAgentToolCalled() => ScheduleRefresh();
    private void OnVisualRefreshRequested() => ScheduleRefresh();

    private void OnExecutionLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScheduleRefresh();
        ScrollExecutionLogToEnd();
    }

    /// <summary>
    /// Debounces layout refreshes to avoid flooding MAUI's layout system.
    /// MAUI layout passes are expensive — batch them.
    /// </summary>
    private void ScheduleRefresh()
    {
        if (_layoutRefreshPending) return;
        _layoutRefreshPending = true;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                _layoutRefreshPending = false;
                WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
            }
            catch
            {
                // Swallow layout refresh exceptions during active processing.
            }
        });
    }

    // ── Agent chat ──────────────────────────────────────────────────────────

    private async void OnReloadMcp(object? sender, EventArgs e)
    {
        if (_workflowViewModel.GetHelper() is AgentHelper helper)
            await helper.LoadMcpServersAsync();
    }

    private async void OnSendToAgent(object? sender, EventArgs e)
    {
        var text = AgentInput?.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        AgentInput!.Text = string.Empty;

        try
        {
            if (_workflowViewModel.AskCommand is IVeloxCommand cmd)
                await cmd.ExecuteAsync(text);
            else
                _workflowViewModel.AskCommand.Execute(text);
        }
        catch (Exception ex)
        {
            _workflowViewModel.AppendAgentError($"Send failed: {ex.Message}");
        }
    }

    private void OnAgentInputCompleted(object? sender, EventArgs e)
    {
        OnSendToAgent(sender, e);
    }

    // VeloxDev customization: 悬停高亮是这本 demo 的。overlay 默认什么都不画，订阅树的输入事件、
    // 把「现在轮到哪条线」交给它，它才照给定颜色画那一条 —— 换色/换画法都在这里改。
    private IInputEvents? _linkInput;

    private void SyncLinkInput()
    {
        if (_linkInput is not null)
        {
            _linkInput = null;
        }

        if (BindingContext is ITreeComponent tree && tree.GetHelper() is IInputEvents events)
        {
            _linkInput = events;
        }
    }

    // VeloxDev customization: Delete 归宿主 —— 库只把按键路由过来（target 就是指针停着的那条线），
    // 删不删由这里写（与悬停高亮同一条路）。
    private void HookLinkKeys(ITreeComponent tree)
    {
        if (tree.GetHelper() is not IInputEvents events) return;

        events.Input.KeyDown += (_, e) =>
        {
            if (e.Key != InputKey.Delete || e.Handle.PreventDefault) return;
            if (e.Target is not ILinkComponent link || !link.DeleteCommand.CanExecute(null)) return;

            link.DeleteCommand.Execute(null);
        };
    }

    // VeloxDev customization: 宿主自留的手势 —— 空白画布上 Shift+拖拽、落在任何地方的 Ctrl+拖拽。
    // 框架那一手（平移、节点拖动、插槽连线）在动手前都读这一笔的句柄，所以一次订阅同时覆盖空白处与卡片上。
    // 滚轮不在这张名单里：普通滚轮会以「汇报」的身份到达同一个订阅，但没有哪家适配器读它的裁决；
    // 框架真正执行到底的滚轮手势只有 Ctrl+滚轮缩放。
    // 订阅按**树实例**去重，而且用一张集合而不是「上一棵」：本 demo 的会话会被换上好几轮（A → B → A），
    // 只比上一棵的话回到 A 时会再订一遍 —— 同一棵树上挂两个订阅，每笔输入就送达两次（实测：滚动一格、
    // 日志两行、args 是同一个实例）。每换一次树都重订一次，与自动滚动同理。
    private static readonly HashSet<TreeViewModel> ProbedTrees = [];

    private static void VetoFrameworkGestures(TreeViewModel tree)
    {
        if (!ProbedTrees.Add(tree))
        {
            return;
        }

        ((IInputEvents)tree.GetHelper()).Input.PointerPressed += (_, e) =>
        {
            if (e.Modifiers.HasFlag(InputModifiers.Control))
            {
                if (e.Target is INodeComponent node) AgentSelection.Toggle(node);
                e.Handle.PreventDefault = true;
            }
            else if (e.Target is null && e.Modifiers.HasFlag(InputModifiers.Shift))
            {
                e.Handle.PreventDefault = true;
            }
        };
    }
}
