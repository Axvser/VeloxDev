using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Workflow;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using VeloxDev.AI;
using VeloxDev.WorkflowSystem;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Controls;

public partial class WorkflowView : ContentView
{
    private TreeViewModel _workflowViewModel = new();
    private DataTemplateSelector? _nodeSelector;
    private readonly ObservableCollection<IWorkflowViewModel> _canvasItems = [];

    public WorkflowView()
    {
        InitializeComponent();
        _nodeSelector = Resources.TryGetValue("NodeSelector", out var selector) ? selector as DataTemplateSelector : null;
        Log($"ctor: selector={_nodeSelector?.GetType().Name ?? "null"}, canvas={PART_Canvas is null}, resources={Resources.Count}");
        if (_nodeSelector is not null)
        {
            WorkflowBehaviors.ViewPool.SetTemplateSelector(PART_Canvas, _nodeSelector);
        }
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
        Log($"AttachSession: old={(oldSession is null ? "null" : oldSession.Tree.Nodes.Count.ToString())}, new={(newSession is null ? "null" : newSession.Tree.Nodes.Count.ToString())}");
        if (oldSession is not null)
        {
            UnsubscribeAutoScroll(oldSession.Tree);
            UnsubscribeCanvasItems(oldSession.Tree);
        }

        WorkflowBehaviors.ViewPool.SetItemsSource(PART_Canvas, null);

        _workflowViewModel = newSession?.Tree ?? new TreeViewModel();
        PART_SurfaceBorder.BindingContext = _workflowViewModel;
        PART_GridDecorator.BindingContext = _workflowViewModel;
        PART_ScrollViewer.BindingContext = _workflowViewModel;
        PART_Canvas.BindingContext = _workflowViewModel;
        if (_nodeSelector is not null)
        {
            WorkflowBehaviors.ViewPool.SetTemplateSelector(PART_Canvas, _nodeSelector);
        }
        RebuildCanvasItems(_workflowViewModel);
        WorkflowBehaviors.ViewPool.SetItemsSource(PART_Canvas, _canvasItems);
        Log($"AttachSession.afterBind: nodes={_workflowViewModel.Nodes.Count}, links={_workflowViewModel.Links.Count}, canvasItems={_canvasItems.Count}, canvasChildren={PART_Canvas.Children.Count}");

        if (newSession is not null)
        {
            SubscribeAutoScroll(newSession.Tree);
            SubscribeCanvasItems(newSession.Tree);
            newSession.Tree.Layout.UpdateCommand.Execute(null);
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Log($"AttachSession.refresh(before): canvasChildren={PART_Canvas.Children.Count}, canvasSize={PART_Canvas.Width}x{PART_Canvas.Height}, request={PART_Canvas.WidthRequest}x{PART_Canvas.HeightRequest}");
            WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
            Log($"AttachSession.refresh(after): canvasChildren={PART_Canvas.Children.Count}, canvasSize={PART_Canvas.Width}x{PART_Canvas.Height}, request={PART_Canvas.WidthRequest}x{PART_Canvas.HeightRequest}");
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

    private Task ShowSelectionDialogAsync(AgentSelectionEventArgs args)
    {
        var tcs = new TaskCompletionSource();
        MainThread.BeginInvokeOnMainThread(async () =>
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
                    var cb = new CheckBox
                    {
                        Color = Color.FromArgb("#7ec8ff"),
                    };
                    var label = new Label
                    {
                        Text = opt,
                        TextColor = Color.FromArgb("#e0e0e0"),
                        FontSize = 13,
                        VerticalOptions = LayoutOptions.Center,
                    };
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
                        BorderWidth = 1,
                        CornerRadius = 6,
                        HeightRequest = 40,
                        HorizontalOptions = LayoutOptions.Fill,
                        Margin = new Thickness(0, 0, 0, 4),
                    };
                    btn.Clicked += (_, _) =>
                    {
                        args.SelectedOption = captured;
                        args.FreeTextResponse = freeTextEntry.Text?.Trim();
                        args.FreeTextResponse = string.IsNullOrWhiteSpace(args.FreeTextResponse) ? null : args.FreeTextResponse;
                        tcs.TrySetResult();
                        Application.Current!.MainPage!.Navigation.PopModalAsync(true);
                    };
                    stack.Children.Add(btn);
                }
            }

            // ── Free text input (always shown) ──
            stack.Children.Add(new Label
            {
                Text = args.FreeTextPrompt,
                TextColor = Color.FromArgb("#b0b0b0"),
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 2),
            });
            stack.Children.Add(freeTextEntry);

            if (isMulti)
            {
                var confirmBtn = new Button
                {
                    Text = "✓  确认选择",
                    BackgroundColor = Color.FromArgb("#0f3460"),
                    TextColor = Color.FromArgb("#7ec8ff"),
                    BorderColor = Color.FromArgb("#7ec8ff"),
                    BorderWidth = 1,
                    CornerRadius = 6,
                    HeightRequest = 40,
                    Margin = new Thickness(0, 10, 0, 0),
                };
                confirmBtn.Clicked += (_, _) =>
                {
                    args.SelectedOptions = checkBoxes!
                        .Where(cb => cb.IsChecked)
                        .Select(cb => (string)((Label)((HorizontalStackLayout)cb.Parent).Children[1]).Text)
                        .ToList();
                    args.FreeTextResponse = freeTextEntry?.Text?.Trim();
                    args.FreeTextResponse = string.IsNullOrWhiteSpace(args.FreeTextResponse) ? null : args.FreeTextResponse;
                    tcs.TrySetResult();
                    Application.Current!.MainPage!.Navigation.PopModalAsync(true);
                };
                stack.Children.Add(confirmBtn);

                var cancelBtn = new Button
                {
                    Text = "取消",
                    BackgroundColor = Color.FromArgb("#2a2a3e"),
                    TextColor = Color.FromArgb("#888888"),
                    BorderColor = Color.FromArgb("#444444"),
                    BorderWidth = 1,
                    CornerRadius = 6,
                    HeightRequest = 36,
                };
                cancelBtn.Clicked += (_, _) =>
                {
                    tcs.TrySetResult();
                    Application.Current!.MainPage!.Navigation.PopModalAsync(true);
                };
                stack.Children.Add(cancelBtn);
            }
            else
            {
                var cancelBtn = new Button
                {
                    Text = "取消（不选择）",
                    BackgroundColor = Color.FromArgb("#2a2a3e"),
                    TextColor = Color.FromArgb("#888888"),
                    BorderColor = Color.FromArgb("#444444"),
                    BorderWidth = 1,
                    CornerRadius = 6,
                    HeightRequest = 36,
                    Margin = new Thickness(0, 8, 0, 0),
                };
                cancelBtn.Clicked += (_, _) =>
                {
                    args.SelectedOption = null;
                    args.FreeTextResponse = freeTextEntry?.Text?.Trim();
                    args.FreeTextResponse = string.IsNullOrWhiteSpace(args.FreeTextResponse) ? null : args.FreeTextResponse;
                    tcs.TrySetResult();
                    Application.Current!.MainPage!.Navigation.PopModalAsync(true);
                };
                stack.Children.Add(cancelBtn);
            }

            page.Content = new ScrollView { Content = stack };
            await Application.Current!.MainPage!.Navigation.PushModalAsync(page, true);
        });
        return tcs.Task;
    }

    private Task ShowConfirmationDialogAsync(AgentConfirmationEventArgs args)
    {
        var tcs = new TaskCompletionSource();
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var allow = await Application.Current!.MainPage!.DisplayAlert(
                "⚠️ Agent · 操作确认",
                $"[操作] {args.OperationKey}\n\n{args.Description}",
                "允许", "拒绝");

            if (!allow)
            {
                args.Result = AgentConfirmationResult.Deny;
                tcs.TrySetResult();
                return;
            }

            var always = await Application.Current!.MainPage!.DisplayAlert(
                "⚠️ Agent · 授权范围",
                "是否在本次会话中始终允许该操作？",
                "始终允许", "仅同意一次");

            args.Result = always ? AgentConfirmationResult.AllowAlways : AgentConfirmationResult.AllowOnce;
            tcs.TrySetResult();
        });
        return tcs.Task;
    }

    private void OnAgentToolCalled() => MainThread.BeginInvokeOnMainThread(() => WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this));

    private void OnVisualRefreshRequested() => MainThread.BeginInvokeOnMainThread(RefreshNodeLayouts);

    private void OnAgentLogChanged(object? sender, NotifyCollectionChangedEventArgs e) => MainThread.BeginInvokeOnMainThread(() => WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this));

    private void OnExecutionLogChanged(object? sender, NotifyCollectionChangedEventArgs e) => MainThread.BeginInvokeOnMainThread(() => WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this));

    private void SubscribeCanvasItems(TreeViewModel vm)
    {
        vm.Nodes.CollectionChanged += OnCanvasNodesChanged;
        vm.Links.CollectionChanged += OnCanvasLinksChanged;
    }

    private void UnsubscribeCanvasItems(TreeViewModel vm)
    {
        vm.Nodes.CollectionChanged -= OnCanvasNodesChanged;
        vm.Links.CollectionChanged -= OnCanvasLinksChanged;
    }

    private void OnCanvasNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var viewportX = PART_ScrollViewer.ScrollX - _workflowViewModel.Layout.ActualOffset.Horizontal;
        var viewportY = PART_ScrollViewer.ScrollY - _workflowViewModel.Layout.ActualOffset.Vertical;
        MainThread.BeginInvokeOnMainThread(() => SyncCanvasItems(e, viewportX, viewportY));
    }

    private void OnCanvasLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var viewportX = PART_ScrollViewer.ScrollX - _workflowViewModel.Layout.ActualOffset.Horizontal;
        var viewportY = PART_ScrollViewer.ScrollY - _workflowViewModel.Layout.ActualOffset.Vertical;
        MainThread.BeginInvokeOnMainThread(() => SyncCanvasItems(e, viewportX, viewportY));
    }

    private void RebuildCanvasItems(TreeViewModel tree)
    {
        _canvasItems.Clear();

        foreach (var link in tree.Links)
        {
            _canvasItems.Add(link);
        }

        foreach (var node in tree.Nodes)
        {
            _canvasItems.Add(node);
        }

        Log($"RebuildCanvasItems: nodes={tree.Nodes.Count}, links={tree.Links.Count}, canvasItems={_canvasItems.Count}, sample={string.Join(",", _canvasItems.Take(6).Select(x => x.GetType().Name))}");
    }

    private void SyncCanvasItems(NotifyCollectionChangedEventArgs e, double viewportX, double viewportY)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems is null)
                {
                    return;
                }

                foreach (var item in e.NewItems.OfType<IWorkflowViewModel>())
                {
                    if (!_canvasItems.Contains(item))
                    {
                        _canvasItems.Add(item);
                    }
                }
                break;
            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems is null)
                {
                    return;
                }

                foreach (var item in e.OldItems.OfType<IWorkflowViewModel>())
                {
                    _canvasItems.Remove(item);
                }
                break;
            default:
                RebuildCanvasItems(_workflowViewModel);
                break;
        }

        Log($"SyncCanvasItems: action={e.Action}, canvasItems={_canvasItems.Count}, canvasChildren={PART_Canvas.Children.Count}");
        RefreshNodeLayouts();
        WorkflowBehaviors.WorkflowSurfaceBehavior.RequestViewportRestore(this, viewportX, viewportY);
    }

    private void RefreshNodeLayouts()
    {
        foreach (var child in PART_Canvas.Children.OfType<ContentView>())
        {
            WorkflowBehaviors.WorkflowSlotLayoutBehavior.Refresh(child);
        }

        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
    }

    private static void Log(string message)
        => Debug.WriteLine($"[WorkflowView] {message}");

}
