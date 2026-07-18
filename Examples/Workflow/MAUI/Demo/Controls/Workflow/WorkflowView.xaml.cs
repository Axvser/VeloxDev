using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Workflow;
using System.Collections.Specialized;
using System.IO;
using System.Windows.Input;
using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Controls;

public partial class WorkflowView : ContentView
{
    private TreeViewModel _workflowViewModel = new();
    private bool _layoutRefreshPending;
    private Page MainPage => Application.Current?.Windows[0].Page ?? throw new InvalidOperationException();

    public WorkflowView()
    {
        InitializeComponent();
    }

    public void LoadPerformanceTest()
    {
        // Set session from PerformanceTestSession tree
        var perf = PerformanceTestSession.Create().Tree;
        var session = WorkflowDemoSession.FromTree(perf);
        Session = session;
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
                await MainPage.DisplayAlert("加载失败", "文件格式不正确或解析失败。", "确定");
                return;
            }

            tree.Layout.UpdateCommand.Execute(null);

            var session = WorkflowDemoSession.FromTree(tree);
            Session = session;
        }
        catch (Exception ex)
        {
            await MainPage.DisplayAlert("错误", $"加载文件失败：{ex.Message}", "确定");
        }
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        try
        {
            var filePath = Path.Combine(FileSystem.AppDataDirectory, "Workflow.json");
            if (_workflowViewModel.SaveCommand is ICommand cmd)
                cmd.Execute(filePath);
            await MainPage.DisplayAlert("保存成功", $"工作流已保存到：{filePath}", "确定");
        }
        catch (Exception ex)
        {
            await MainPage.DisplayAlert("错误", $"保存文件失败：{ex.Message}", "确定");
        }
    }

    private void OnLoadNetworkDemoClicked(object? sender, EventArgs e)
    {
        LoadNetworkDemo();
    }

    private void OnLoadPerformanceTestClicked(object? sender, EventArgs e)
    {
        LoadPerformanceTest();
    }

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
            UnsubscribeAutoScroll(oldSession.Tree);

        // Capture ViewportOffset BEFORE setting BindingContext, because
        // BindingContext change triggers OnBindingContextChanged →
        // Refresh → UpdateVisibleRegion, which overwrites ViewportOffset
        // to the current (pre-restore, 0,0) scroll position.
        var savedVpX = 0d;
        var savedVpY = 0d;
        if (newSession is not null)
        {
            savedVpX = newSession.Tree.Layout.ViewportOffset.Horizontal;
            savedVpY = newSession.Tree.Layout.ViewportOffset.Vertical;
        }

        _workflowViewModel = newSession?.Tree ?? new TreeViewModel();
        // MAUI propagates BindingContext through the visual tree automatically,
        // so setting it on the ContentView root is sufficient. Do NOT set
        // BindingContext on individual child elements — that breaks the natural
        // inheritance chain and can cause missed binding updates.
        BindingContext = _workflowViewModel;

        if (newSession is not null)
        {
            SubscribeAutoScroll(newSession.Tree);
            newSession.Tree.Layout.UpdateCommand.Execute(null);
        }

        // Delay refresh to after layout settles, then restore the saved viewport
        // position (or center the content if no saved position exists).
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (newSession is not null)
            {
                if (savedVpX > 0 || savedVpY > 0)
                {
                    // RequestViewportRestore internally calls Refresh then queues
                    // a deferred scroll via ApplyPendingScrollRestore. The deferred
                    // scroll uses await Task.Yield() which gives MAUI time to finish
                    // the layout pass before scrolling.  This is more reliable than
                    // calling ScrollToAsync directly while layout is still pending.
                    WorkflowBehaviors.WorkflowSurfaceBehavior.RequestViewportRestore(this, savedVpX, savedVpY);
                }
                else
                {
                    // First load — center the content.
                    WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
                    var layout = newSession.Tree.Layout;
                    var centerX = layout.ActualSize.Width / 2.0;
                    var centerY = layout.ActualSize.Height / 2.0;
                    var vpW = PART_ScrollViewer.Width > 0 ? PART_ScrollViewer.Width : 100;
                    var vpH = PART_ScrollViewer.Height > 0 ? PART_ScrollViewer.Height : 100;
                    PART_ScrollViewer.ScrollToAsync(
                        Math.Max(0, centerX - vpW / 2.0),
                        Math.Max(0, centerY - vpH / 2.0), false);
                }
            }
        });
    }

    private void SubscribeAutoScroll(TreeViewModel vm)
    {
        vm.AgentLog.CollectionChanged += OnAgentLogChanged;
        vm.ExecutionLog.CollectionChanged += OnExecutionLogChanged;
        if (vm.GetHelper() is AgentHelper helper)
        {
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

    private Task ShowSelectionDialogAsync(AgentSelectionEventArgs args)
        => MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var isMulti = args.AllowMultiSelect;

            var page = new ContentPage
            {
                BackgroundColor = Color.FromArgb("#1a1a2e"),
                Title = isMulti ? "☑️  Agent · 请多选" : "🤖  Agent · 请选择",
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
                Placeholder = args.FreeTextPrompt,
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
                    Text = "✓  确认选择",
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
                "⚠️ Agent · 操作确认", $"[操作] {args.OperationKey}\n\n{args.Description}", "允许", "拒绝");

            if (!allow) { args.Result = AgentConfirmationResult.Deny; return; }

            args.Result = await MainPage.DisplayAlertAsync(
                "⚠️ Agent · 授权范围", "是否在本次会话中始终允许该操作？", "始终允许", "仅同意一次")
                ? AgentConfirmationResult.AllowAlways : AgentConfirmationResult.AllowOnce;
        });

    private void OnAgentToolCalled() => ScheduleRefresh();
    private void OnVisualRefreshRequested() => ScheduleRefresh();

    private void OnExecutionLogChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleRefresh();

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

    // ── Agent Chat ──────────────────────────────────────────────────────────

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
            _workflowViewModel.AppendAgentLog($"❌ 发送失败：{ex.Message}");
        }
    }

    private void OnAgentInputCompleted(object? sender, EventArgs e)
    {
        OnSendToAgent(sender, e);
    }
}
