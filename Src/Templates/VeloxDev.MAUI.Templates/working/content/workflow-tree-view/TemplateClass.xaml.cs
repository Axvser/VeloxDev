// VeloxDev customization: Set BindingContext to your IWorkflowTreeViewModel before the control is loaded.
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using VeloxDev.WorkflowSystem;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

public partial class TemplateClass : ContentView, WorkflowBehaviors.IWorkflowSurfaceHost
{
    private bool _nodeLayoutRefreshPending;

    public TemplateClass()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        PART_Canvas.ChildAdded += OnCanvasChildAdded;
        PART_Canvas.ChildRemoved += OnCanvasChildRemoved;
    }

    private IWorkflowTreeViewModel WorkflowTree
        => BindingContext as IWorkflowTreeViewModel
            ?? throw new InvalidOperationException(
                "Set BindingContext to an IWorkflowTreeViewModel before loading the workflow tree view.");

    IWorkflowTreeViewModel? WorkflowBehaviors.IWorkflowSurfaceHost.WorkflowTree => WorkflowTree;

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (BindingContext is not IWorkflowTreeViewModel tree)
        {
            return;
        }

        var selector = (DataTemplateSelector)Resources["WorkflowTemplateSelector"];
        WorkflowBehaviors.ViewPool.SetTemplateSelector(PART_Canvas, selector);
        WorkflowBehaviors.ViewPool.SetItemsSource(PART_Canvas, tree.GetHelper().VisibleItems);
        tree.Layout.UpdateCommand.Execute(null);
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
        ScheduleNodeLayoutRefresh();
    }

    private void OnCanvasChildAdded(object? sender, ElementEventArgs e)
    {
        if (e.Element is ContentView view)
        {
            view.Loaded += OnNodeViewLayoutChanged;
            view.SizeChanged += OnNodeViewLayoutChanged;
            view.BindingContextChanged += OnNodeViewBindingContextChanged;
        }

        ScheduleNodeLayoutRefresh();
    }

    private void OnCanvasChildRemoved(object? sender, ElementEventArgs e)
    {
        if (e.Element is ContentView view)
        {
            view.Loaded -= OnNodeViewLayoutChanged;
            view.SizeChanged -= OnNodeViewLayoutChanged;
            view.BindingContextChanged -= OnNodeViewBindingContextChanged;
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
        => WorkflowTree.SetPointerCommand.Execute(anchor);

    public void CompleteConnection(Anchor anchor, IWorkflowSlotViewModel sourceSlot)
    {
        // Adjust the radius when your slot visual is substantially larger or smaller.
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
            WorkflowTree.ResetVirtualLinkCommand.Execute(null);
        }
    }

    private IEnumerable<WorkflowSlotView> EnumerateVisibleSlotViews()
    {
        foreach (var child in PART_Canvas.Children.OfType<Element>())
        {
            foreach (var slotView in EnumerateSlotViews(child))
            {
                yield return slotView;
            }
        }
    }

    private static IEnumerable<WorkflowSlotView> EnumerateSlotViews(Element element)
    {
        if (element is WorkflowSlotView slotView)
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
