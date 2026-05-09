using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Workflow;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

public partial class WorkflowView : ContentView, IWorkflowSurfaceHost
{
    private TreeViewModel _workflowViewModel = new();
    private DataTemplateSelector? _nodeSelector;
    private readonly ObservableCollection<IWorkflowViewModel> _canvasItems = [];

    internal TreeViewModel WorkflowTree => _workflowViewModel;

    public WorkflowView()
    {
        InitializeComponent();
        _nodeSelector = Resources.TryGetValue("NodeSelector", out var selector) ? selector as DataTemplateSelector : null;
        Log($"ctor: selector={_nodeSelector?.GetType().Name ?? "null"}, canvas={PART_Canvas is null}, resources={Resources.Count}");
        if (_nodeSelector is not null)
        {
            ViewPool.SetTemplateSelector(PART_Canvas, _nodeSelector);
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

        ViewPool.SetItemsSource(PART_Canvas, null);

        _workflowViewModel = newSession?.Tree ?? new TreeViewModel();
        PART_SurfaceBorder.BindingContext = _workflowViewModel;
        PART_GridDecorator.BindingContext = _workflowViewModel;
        PART_ScrollViewer.BindingContext = _workflowViewModel;
        PART_Canvas.BindingContext = _workflowViewModel;
        if (_nodeSelector is not null)
        {
            ViewPool.SetTemplateSelector(PART_Canvas, _nodeSelector);
        }
        RebuildCanvasItems(_workflowViewModel);
        ViewPool.SetItemsSource(PART_Canvas, _canvasItems);
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
            WorkflowSurfaceBehavior.Refresh(this);
            Log($"AttachSession.refresh(after): canvasChildren={PART_Canvas.Children.Count}, canvasSize={PART_Canvas.Width}x{PART_Canvas.Height}, request={PART_Canvas.WidthRequest}x{PART_Canvas.HeightRequest}");
        });
    }

    private void SubscribeAutoScroll(TreeViewModel vm)
    {
        vm.AgentLog.CollectionChanged += OnAgentLogChanged;
        vm.ExecutionLog.CollectionChanged += OnExecutionLogChanged;
        if (vm.GetHelper() is AgentHelper helper)
        {
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
            helper.ToolCalled -= OnAgentToolCalled;
            helper.VisualRefreshRequested -= OnVisualRefreshRequested;
        }
    }

    private void OnAgentToolCalled() => MainThread.BeginInvokeOnMainThread(() => WorkflowSurfaceBehavior.Refresh(this));

    private void OnVisualRefreshRequested() => MainThread.BeginInvokeOnMainThread(RefreshNodeLayouts);

    private void OnAgentLogChanged(object? sender, NotifyCollectionChangedEventArgs e) => MainThread.BeginInvokeOnMainThread(() => WorkflowSurfaceBehavior.Refresh(this));

    private void OnExecutionLogChanged(object? sender, NotifyCollectionChangedEventArgs e) => MainThread.BeginInvokeOnMainThread(() => WorkflowSurfaceBehavior.Refresh(this));

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
        => MainThread.BeginInvokeOnMainThread(() => SyncCanvasItems(e));

    private void OnCanvasLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => MainThread.BeginInvokeOnMainThread(() => SyncCanvasItems(e));

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

    private void SyncCanvasItems(NotifyCollectionChangedEventArgs e)
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
    }

    private void RefreshNodeLayouts()
    {
        foreach (var child in PART_Canvas.Children.OfType<ContentView>())
        {
            WorkflowSlotLayoutBehavior.Refresh(child);
        }

        WorkflowSurfaceBehavior.Refresh(this);
    }

    private static void Log(string message)
        => Debug.WriteLine($"[WorkflowView] {message}");

    public void BeginConnection(IWorkflowSlotViewModel slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        slot.SendConnectionCommand.Execute(null);
    }

    public void UpdateConnectionPointer(Anchor anchor)
    {
        _workflowViewModel.SetPointerCommand.Execute(anchor);
    }

    public void CompleteConnection(Anchor anchor, IWorkflowSlotViewModel sourceSlot)
    {
        ArgumentNullException.ThrowIfNull(sourceSlot);

        var receiver = HitTestSlot(anchor, sourceSlot);
        if (receiver is not null)
        {
            receiver.ReceiveConnectionCommand.Execute(null);
            return;
        }

        _workflowViewModel.ResetVirtualLinkCommand.Execute(null);
    }

    private IWorkflowSlotViewModel? HitTestSlot(Anchor anchor, IWorkflowSlotViewModel exclude)
    {
        const double radius = 18d;
        var radiusSquared = radius * radius;

        foreach (var slot in EnumerateSlots())
        {
            if (ReferenceEquals(slot, exclude))
            {
                continue;
            }

            var dx = slot.Anchor.Horizontal - anchor.Horizontal;
            var dy = slot.Anchor.Vertical - anchor.Vertical;
            if ((dx * dx) + (dy * dy) <= radiusSquared)
            {
                return slot;
            }
        }

        return null;
    }

    private IEnumerable<IWorkflowSlotViewModel> EnumerateSlots()
    {
        foreach (var node in _workflowViewModel.Nodes)
        {
            if (node is BoolSelectorNodeViewModel boolSelector)
            {
                if (boolSelector.InputSlot is not null) yield return boolSelector.InputSlot;
                if (boolSelector.TrueSlot is not null) yield return boolSelector.TrueSlot;
                if (boolSelector.FalseSlot is not null) yield return boolSelector.FalseSlot;
                continue;
            }

            if (node is EnumSelectorNodeViewModel enumSelector)
            {
                if (enumSelector.InputSlot is not null) yield return enumSelector.InputSlot;
                foreach (var slot in enumSelector.OutputSlots) yield return slot;
                continue;
            }

            if (node is NodeViewModel workflowNode)
            {
                if (workflowNode.InputSlot is not null) yield return workflowNode.InputSlot;
                if (workflowNode.OutputSlot is not null) yield return workflowNode.OutputSlot;
                continue;
            }

            if (node is ControllerViewModel controller && controller.OutputSlot is not null)
            {
                yield return controller.OutputSlot;
            }
        }
    }
}
