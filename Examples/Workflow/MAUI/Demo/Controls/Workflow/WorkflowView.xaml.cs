using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Workflow;
using System.Collections.Specialized;
using VeloxDev.AI;
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

        _workflowViewModel = newSession?.Tree ?? new TreeViewModel();
        PART_SurfaceBorder.BindingContext = _workflowViewModel;
        PART_GridDecorator.BindingContext = _workflowViewModel;
        PART_ScrollViewer.BindingContext = _workflowViewModel;
        PART_Canvas.BindingContext = _workflowViewModel;

        if (newSession is not null)
        {
            SubscribeAutoScroll(newSession.Tree);
            newSession.Tree.Layout.UpdateCommand.Execute(null);
        }

        // Delay refresh to after layout settles
        MainThread.BeginInvokeOnMainThread(() =>
            WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this));
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

    private void OnAgentLogChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleRefresh();
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
            _layoutRefreshPending = false;
            WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
        });
    }

    }
