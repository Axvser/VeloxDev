using VeloxDev.WorkflowSystem;
using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Workflow;
using Microsoft.Win32;
using System.Collections.Specialized;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VeloxDev.AI;
using VeloxDev.AI.Safety;
using VeloxDev.Serialization;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;
using PlatformInput = System.Windows.Input;
using Wf = VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

public partial class WorkflowView : UserControl
{
    private TreeViewModel _workflowViewModel = new();

    // 当前树背后的会话：持有日志文件句柄与控制器上的会话配置。换树时先释放，
    // 否则下一棵树第一次运行会去开同一个 log 文件（FileShare.Read）而失败。
    private WorkflowDemoSession? _session;
    private double _nodeLayoutTolerance = 8d;

    public WorkflowView()
    {
        InitializeComponent();

        // Keep the canvas-info HUD current on every scroll / viewport change. The overlay subscribes to
        // the Core model itself (scale / actual size / visible items); the scroll and content offsets come
        // from the grid decorator's DPs, which WorkflowSurfaceBehavior pushes on this same event.
        PART_ScrollViewer.ScrollChanged += (_, _) => InfoOverlay.Refresh();
        AddHandler(PreviewMouseUpEvent, new MouseButtonEventHandler(OnNodeRightButtonUp), true);

        DataContext = _workflowViewModel;
        ElementList.Tree = _workflowViewModel;
        InitializeNetworkDemo();
        InitializeMcp();
    }

    /// <summary>Switches the left sidebar between the explorer, run controls and MCP views.</summary>
    private void OnActivityClick(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string view }) return;

        SetSidebarVisible(true);
        ShowSidebarPage(view);
    }

    private void ShowSidebarPage(string view)
    {
        ExplorerPanel.Visibility = view == "explorer" ? Visibility.Visible : Visibility.Collapsed;
        RunPanel.Visibility = view == "run" ? Visibility.Visible : Visibility.Collapsed;
        McpPanel.Visibility = view == "mcp" ? Visibility.Visible : Visibility.Collapsed;

        SidebarHeader.Text = view switch
        {
            "run" => "运行",
            "mcp" => "MCP 服务器",
            _ => "资源管理器",
        };
    }

    private void OnInspectorActivityClick(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string view }) return;

        SetInspectorVisible(true);
        ShowInspectorPage(view);
    }

    private void OnRightTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string view }) return;
        ShowInspectorPage(view);
    }

    private void ShowInspectorPage(string view)
    {
        PropertiesPanel.Visibility = view == "properties" ? Visibility.Visible : Visibility.Collapsed;
        AgentPanel.Visibility = view == "agent" ? Visibility.Visible : Visibility.Collapsed;
        PropertiesTab.IsChecked = view == "properties";
        AgentTab.IsChecked = view == "agent";
        InspectorPropertiesActivity.IsChecked = view == "properties";
        InspectorAgentActivity.IsChecked = view == "agent";
    }

    private void OnMainTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string view }) return;
        SelectMainTab(view);
    }

    private void SelectMainTab(string view)
    {
        WorkflowContainer.Visibility = view == "canvas" ? Visibility.Visible : Visibility.Collapsed;
        OutputPanel.Visibility = view == "output" ? Visibility.Visible : Visibility.Collapsed;
        CanvasTab.IsChecked = view == "canvas";
        OutputTab.IsChecked = view == "output";
        OutputActivity.IsChecked = view == "output";
    }

    private void OnOutputActivityClick(object sender, RoutedEventArgs e)
        => SelectMainTab("output");

    private void OnShowPanelMenu(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string view }) return;

        switch (view)
        {
            case "properties":
            case "agent":
                SetInspectorVisible(true);
                ShowInspectorPage(view);
                break;
            case "output":
                SelectMainTab("output");
                break;
            default:
                SetSidebarVisible(true);
                ShowSidebarPage(view);
                ExplorerActivity.IsChecked = view == "explorer";
                RunActivity.IsChecked = view == "run";
                McpActivity.IsChecked = view == "mcp";
                break;
        }
    }

    private void OnToggleSidebar(object sender, RoutedEventArgs e)
        => SetSidebarVisible(SidebarHost.Visibility != Visibility.Visible);

    private void OnToggleInspector(object sender, RoutedEventArgs e)
        => SetInspectorVisible(InspectorHost.Visibility != Visibility.Visible);

    private void SetSidebarVisible(bool visible)
    {
        SidebarHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        LeftSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        WorkbenchGrid.ColumnDefinitions[1].Width = new GridLength(visible ? 272 : 0);
        WorkbenchGrid.ColumnDefinitions[2].Width = new GridLength(visible ? 4 : 0);
    }

    private void SetInspectorVisible(bool visible)
    {
        InspectorHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        RightSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        WorkbenchGrid.ColumnDefinitions[4].Width = new GridLength(visible ? 4 : 0);
        WorkbenchGrid.ColumnDefinitions[5].Width = new GridLength(visible ? 348 : 0);
    }

    private void OnOpenCommandCenter(object? sender, RoutedEventArgs e)
    {
        if (CommandCenterButton.ContextMenu is not { } menu) return;
        menu.PlacementTarget = CommandCenterButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OnShowAbout(object sender, RoutedEventArgs e)
        => MessageBox.Show(
            "演示宿主：工作流画布、运行门、Agent 与 MCP 面板都在这一套外壳里。",
            "VeloxDev — Workflow Studio",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private void OnGlobalKeyDown(object sender, PlatformInput.KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;

        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        switch (e.Key)
        {
            case PlatformInput.Key.P when shift:
                OnOpenCommandCenter(null, new RoutedEventArgs());
                e.Handled = true;
                break;
            case PlatformInput.Key.S:
                SaveWorkflow(null, new RoutedEventArgs());
                e.Handled = true;
                break;
            case PlatformInput.Key.O:
                SelectWorkflow(null, new RoutedEventArgs());
                e.Handled = true;
                break;
            case PlatformInput.Key.Z:
                ExecuteHistory(undo: true);
                e.Handled = true;
                break;
            case PlatformInput.Key.Y:
                ExecuteHistory(undo: false);
                e.Handled = true;
                break;
        }
    }

    private void ExecuteHistory(bool undo)
    {
        var command = undo ? _workflowViewModel.UndoCommand : _workflowViewModel.RedoCommand;
        if (command.CanExecute(null)) command.Execute(null);
    }

    // Custom title bar: drag from empty space, double-click toggles maximize. Buttons and menu items
    // handle their own clicks, so the press is ignored when it originates inside either.
    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            (FindAncestor<ButtonBase>(source) is not null || FindAncestor<MenuItem>(source) is not null))
            return;

        if (Window.GetWindow(this) is not { } window) return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize(window);
            return;
        }

        window.DragMove();
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void OnMinimizeWindow(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window)
            window.WindowState = WindowState.Minimized;
    }

    private void OnMaximizeWindow(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window)
            ToggleMaximize(window);
    }

    private void OnCloseWindow(object sender, RoutedEventArgs e)
        => Window.GetWindow(this)?.Close();

    private static void ToggleMaximize(Window window)
        => window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

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
    private void OnPermissionModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PermissionModePicker.SelectedItem is not AgentPermissionMode mode) return;
        if (_workflowViewModel.GetHelper() is not AgentHelper helper) return;

        helper.PermissionMode = mode;
        helper.Scope?.SetPermissionMode(mode);
    }

    private void InitializeMcp()
    {
        if (_workflowViewModel.GetHelper() is not AgentHelper helper) return;

        // Marshal status updates to the UI thread (ObservableCollection bindings require the same thread).
        helper.Mcp.WithSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher));
        McpStatusPanel.DataContext = helper.Mcp.Status;
        _ = helper.LoadMcpServersAsync();
    }

    private async void OnReloadMcp(object sender, RoutedEventArgs e)
    {
        if (_workflowViewModel.GetHelper() is AgentHelper helper)
            await helper.LoadMcpServersAsync();
    }

    private async void SelectWorkflow(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择工作流文件",
            Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            DefaultExt = ".json",
            CheckFileExists = true,
            CheckPathExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                using var stream = File.OpenRead(dialog.FileName);
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();
                var result = json.Deserialize<TreeViewModel>();
                // The surface restores the saved viewport position by itself when the tree is attached below.
                result.Layout.UpdateCommand.Execute(null);

                UnsubscribeAutoScroll(_workflowViewModel);
                _session?.Dispose();

                // 从文件来的树也要有会话配置，否则控制器上的 Compile/Run 没有日志、观察者与运行管理器行。
                _session = WorkflowDemoSession.FromTree(result);
                _workflowViewModel = result;
                DataContext = _workflowViewModel;
                ElementList.Tree = _workflowViewModel;
                SubscribeAutoScroll(_workflowViewModel);
        VetoFrameworkGestures(_workflowViewModel);
                WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
                InitializePermissionMode();

                MessageBox.Show($"Workflow loaded successfully from {dialog.FileName}.", "Load succeeded",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load file: {ex.GetType().Name}\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void SaveWorkflow(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TreeViewModel tree) return;

        var dialog = new OpenFolderDialog();

        if (dialog.ShowDialog() == true)
        {
            tree.SaveCommand.Execute(Path.Combine(dialog.FolderName, "Workflow.json"));
            MessageBox.Show($"Workflow Saved At {dialog.FolderName}", "OK");
        }
    }

    private void LoadNetworkDemo(object sender, RoutedEventArgs e)
    {
        InitializeNetworkDemo();
    }

    private void InitializeNetworkDemo()
    {
        UnsubscribeAutoScroll(_workflowViewModel);
        _session?.Dispose();
        _session = WorkflowDemoSession.Create();
        _workflowViewModel = _session.Tree;
        HookLinkKeys(_workflowViewModel);
        DataContext = _workflowViewModel;
        ElementList.Tree = _workflowViewModel;
        SubscribeAutoScroll(_workflowViewModel);
        VetoFrameworkGestures(_workflowViewModel);
        _workflowViewModel.Layout.UpdateCommand.Execute(null);
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
        InitializePermissionMode();
    }

    // Agent 一轮里每个工具调用都要求刷新，而一次刷新是全量的（重新定位命名控件、跑布局、重算可见区）。
    // 合并之后一轮只刷新一次 —— 控制器本身是纯的，后来的请求要的正是前面那次刷新即将看到的状态。
    private CoalescedRefresh? _surfaceRefresh;

    // ── Run manager ──────────────────────────────────────────────────────────

    private static WorkflowRunTaskViewModel? RunTaskOf(object sender)
        => (sender as FrameworkElement)?.DataContext as WorkflowRunTaskViewModel;

    private void OnPauseRunTask(object sender, RoutedEventArgs e) => RunTaskOf(sender)?.Pause();

    private void OnResumeRunTask(object sender, RoutedEventArgs e) => RunTaskOf(sender)?.Resume();

    private void OnStopRunTask(object sender, RoutedEventArgs e) => RunTaskOf(sender)?.Stop();

    private void OnStartRunTask(object sender, RoutedEventArgs e) => RunTaskOf(sender)?.Start();

    private void OnRemoveRunTask(object sender, RoutedEventArgs e)
    {
        if (RunTaskOf(sender) is { } task) _workflowViewModel.Runs.Remove(task);
    }

    private void OnClearFinishedRuns(object sender, RoutedEventArgs e)
        => _workflowViewModel.Runs.ClearFinished();

    private void OnLocateSelectedElement(object sender, RoutedEventArgs e)
    {
        if (_workflowViewModel.SelectedElement is not { } entry) return;

        ElementStatus.Text = ElementList.Locate(entry)
            ? $"已定位 {entry.Label}"
            : "没有可用的表面，或该元素还没有有限坐标";
    }

    private void OnDeleteSelectedElement(object sender, RoutedEventArgs e)
    {
        if (_workflowViewModel.SelectedElement is not { } entry) return;

        ElementList.Delete(entry);
        ElementStatus.Text = $"已删除 {entry.Label}";
    }

    private void OnSendToAgent(object sender, RoutedEventArgs e)
    {
        var text = AgentInput?.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        _workflowViewModel.AskCommand.Execute(text);
        AgentInput!.Text = string.Empty;
    }

    private void OnAgentInputKeyDown(object sender, PlatformInput.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnSendToAgent(sender, e);
            e.Handled = true;
        }
    }

    /// <summary>
    /// The presses this host has claimed: Shift-drag on the empty canvas, and Ctrl-drag anywhere — the
    /// framework's own hand stands down and the host's takes over.
    /// </summary>
    /// <remarks>
    /// Subscribe, test the condition, set <c>PreventDefault</c> — the whole starting point of a press-and-drag
    /// interaction of one's own. One subscription covers the blank canvas and the cards alike, because the refusal
    /// is read wherever the framework's hand would have started: pan, node drag and slot connection. The wheel is
    /// not on that list — a plain wheel reaches this same subscription as a report and no adapter reads its
    /// verdict, while Ctrl+wheel zoom is the only wheel gesture the framework itself runs. Re-subscribed on every
    /// tree swap, the same way auto-scroll is: the tree on screen is not the one the constructor started with.
    /// </remarks>
    private static void VetoFrameworkGestures(TreeViewModel tree)
    {
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

    // Right-click menus must open on the UP event: a ContextMenu opened on PreviewMouseDown is dismissed
    // by the corresponding MouseUp before the user sees it. The Core input route still owns the hit test,
    // but WPF's own bubbling event is the stable place to pop a native menu.
    private void OnNodeRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != PlatformInput.MouseButton.Right) return;
        if (IsInsideTextInput(e.OriginalSource as DependencyObject)) return;

        var element = FindNodeElement(e.OriginalSource as DependencyObject);
        if (element?.DataContext is not INodeComponent node) return;

        ShowNodeLayoutMenu(node, element);
        e.Handled = true;
    }

    private static bool IsInsideTextInput(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TextBoxBase or PasswordBox or RichTextBox) return true;
            if (current is WorkflowView) break;
        }

        return false;
    }

    private static FrameworkElement? FindNodeElement(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement element && element.DataContext is INodeComponent)
            {
                return element;
            }

            if (current is WorkflowView) break;
        }

        return null;
    }

    /// <summary>Demo-only: the node's own right-click menu, built from the shared layout presets.</summary>
    private void ShowNodeLayoutMenu(INodeComponent node, UIElement? source)
    {
        var menu = new ContextMenu();

        var layout = new MenuItem { Header = "自动布局" };
        foreach (var (label, traversal) in WorkflowLayoutDemo.Traversals)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => ApplyNodeLayout(node, traversal);
            layout.Items.Add(item);
        }
        menu.Items.Add(layout);

        var tolerance = new MenuItem { Header = "布局容差" };
        foreach (var (label, value) in WorkflowLayoutDemo.Tolerances)
        {
            var item = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = Math.Abs(_nodeLayoutTolerance - value) < 1e-9,
            };
            item.Click += (_, _) => _nodeLayoutTolerance = value;
            tolerance.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(tolerance);

        menu.PlacementTarget = source ?? this;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void ApplyNodeLayout(INodeComponent node, WorkflowLayoutTraversal traversal)
    {
        try
        {
            var result = WorkflowLayoutDemo.Arrange(node, traversal, _nodeLayoutTolerance);
            ElementStatus.Text = WorkflowLayoutDemo.Describe(result, $"从 {node.GetType().Name}");
            WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
        }
        catch (Exception ex)
        {
            ElementStatus.Text = $"布局失败：{ex.Message}";
        }
    }

    private void SubscribeAutoScroll(TreeViewModel vm)
    {
        vm.AgentLog.CollectionChanged += OnAgentLogChanged;
        vm.ExecutionLog.CollectionChanged += OnExecutionLogChanged;
        if (vm.GetHelper() is AgentHelper helper)
        {
            // 一个 surface 一个合并器：换树会重新走到这里，但合并器跟着视图走，不该重建。
            _surfaceRefresh ??= new CoalescedRefresh(
                () => WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this),
                action => Dispatcher.InvokeAsync(action, System.Windows.Threading.DispatcherPriority.Background));

            helper.SelectionHandler = ShowSelectionDialogAsync;
            helper.ConfirmationHandler = ShowConfirmationDialogAsync;
            helper.ToolCalled += OnAgentToolCalled;
            helper.VisualRefreshRequested += OnVisualRefreshRequested;
        }
    }

    private void UnsubscribeAutoScroll(TreeViewModel vm)
    {
        vm.AgentLog.CollectionChanged -= OnAgentLogChanged;
        vm.ExecutionLog.CollectionChanged -= OnExecutionLogChanged;
        if (vm.GetHelper() is AgentHelper helper)
        {
            helper.SelectionHandler = null;
            helper.ConfirmationHandler = null;
            helper.ToolCalled -= OnAgentToolCalled;
            helper.VisualRefreshRequested -= OnVisualRefreshRequested;
        }
    }

    private void OnAgentToolCalled() => _surfaceRefresh?.Request();

    private void OnVisualRefreshRequested() => _surfaceRefresh?.Request();

    private async Task ShowSelectionDialogAsync(AgentSelectionEventArgs args)
    {
        var result = await Dispatcher.InvokeAsync<SelectionDialogResult>(() =>
        {
            var prompt = args.Prompt;
            var options = args.Options;
            var isMulti = args.AllowMultiSelect;
            var win = new System.Windows.Window
            {
                Title = isMulti ? "Agent · 请多选" : "Agent · 请选择",
                Width = 460,
                SizeToContent = System.Windows.SizeToContent.Height,
                MaxHeight = 620,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a1a2e")),
            };

            // ── Header ──
            var headerStack = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
            headerStack.Children.Add(new TextBlock
            {
                Text = isMulti ? "Agent · 请多选" : "Agent · 请选择",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7ec8ff")),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = prompt,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e0e0e0")),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0),
            });
            var header = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16213e")),
                Child = headerStack,
            };

            // ── Options area ──
            var optionStack = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            // Track UI controls for reading state on submit
            List<CheckBox>? checkBoxes = isMulti ? [] : null;
            string? singleChoice = null;

            foreach (var opt in options)
            {
                if (isMulti)
                {
                    var cb = new CheckBox
                    {
                        Content = opt,
                        IsChecked = false,
                        Margin = new Thickness(0, 0, 0, 6),
                        Padding = new Thickness(8, 6, 8, 6),
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e0e0e0")),
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0f3460")),
                        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7ec8ff")),
                        BorderThickness = new Thickness(1),
                        FontSize = 12,
                    };
                    checkBoxes!.Add(cb);
                    optionStack.Children.Add(cb);
                }
                else
                {
                    var captured = opt;
                    var btn = new System.Windows.Controls.Button
                    {
                        Content = opt,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(14, 10, 14, 10),
                        FontSize = 12,
                        Margin = new Thickness(0, 0, 0, 6),
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0f3460")),
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e0e0e0")),
                        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7ec8ff")),
                        BorderThickness = new Thickness(1),
                        Cursor = Cursors.Hand,
                    };
                    btn.Click += (_, _) => { singleChoice = captured; win.Close(); };
                    optionStack.Children.Add(btn);
                }
            }

            // ── Free text input (always shown) ──
            optionStack.Children.Add(new TextBlock
            {
                Text = args.FreeTextPrompt,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#b0b0b0")),
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 4),
            });
            var freeTextBox = new TextBox
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2d2d2d")),
                Foreground = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 12,
                AcceptsReturn = false,
                MinHeight = 30,
            };
            optionStack.Children.Add(freeTextBox);

            // ── Confirm / Cancel buttons ──
            if (isMulti)
            {
                var btnRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 10, 0, 0),
                };
                var cancelBtn = new System.Windows.Controls.Button
                {
                    Content = "取消",
                    Padding = new Thickness(14, 8, 14, 8),
                    FontSize = 11,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2a2a3e")),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444444")),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 6, 0),
                };
                cancelBtn.Click += (_, _) => win.Close();
                btnRow.Children.Add(cancelBtn);

                var confirmBtn = new System.Windows.Controls.Button
                {
                    Content = "确认选择",
                    Padding = new Thickness(14, 8, 14, 8),
                    FontSize = 11,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0f3460")),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7ec8ff")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7ec8ff")),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                };
                confirmBtn.Click += (_, _) => win.Close();
                btnRow.Children.Add(confirmBtn);
                optionStack.Children.Add(btnRow);
            }
            else
            {
                var submitBtn = new System.Windows.Controls.Button
                {
                    Content = "使用输入",
                    IsEnabled = false,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Padding = new Thickness(14, 8, 14, 8),
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 0, 6),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0f3460")),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7ec8ff")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7ec8ff")),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                };
                freeTextBox.TextChanged += (_, _) => submitBtn.IsEnabled = !string.IsNullOrWhiteSpace(freeTextBox.Text);
                submitBtn.Click += (_, _) => { singleChoice = null; win.Close(); };
                optionStack.Children.Add(submitBtn);

                var cancelBtn = new System.Windows.Controls.Button
                {
                    Content = "取消（不选择）",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Padding = new Thickness(14, 8, 14, 8),
                    FontSize = 11,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2a2a3e")),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444444")),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                };
                cancelBtn.Click += (_, _) => win.Close();
                optionStack.Children.Add(cancelBtn);
            }

            var scroll = new System.Windows.Controls.ScrollViewer
            {
                MaxHeight = 420,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                Content = optionStack,
            };

            var root = new StackPanel
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a1a2e")),
            };
            root.Children.Add(header);
            root.Children.Add(scroll);
            win.Content = root;

            var owner = System.Windows.Application.Current?.MainWindow;
            if (owner is not null) win.Owner = owner;
            win.ShowDialog();

            // Collect results
            List<string>? selectedOptions = isMulti
                ? checkBoxes!.Where(cb => cb.IsChecked == true).Select(cb => (string)cb.Content).ToList()
                : null;
            var selectedSingle = isMulti ? null : singleChoice;
            var freeText = freeTextBox?.Text?.Trim();
            return new SelectionDialogResult
            {
                SelectedOption = selectedSingle,
                SelectedOptions = selectedOptions,
                FreeTextResponse = string.IsNullOrWhiteSpace(freeText) ? null : freeText,
            };
        });

        args.SelectedOption = result.SelectedOption;
        args.SelectedOptions = result.SelectedOptions;
        args.FreeTextResponse = result.FreeTextResponse;
    }

    /// <summary>
    /// Internal helper to carry selection dialog results out of the Dispatcher call.
    /// </summary>
    private sealed class SelectionDialogResult
    {
        public string? SelectedOption { get; init; }
        public IReadOnlyList<string>? SelectedOptions { get; init; }
        public string? FreeTextResponse { get; init; }
    }

    private async Task ShowConfirmationDialogAsync(AgentConfirmationEventArgs args)
    {
        var result = await Dispatcher.InvokeAsync<AgentConfirmationResult>(() =>
        {
            var operationKey = args.OperationKey;
            var description = args.Description;
            var result = AgentConfirmationResult.Deny;

            var win = new System.Windows.Window
            {
                Title = "Agent · 操作确认",
                Width = 440,
                SizeToContent = System.Windows.SizeToContent.Height,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a1a2e")),
            };

            // Header
            var headerStack = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
            headerStack.Children.Add(new TextBlock
            {
                Text = "Agent · 操作确认",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffd166")),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
            });
            headerStack.Children.Add(new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2a1f00")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffd166")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 4, 0, 0),
                Child = new TextBlock
                {
                    Text = operationKey,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffd166")),
                    FontSize = 11,
                    FontFamily = new FontFamily("Consolas"),
                },
            });
            var header = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16213e")),
                Child = headerStack,
            };

            // Body
            var bodyStack = new StackPanel { Margin = new Thickness(18, 14, 18, 14), MinWidth = 300 };
            bodyStack.Children.Add(new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e0e0e0")),
                FontSize = 12,
            });

            static System.Windows.Controls.Button MakeBtn(string label, string bg, string fg, string border) =>
                new()
                {
                    Content = label,
                    Padding = new Thickness(16, 9, 16, 9),
                    FontSize = 12,
                    Margin = new Thickness(4, 0, 0, 0),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg)),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg)),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(border)),
                    BorderThickness = new Thickness(1),
                };

            var denyBtn   = MakeBtn("拒绝",            "#3b0000", "#ff6b6b", "#ff6b6b");
            var onceBtn   = MakeBtn("仅同意一次",       "#0f3460", "#7ec8ff", "#7ec8ff");
            var alwaysBtn = MakeBtn("本次会话始终同意", "#0d3b1a", "#6bffb8", "#6bffb8");

            denyBtn.Click   += (_, _) => { result = AgentConfirmationResult.Deny;        win.Close(); };
            onceBtn.Click   += (_, _) => { result = AgentConfirmationResult.AllowOnce;   win.Close(); };
            alwaysBtn.Click += (_, _) => { result = AgentConfirmationResult.AllowAlways; win.Close(); };

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0),
            };
            btnRow.Children.Add(denyBtn);
            btnRow.Children.Add(onceBtn);
            btnRow.Children.Add(alwaysBtn);
            bodyStack.Children.Add(btnRow);

            var root = new StackPanel
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a1a2e")),
            };
            root.Children.Add(header);
            root.Children.Add(bodyStack);
            win.Content = root;

            var owner = System.Windows.Application.Current?.MainWindow;
            if (owner is not null) win.Owner = owner;
            win.ShowDialog();
            return result;
        });
        args.Result = result;
    }

    private void OnAgentLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (AgentLogScroller is not { Items.Count: > 0 } lb) return;
            lb.ScrollIntoView(lb.Items[lb.Items.Count - 1]);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnExecutionLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScrollToEnd(ExecutionLogScroller);
    }

    private static void ScrollToEnd(ScrollViewer? scroller)
    {
        if (scroller is null) return;
        scroller.ScrollToEnd();
    }

    // VeloxDev customization: Delete 归宿主 —— 库只把按键路由过来（target 就是指针停着的那条线），
    // 删不删由这里写（与悬停高亮同一条路）。
    private void HookLinkKeys(ITreeComponent tree)
    {
        if (tree.GetHelper() is not Wf.IInputEvents events) return;

        events.Input.KeyDown += (_, e) =>
        {
            if (e.Key != Wf.InputKey.Delete || e.Handle.PreventDefault) return;
            if (e.Target is not ILinkComponent link || !link.DeleteCommand.CanExecute(null)) return;

            link.DeleteCommand.Execute(null);
        };
    }
}
