using System.Runtime.CompilerServices;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using VeloxDev.WorkflowSystem;
using PlatformInput = Jalium.UI.Input;
using Wf = VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Attached behavior that turns a slot view into the two ends of a connection gesture: a left-press starts one
/// (<see cref="IWorkflowSlotViewModel.SendConnectionCommand"/>) and the matching release finishes one
/// (<see cref="IWorkflowSlotViewModel.ReceiveConnectionCommand"/>).
/// </summary>
/// <remarks>
/// <para>
/// The behavior reads the slot from the element's <see cref="FrameworkElement.DataContext"/>, so a pooled slot view
/// is always wired to whichever slot it is currently showing.
/// </para>
/// <para>
/// <b>It owns the whole gesture, including its failure.</b> A press starts a connection and leaves a rubber band
/// hanging off the tree's virtual link; every release that does not land on a willing receiver must take that
/// rubber band back down — otherwise the tree still believes a connection is in flight and refuses to start the
/// next one. Releasing on the port that started it is the common case of that, and it is easy to miss because it
/// looks like a successful gesture from the outside.
/// </para>
/// <para>
/// Whether the gesture is <i>legal</i> — channels, direction, whether a link already exists — is the model's
/// business. This behavior only translates the pointer and cleans up after itself.
/// </para>
/// </remarks>
public sealed class WorkflowSlotConnectionBehavior : DependencyObject
{
    /// <summary>The attached property that turns slot-connection dragging on for a control.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(WorkflowSlotConnectionBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    // 这一次手势是从哪个口发起的。按控件存，松手时用它判断「松回自己身上」。
    private sealed class ConnectionState
    {
        public IWorkflowSlotViewModel? Sender;
    }

    private static readonly ConditionalWeakTable<Control, ConnectionState> States = new();

    /// <summary>Reads the <c>IsEnabled</c> attached property from <paramref name="element"/>.</summary>
    public static bool GetIsEnabled(DependencyObject element) => element.GetValue(IsEnabledProperty) is true;

    /// <summary>Sets the <c>IsEnabled</c> attached property on <paramref name="element"/>.</summary>
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control control)
        {
            return;
        }

        // 按下订**隧道相**：这一笔只有插槽自己路由得到（见 OnPointerPressed），而左键专用的那一个是从
        // 隧道相翻译出来的 Direct 事件、args 是另建的一份 —— 用它就没法和按下源对「是不是同一笔」。
        control.PreviewMouseDown -= OnPointerPressed;
        control.PreviewMouseLeftButtonUp -= OnPointerReleased;

        if (e.NewValue is true)
        {
            control.PreviewMouseDown += OnPointerPressed;
            control.PreviewMouseLeftButtonUp += OnPointerReleased;
        }
    }

    private static void OnPointerPressed(object? sender, MouseButtonEventArgs e)
    {
        if (sender is not Control { DataContext: IWorkflowSlotViewModel slot } control)
        {
            return;
        }

        if (e.ChangedButton != PlatformInput.MouseButton.Left)
        {
            return;
        }

        // 这一笔由插槽自己交给表面路由 —— 宿主上收不到按下，句柄只有这里取得到。订阅者在插槽自己的
        // InputRelay 上置 PreventDefault 就是「这一次别连」；不读它，插槽上就没有任何可定制的地方。
        if (WorkflowSurfaceBehavior.RouteComponentPress(control, slot, e)?.PreventDefault == true)
        {
            return;
        }

        if (!slot.SendConnectionCommand.CanExecute(null))
        {
            return;
        }

        // 上一次没收干净（松手落在窗口外、或落在一个吃掉事件的地方）时，树那边还挂着一根橡皮筋，
        // 它会拒绝起新的连接 —— 表现为「端口概率性点不动」。先把旧的收掉。
        CancelLeftover(control);

        slot.SendConnectionCommand.Execute(null);
        StateOf(control).Sender = slot;
        e.Handled = true;
    }

    private static void OnPointerReleased(object? sender, MouseButtonEventArgs e)
    {
        if (sender is not Control { DataContext: IWorkflowSlotViewModel slot } control)
        {
            return;
        }

        var state = StateOf(control);

        // 松回发起口自己身上：这一拖没有落到别的口上，收货人就是它自己 —— 不算连成，收回虚拟连线。
        if (ReferenceEquals(state.Sender, slot))
        {
            Cancel(control);
            e.Handled = true;
            return;
        }

        if (slot.ReceiveConnectionCommand.CanExecute(null))
        {
            slot.ReceiveConnectionCommand.Execute(null);
            state.Sender = null;
            e.Handled = true;
            return;
        }

        // 落在一个接不了的口上：同样收回。
        Cancel(control);
        e.Handled = true;
    }

    // 把树上还挂着的虚拟连线收掉。树要从宿主拿 —— 槽自己的 DataContext 是槽，不是树。
    private static void Cancel(Control control)
    {
        StateOf(control).Sender = null;
        CancelLeftover(control);
    }

    private static void CancelLeftover(Control control)
    {
        if (WorkflowSurfaceBehavior.TreeOf(control) is not { } tree || !tree.VirtualLink.IsVisible)
        {
            return;
        }

        if (tree.ResetVirtualLinkCommand.CanExecute(null))
        {
            tree.ResetVirtualLinkCommand.Execute(null);
        }
    }

    private static ConnectionState StateOf(Control control)
    {
        if (!States.TryGetValue(control, out var state))
        {
            state = new ConnectionState();
            States.Add(control, state);
        }

        return state;
    }
}
