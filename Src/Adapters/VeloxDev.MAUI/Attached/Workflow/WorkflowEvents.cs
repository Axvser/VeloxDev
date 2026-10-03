using System.Runtime.CompilerServices;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Attached properties that hand a component's model events to a host object, by binding:
/// <code>
/// &lt;local:NodeView behaviors:WorkflowEvents.Node="{Binding NodeEvents}" /&gt;
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// The value is an <see cref="IWorkflowNodeEventSink"/> / <see cref="IWorkflowSlotEventSink"/> /
/// <see cref="IWorkflowTreeEventSink"/> — one object with a method per event, rather than one attached property
/// per event. The forwarding itself is <see cref="WorkflowEventRelay"/>, so all seven platforms share it and none
/// of them can drift.
/// </para>
/// <para>
/// Attach it to the view whose <see cref="BindableObject.BindingContext"/> is the component: the subscription
/// follows the binding context, which is what makes a pooled view safe — when the pool hands the view to another
/// node, the old subscription is dropped and a new one is made.
/// </para>
/// <para>
/// For the panels whose views are not derived from this package's base classes, or where a host would rather write
/// code than XAML, the same relay can be called directly:
/// <c>WorkflowEventRelay.Attach(node, sink)</c>.
/// </para>
/// </remarks>
/// <seealso cref="WorkflowEventRelay"/>
public static class WorkflowEvents
{
    /// <summary>The <see cref="IWorkflowNodeEventSink"/> for the node this element is showing.</summary>
    public static readonly BindableProperty NodeProperty = BindableProperty.CreateAttached(
        "Node", typeof(IWorkflowNodeEventSink), typeof(WorkflowEvents), null, propertyChanged: OnSinkChanged);

    /// <summary>The <see cref="IWorkflowSlotEventSink"/> for the slot this element is showing.</summary>
    public static readonly BindableProperty SlotProperty = BindableProperty.CreateAttached(
        "Slot", typeof(IWorkflowSlotEventSink), typeof(WorkflowEvents), null, propertyChanged: OnSinkChanged);

    /// <summary>The <see cref="IWorkflowTreeEventSink"/> for the tree this element is showing.</summary>
    public static readonly BindableProperty TreeProperty = BindableProperty.CreateAttached(
        "Tree", typeof(IWorkflowTreeEventSink), typeof(WorkflowEvents), null, propertyChanged: OnSinkChanged);

    /// <summary>Sets the <see cref="IWorkflowNodeEventSink"/> that handles <paramref name="element"/>'s node events.</summary>
    /// <param name="element">The view that shows the node.</param>
    /// <param name="value">The host's handler, or <see langword="null"/> to detach.</param>
    public static void SetNode(BindableObject element, IWorkflowNodeEventSink? value) => element.SetValue(NodeProperty, value);

    /// <summary>Gets the <see cref="IWorkflowNodeEventSink"/> that handles <paramref name="element"/>'s node events.</summary>
    /// <param name="element">The view that shows the node.</param>
    /// <returns>The host's handler, or <see langword="null"/> when none is attached.</returns>
    public static IWorkflowNodeEventSink? GetNode(BindableObject element) => (IWorkflowNodeEventSink?)element.GetValue(NodeProperty);

    /// <summary>Sets the <see cref="IWorkflowSlotEventSink"/> that handles <paramref name="element"/>'s slot events.</summary>
    /// <param name="element">The view that shows the slot.</param>
    /// <param name="value">The host's handler, or <see langword="null"/> to detach.</param>
    public static void SetSlot(BindableObject element, IWorkflowSlotEventSink? value) => element.SetValue(SlotProperty, value);

    /// <summary>Gets the <see cref="IWorkflowSlotEventSink"/> that handles <paramref name="element"/>'s slot events.</summary>
    /// <param name="element">The view that shows the slot.</param>
    /// <returns>The host's handler, or <see langword="null"/> when none is attached.</returns>
    public static IWorkflowSlotEventSink? GetSlot(BindableObject element) => (IWorkflowSlotEventSink?)element.GetValue(SlotProperty);

    /// <summary>Sets the <see cref="IWorkflowTreeEventSink"/> that handles <paramref name="element"/>'s tree events.</summary>
    /// <param name="element">The view that shows the tree.</param>
    /// <param name="value">The host's handler, or <see langword="null"/> to detach.</param>
    public static void SetTree(BindableObject element, IWorkflowTreeEventSink? value) => element.SetValue(TreeProperty, value);

    /// <summary>Gets the <see cref="IWorkflowTreeEventSink"/> that handles <paramref name="element"/>'s tree events.</summary>
    /// <param name="element">The view that shows the tree.</param>
    /// <returns>The host's handler, or <see langword="null"/> when none is attached.</returns>
    public static IWorkflowTreeEventSink? GetTree(BindableObject element) => (IWorkflowTreeEventSink?)element.GetValue(TreeProperty);

    // 每个元素一份订阅：值变了、或它换了个 BindingContext（池化视图就是这样），都要重新接。
    private static readonly ConditionalWeakTable<VisualElement, Attachment> Attachments = new();

    private static void OnSinkChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not VisualElement element) return;

        Attachments.GetValue(element, static e => new Attachment(e)).Connect();
    }

    // 一个元素上的三条线（node / slot / tree）共用一个 Attachment：Dispose 旧的、按当前 BindingContext 与三个值重接。
    private sealed class Attachment
    {
        private readonly VisualElement _element;
        private IDisposable? _node;
        private IDisposable? _slot;
        private IDisposable? _tree;

        public Attachment(VisualElement element)
        {
            _element = element;
            element.BindingContextChanged += OnBindingContextChanged;
            element.Loaded += OnLoaded;
            element.Unloaded += OnUnloaded;
        }

        public void Connect()
        {
            Disconnect();

            var context = _element.BindingContext;
            if (GetNode(_element) is { } nodeSink && context is IWorkflowNodeViewModel node)
            {
                _node = WorkflowEventRelay.Attach(node, nodeSink);
            }

            if (GetSlot(_element) is { } slotSink && context is IWorkflowSlotViewModel slot)
            {
                _slot = WorkflowEventRelay.Attach(slot, slotSink);
            }

            if (GetTree(_element) is { } treeSink && context is IWorkflowTreeViewModel tree)
            {
                _tree = WorkflowEventRelay.Attach(tree, treeSink);
            }
        }

        private void Disconnect()
        {
            _node?.Dispose();
            _slot?.Dispose();
            _tree?.Dispose();
            _node = null;
            _slot = null;
            _tree = null;
        }

        private void OnBindingContextChanged(object? sender, EventArgs e) => Connect();

        // 重进树（池化视图留在 Children 里，通常不会触发）时按当下 BindingContext 重接。
        private void OnLoaded(object? sender, EventArgs e) => Connect();

        private void OnUnloaded(object? sender, EventArgs e)
        {
            // 视图离树：订阅必须解掉，否则宿主与节点互相持有
            Disconnect();
        }
    }
}
