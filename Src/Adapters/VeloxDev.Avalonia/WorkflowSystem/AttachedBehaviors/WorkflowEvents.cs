using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
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
/// Attach it to the view whose <see cref="Control.DataContext"/> is the component: the subscription follows the
/// data context, which is what makes a pooled view safe — when the pool hands the view to another node, the old
/// subscription is dropped and a new one is made.
/// </para>
/// <para>
/// For the panels whose views are not derived from this package's base classes, or where a host would rather write
/// code than XAML, the same relay can be called directly:
/// <c>WorkflowEventRelay.Attach(node, sink)</c>.
/// </para>
/// </remarks>
/// <seealso cref="WorkflowEventRelay"/>
public sealed class WorkflowEvents : AvaloniaObject
{
    /// <summary>The <see cref="IWorkflowNodeEventSink"/> for the node this element is showing.</summary>
    public static readonly AttachedProperty<IWorkflowNodeEventSink?> NodeProperty =
        AvaloniaProperty.RegisterAttached<WorkflowEvents, Control, IWorkflowNodeEventSink?>("Node");

    /// <summary>The <see cref="IWorkflowSlotEventSink"/> for the slot this element is showing.</summary>
    public static readonly AttachedProperty<IWorkflowSlotEventSink?> SlotProperty =
        AvaloniaProperty.RegisterAttached<WorkflowEvents, Control, IWorkflowSlotEventSink?>("Slot");

    /// <summary>The <see cref="IWorkflowTreeEventSink"/> for the tree this element is showing.</summary>
    public static readonly AttachedProperty<IWorkflowTreeEventSink?> TreeProperty =
        AvaloniaProperty.RegisterAttached<WorkflowEvents, Control, IWorkflowTreeEventSink?>("Tree");

    static WorkflowEvents()
    {
        // 三条线共用同一个变更回调：拿到值后一律交给 Attachment 重新接。
        NodeProperty.Changed.AddClassHandler<Control>(OnSinkChanged);
        SlotProperty.Changed.AddClassHandler<Control>(OnSinkChanged);
        TreeProperty.Changed.AddClassHandler<Control>(OnSinkChanged);
    }

    /// <summary>Sets the <see cref="IWorkflowNodeEventSink"/> for <paramref name="element"/>.</summary>
    public static void SetNode(AvaloniaObject element, IWorkflowNodeEventSink? value) => element.SetValue(NodeProperty, value);

    /// <summary>Gets the <see cref="IWorkflowNodeEventSink"/> attached to <paramref name="element"/>.</summary>
    public static IWorkflowNodeEventSink? GetNode(AvaloniaObject element) => element.GetValue(NodeProperty);

    /// <summary>Sets the <see cref="IWorkflowSlotEventSink"/> for <paramref name="element"/>.</summary>
    public static void SetSlot(AvaloniaObject element, IWorkflowSlotEventSink? value) => element.SetValue(SlotProperty, value);

    /// <summary>Gets the <see cref="IWorkflowSlotEventSink"/> attached to <paramref name="element"/>.</summary>
    public static IWorkflowSlotEventSink? GetSlot(AvaloniaObject element) => element.GetValue(SlotProperty);

    /// <summary>Sets the <see cref="IWorkflowTreeEventSink"/> for <paramref name="element"/>.</summary>
    public static void SetTree(AvaloniaObject element, IWorkflowTreeEventSink? value) => element.SetValue(TreeProperty, value);

    /// <summary>Gets the <see cref="IWorkflowTreeEventSink"/> attached to <paramref name="element"/>.</summary>
    public static IWorkflowTreeEventSink? GetTree(AvaloniaObject element) => element.GetValue(TreeProperty);

    // 每个元素一份订阅：值变了、或它换了个 DataContext（池化视图就是这样），都要重新接。
    private static readonly ConditionalWeakTable<Control, Attachment> Attachments = new();

    private static void OnSinkChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (!Attachments.TryGetValue(control, out var attachment))
        {
            attachment = new Attachment(control);
            Attachments.Add(control, attachment);
        }

        attachment.Connect();
    }

    // 一个元素上的三条线（node / slot / tree）共用一个 Attachment：Dispose 旧的、按当前 DataContext 与三个值重接。
    private sealed class Attachment
    {
        private readonly Control _element;
        private IDisposable? _node;
        private IDisposable? _slot;
        private IDisposable? _tree;

        public Attachment(Control element)
        {
            _element = element;
            element.DataContextChanged += OnDataContextChanged;
            element.DetachedFromVisualTree += OnDetachedFromVisualTree;
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

        private void OnDataContextChanged(object? sender, EventArgs e) => Connect();

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            // 视图离树：订阅必须解掉，否则宿主与节点互相持有
            _element.DataContextChanged -= OnDataContextChanged;
            _element.DetachedFromVisualTree -= OnDetachedFromVisualTree;
            Disconnect();
        }
    }
}
