using Demo.ViewModels.Workflow;
using Demo.ViewModels.Workflow.Enums;
using VeloxDev.WorkflowSystem;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Controls;

public partial class WorkflowView : ContentView, WorkflowBehaviors.IWorkflowSurfaceHost
{
    private readonly TreeViewModel _tree = new();
    private bool _nodeLayoutRefreshPending;

    IWorkflowTreeViewModel WorkflowBehaviors.IWorkflowSurfaceHost.WorkflowTree => _tree;

    public WorkflowView()
    {
        InitializeComponent();
        BindingContext = _tree;

        var selector = (DataTemplateSelector)Resources["WorkflowTemplateSelector"];
        WorkflowBehaviors.ViewPool.SetTemplateSelector(PART_Canvas, selector);
        WorkflowBehaviors.ViewPool.SetItemsSource(PART_Canvas, _tree.Helper.VisibleItems);

        LoadTree();
        Loaded += OnLoaded;
        PART_Canvas.ChildAdded += OnCanvasChildAdded;
        PART_Canvas.ChildRemoved += OnCanvasChildRemoved;
    }

    private void LoadTree()
    {
        var size = new VeloxDev.WorkflowSystem.Size(260, 180);
        var nodes = new[]
        {
            new NodeViewModel { Name = "Boolean routes", Size = size, Anchor = new Anchor { Horizontal = 80, Vertical = 80 } },
            new NodeViewModel { Name = "Voltage routes", Size = size, Anchor = new Anchor { Horizontal = 400, Vertical = 220 } },
            new NodeViewModel { Name = "Model routes", Size = size, Anchor = new Anchor { Horizontal = 720, Vertical = 80 } }
        };

        foreach (var node in nodes)
        {
            _tree.CreateNodeCommand.Execute(node);
        }

        nodes[0].OutputSlots.SetSelector(typeof(bool));
        nodes[1].OutputSlots.SetSelector(typeof(VoltageRange));
        nodes[2].OutputSlots.SetSelector(typeof(ModelProtocol));
        nodes[0].InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        nodes[1].InputSlot.SetChannelCommand.Execute(SlotChannel.MultipleSources);
        nodes[2].InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _tree.Layout.UpdateCommand.Execute(null);
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
        ScheduleNodeLayoutRefresh();
    }

    private void OnCanvasChildAdded(object? sender, ElementEventArgs e)
    {
        if (e.Element is ContentView nodeView)
        {
            nodeView.Loaded += OnNodeViewLayoutChanged;
            nodeView.SizeChanged += OnNodeViewLayoutChanged;
            nodeView.BindingContextChanged += OnNodeViewBindingContextChanged;
        }

        ScheduleNodeLayoutRefresh();
    }

    private void OnCanvasChildRemoved(object? sender, ElementEventArgs e)
    {
        if (e.Element is ContentView nodeView)
        {
            nodeView.Loaded -= OnNodeViewLayoutChanged;
            nodeView.SizeChanged -= OnNodeViewLayoutChanged;
            nodeView.BindingContextChanged -= OnNodeViewBindingContextChanged;
        }
    }

    private void OnNodeViewLayoutChanged(object? sender, EventArgs e)
        => ScheduleNodeLayoutRefresh();

    private void OnNodeViewBindingContextChanged(object? sender, EventArgs e)
        => ScheduleNodeLayoutRefresh();

    private void ScheduleNodeLayoutRefresh()
    {
        if (_nodeLayoutRefreshPending)
        {
            return;
        }

        _nodeLayoutRefreshPending = true;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _nodeLayoutRefreshPending = false;
                RefreshNodeLayouts();
            });
        });
    }

    private void RefreshNodeLayouts()
    {
        foreach (var child in PART_Canvas.Children.OfType<ContentView>())
        {
            WorkflowBehaviors.WorkflowSlotLayoutBehavior.Refresh(child);
        }

        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
    }

    public void UpdateConnectionPointer(Anchor anchor)
        => _tree.SetPointerCommand.Execute(anchor);

    public void CompleteConnection(Anchor anchor, IWorkflowSlotViewModel sourceSlot)
    {
        const double radius = 18d;
        var receiver = EnumerateVisibleSlotViews()
            .Where(view => view.BindingContext is IWorkflowSlotViewModel slot
                && !ReferenceEquals(slot, sourceSlot)
                && view.SynchronizeAnchor())
            .Select(view => (IWorkflowSlotViewModel)view.BindingContext)
            .FirstOrDefault(slot =>
            {
                var x = slot.Anchor.Horizontal - anchor.Horizontal;
                var y = slot.Anchor.Vertical - anchor.Vertical;
                return (x * x) + (y * y) <= radius * radius;
            });

        if (receiver?.ReceiveConnectionCommand.CanExecute(null) == true)
        {
            receiver.ReceiveConnectionCommand.Execute(null);
        }
        else
        {
            _tree.ResetVirtualLinkCommand.Execute(null);
        }
    }

    private IEnumerable<SlotView> EnumerateVisibleSlotViews()
    {
        foreach (var child in PART_Canvas.Children.OfType<Element>())
        {
            foreach (var slotView in EnumerateSlotViews(child))
            {
                yield return slotView;
            }
        }
    }

    private static IEnumerable<SlotView> EnumerateSlotViews(Element element)
    {
        if (element is SlotView slotView)
        {
            yield return slotView;
            yield break;
        }

        if (element is ContentView { Content: Element content })
        {
            foreach (var descendant in EnumerateSlotViews(content))
            {
                yield return descendant;
            }

            yield break;
        }

        if (element is Border { Content: Element borderContent })
        {
            foreach (var descendant in EnumerateSlotViews(borderContent))
            {
                yield return descendant;
            }

            yield break;
        }

        if (element is Layout layout)
        {
            foreach (var child in layout.Children.OfType<Element>())
            {
                foreach (var descendant in EnumerateSlotViews(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
