using System.Runtime.CompilerServices;
using Easel;
using Easel.Visuals;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Attached properties that hand a component's model events to a host object, by binding:
/// <code>
/// &lt;local:NodeView behaviors:WorkflowEvents.Node="{Binding NodeEvents}" /&gt;
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// The value is an <see cref="INodeEventSink"/> / <see cref="ISlotEventSink"/> /
/// <see cref="ITreeEventSink"/> — one object with a method per event, rather than one attached property per
/// event. The forwarding itself is <see cref="WorkflowEventRelay"/>, so all seven platforms share it and none of
/// them can drift.
/// </para>
/// <para>
/// Attach it to the view whose <see cref="VisualElement.DataContext"/> is the component: the subscription
/// follows the data context, which is what makes a pooled view safe — when the pool hands the view to another node,
/// the old subscription is dropped and a new one is made.
/// </para>
/// <para>
/// For hosts that would rather write code than XAML, the same relay can be called directly:
/// <c>WorkflowEventRelay.Attach(node, sink)</c>.
/// </para>
/// </remarks>
/// <seealso cref="WorkflowEventRelay"/>
public sealed class WorkflowEvents
{
    /// <summary>The <see cref="INodeEventSink"/> for the node this element is showing.</summary>
    public static readonly DependencyProperty NodeProperty = DependencyProperty.RegisterAttached(
        "Node", typeof(INodeEventSink), typeof(WorkflowEvents),
        // 视觉元素不是 Control：只有 VisualPropertyChangedCallback 会在这里触发，Ensure 因此能按属性变化接线。
        new PropertyMetadata(null) { VisualPropertyChangedCallback = OnVisualPropertyChanged });

    /// <summary>The <see cref="ISlotEventSink"/> for the slot this element is showing.</summary>
    public static readonly DependencyProperty SlotProperty = DependencyProperty.RegisterAttached(
        "Slot", typeof(ISlotEventSink), typeof(WorkflowEvents),
        new PropertyMetadata(null) { VisualPropertyChangedCallback = OnVisualPropertyChanged });

    /// <summary>The <see cref="ITreeEventSink"/> for the tree this element is showing.</summary>
    public static readonly DependencyProperty TreeProperty = DependencyProperty.RegisterAttached(
        "Tree", typeof(ITreeEventSink), typeof(WorkflowEvents),
        new PropertyMetadata(null) { VisualPropertyChangedCallback = OnVisualPropertyChanged });

    // 每个元素一份订阅：值变了、或它换了个 DataContext（池化视图就是这样），都要重新接。
    private static readonly ConditionalWeakTable<VisualElement, Attachment> Attachments = new();

    private WorkflowEvents()
    {
    }

    // 视觉元素的附着属性写值只走这一条回调；Ensure 本身幂等，重复调用不会重复接线。
    private static void OnVisualPropertyChanged(IWinformsObject target, DependencyPropertyChangedEventArgs e)
    {
        _ = e;
        if (target is VisualElement element)
        {
            Ensure(element);
        }
    }

    /// <summary>Gets the <see cref="INodeEventSink"/> attached to <paramref name="element"/>.</summary>
    /// <param name="element">The element the sink is bound on.</param>
    public static INodeEventSink? GetNode(VisualElement element) => (INodeEventSink?)element.GetValue(NodeProperty);

    /// <summary>Sets the <see cref="INodeEventSink"/> for <paramref name="element"/>.</summary>
    /// <param name="element">The element the sink is bound on.</param>
    /// <param name="value">The host's handler, or <see langword="null"/> to detach.</param>
    public static void SetNode(VisualElement element, INodeEventSink? value) => element.SetValue(NodeProperty, value);

    /// <summary>Gets the <see cref="ISlotEventSink"/> attached to <paramref name="element"/>.</summary>
    /// <param name="element">The element the sink is bound on.</param>
    public static ISlotEventSink? GetSlot(VisualElement element) => (ISlotEventSink?)element.GetValue(SlotProperty);

    /// <summary>Sets the <see cref="ISlotEventSink"/> for <paramref name="element"/>.</summary>
    /// <param name="element">The element the sink is bound on.</param>
    /// <param name="value">The host's handler, or <see langword="null"/> to detach.</param>
    public static void SetSlot(VisualElement element, ISlotEventSink? value) => element.SetValue(SlotProperty, value);

    /// <summary>Gets the <see cref="ITreeEventSink"/> attached to <paramref name="element"/>.</summary>
    /// <param name="element">The element the sink is bound on.</param>
    public static ITreeEventSink? GetTree(VisualElement element) => (ITreeEventSink?)element.GetValue(TreeProperty);

    /// <summary>Sets the <see cref="ITreeEventSink"/> for <paramref name="element"/>.</summary>
    /// <param name="element">The element the sink is bound on.</param>
    /// <param name="value">The host's handler, or <see langword="null"/> to detach.</param>
    public static void SetTree(VisualElement element, ITreeEventSink? value) => element.SetValue(TreeProperty, value);

    /// <summary>Ensures the element has a relay attachment, then connects its current sinks to its current data context.</summary>
    /// <param name="element">The element to attach.</param>
    public static void Ensure(VisualElement element)
    {
        if (!Attachments.TryGetValue(element, out var attachment))
        {
            attachment = new Attachment(element);
            Attachments.Add(element, attachment);
        }

        attachment.Connect();
    }

    // 元素上的三条线（node / slot / tree）共用一个 Attachment：Dispose 旧的、按当前 DataContext 与三个值重接。
    private sealed class Attachment
    {
        private readonly VisualElement _element;
        private IDisposable? _node;
        private IDisposable? _slot;
        private IDisposable? _tree;
        private object? _connectedContext;
        private INodeEventSink? _connectedNodeSink;
        private ISlotEventSink? _connectedSlotSink;
        private ITreeEventSink? _connectedTreeSink;

        public Attachment(VisualElement element)
        {
            _element = element;
            element.PropertyChanged += OnPropertyChanged;
        }

        public void Connect()
        {
            var context = _element.DataContext;
            var nodeSink = GetNode(_element);
            var slotSink = GetSlot(_element);
            var treeSink = GetTree(_element);

            // 表面每次遍历都会 Ensure：四者都没变就不重接，否则每个节点每轮都退订/重订一次
            if (ReferenceEquals(context, _connectedContext)
                && ReferenceEquals(nodeSink, _connectedNodeSink)
                && ReferenceEquals(slotSink, _connectedSlotSink)
                && ReferenceEquals(treeSink, _connectedTreeSink))
            {
                return;
            }

            Disconnect();
            _connectedContext = context;
            _connectedNodeSink = nodeSink;
            _connectedSlotSink = slotSink;
            _connectedTreeSink = treeSink;

            if (nodeSink is not null && context is INodeComponent node)
            {
                _node = WorkflowEventRelay.Attach(node, nodeSink);
            }

            if (slotSink is not null && context is ISlotComponent slot)
            {
                _slot = WorkflowEventRelay.Attach(slot, slotSink);
            }

            if (treeSink is not null && context is ITreeComponent tree)
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

        private void OnPropertyChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (ReferenceEquals(e.Property, VisualElement.DataContextProperty)
                || ReferenceEquals(e.Property, NodeProperty)
                || ReferenceEquals(e.Property, SlotProperty)
                || ReferenceEquals(e.Property, TreeProperty))
            {
                Connect();
            }
        }
    }
}
