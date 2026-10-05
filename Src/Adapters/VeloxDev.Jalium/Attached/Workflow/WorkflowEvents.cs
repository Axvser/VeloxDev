using System;
using System.Runtime.CompilerServices;
using Jalium.UI;
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
/// <see cref="IWorkflowTreeEventSink"/> — one object with a method per event, rather than one attached property per
/// event. The forwarding itself is <see cref="WorkflowEventRelay"/>, so all seven platforms share it and none of
/// them can drift.
/// </para>
/// <para>
/// Attach it to the view whose <see cref="FrameworkElement.DataContext"/> is the component: the subscription
/// follows the data context, which is what makes a pooled view safe — when the pool hands the view to another node,
/// the old subscription is dropped and a new one is made.
/// </para>
/// <para>
/// For a host that would rather write code than markup, the same relay can be called directly:
/// <c>WorkflowEventRelay.Attach(node, sink)</c>.
/// </para>
/// </remarks>
/// <seealso cref="WorkflowEventRelay"/>
public static class WorkflowEvents
{
    /// <summary>The <see cref="IWorkflowNodeEventSink"/> for the node this element is showing.</summary>
    public static readonly DependencyProperty NodeProperty = DependencyProperty.RegisterAttached(
        "Node", typeof(IWorkflowNodeEventSink), typeof(WorkflowEvents), new PropertyMetadata(null, OnSinkChanged));

    /// <summary>The <see cref="IWorkflowSlotEventSink"/> for the slot this element is showing.</summary>
    public static readonly DependencyProperty SlotProperty = DependencyProperty.RegisterAttached(
        "Slot", typeof(IWorkflowSlotEventSink), typeof(WorkflowEvents), new PropertyMetadata(null, OnSinkChanged));

    /// <summary>The <see cref="IWorkflowTreeEventSink"/> for the tree this element is showing.</summary>
    public static readonly DependencyProperty TreeProperty = DependencyProperty.RegisterAttached(
        "Tree", typeof(IWorkflowTreeEventSink), typeof(WorkflowEvents), new PropertyMetadata(null, OnSinkChanged));

    /// <summary>Gets the <see cref="IWorkflowNodeEventSink"/> attached to <paramref name="element"/>.</summary>
    /// <param name="element">The element the sink is bound on.</param>
    public static IWorkflowNodeEventSink? GetNode(DependencyObject element) => (IWorkflowNodeEventSink?)element.GetValue(NodeProperty);

    /// <summary>Sets the <see cref="IWorkflowNodeEventSink"/> for <paramref name="element"/>.</summary>
    /// <param name="element">The element the sink is bound on.</param>
    /// <param name="value">The host's handler, or <see langword="null"/> to detach.</param>
    public static void SetNode(DependencyObject element, IWorkflowNodeEventSink? value) => element.SetValue(NodeProperty, value);

    /// <summary>Gets the <see cref="IWorkflowSlotEventSink"/> attached to <paramref name="element"/>.</summary>
    /// <param name="element">The element the sink is bound on.</param>
    public static IWorkflowSlotEventSink? GetSlot(DependencyObject element) => (IWorkflowSlotEventSink?)element.GetValue(SlotProperty);

    /// <summary>Sets the <see cref="IWorkflowSlotEventSink"/> for <paramref name="element"/>.</summary>
    /// <param name="element">The element the sink is bound on.</param>
    /// <param name="value">The host's handler, or <see langword="null"/> to detach.</param>
    public static void SetSlot(DependencyObject element, IWorkflowSlotEventSink? value) => element.SetValue(SlotProperty, value);

    /// <summary>Gets the <see cref="IWorkflowTreeEventSink"/> attached to <paramref name="element"/>.</summary>
    /// <param name="element">The element the sink is bound on.</param>
    public static IWorkflowTreeEventSink? GetTree(DependencyObject element) => (IWorkflowTreeEventSink?)element.GetValue(TreeProperty);

    /// <summary>Sets the <see cref="IWorkflowTreeEventSink"/> for <paramref name="element"/>.</summary>
    /// <param name="element">The element the sink is bound on.</param>
    /// <param name="value">The host's handler, or <see langword="null"/> to detach.</param>
    public static void SetTree(DependencyObject element, IWorkflowTreeEventSink? value) => element.SetValue(TreeProperty, value);

    // 每个元素一份订阅：值变了、或它换了个 DataContext（池化视图就是这样），都要重新接。
    private static readonly ConditionalWeakTable<FrameworkElement, Attachment> Attachments = new();

    private static void OnSinkChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        if (!Attachments.TryGetValue(element, out var attachment))
        {
            attachment = new Attachment(element);
            Attachments.Add(element, attachment);
        }

        attachment.Connect();
    }

    // 一个元素上的三条线（node / slot / tree）共用一个 Attachment：Dispose 旧的、按当前 DataContext 与三个值重接。
    private sealed class Attachment
    {
        private readonly FrameworkElement _element;
        private IDisposable? _node;
        private IDisposable? _slot;
        private IDisposable? _tree;

        public Attachment(FrameworkElement element)
        {
            _element = element;
            element.DataContextChanged += OnDataContextChanged;
            element.Unloaded += OnUnloaded;
        }

        public void Connect()
        {
            Disconnect();

            var context = _element.DataContext;
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

        private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e) => Connect();

        private void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            // 视图离树：订阅必须解掉，否则宿主与节点互相持有。
            _element.DataContextChanged -= OnDataContextChanged;
            _element.Unloaded -= OnUnloaded;
            Disconnect();
        }
    }
}
