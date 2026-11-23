using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Easel;
using Easel.Visuals;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// WPF-shaped attached behavior that starts and completes a slot connection from the marked slot view.
/// </summary>
/// <remarks>
/// Attach it to the slot view root. Press sends the slot's connection command; release sends the receive
/// command. The surface forwards pointer movement to the tree, so the preview receiver and the virtual link
/// update while the pointer is captured.
/// </remarks>
public sealed class WorkflowSlotConnectionBehavior
{
    /// <summary>Whether this slot view starts and completes a connection drag.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(WorkflowSlotConnectionBehavior),
        new PropertyMetadata(false));

    private static readonly ConditionalWeakTable<VisualElement, ConnectionState> States = new();

    private WorkflowSlotConnectionBehavior()
    {
    }

    /// <summary>Reads the connection switch.</summary>
    public static bool GetIsEnabled(VisualElement element) => (bool)element.GetValue(IsEnabledProperty)!;

    /// <summary>Writes the connection switch.</summary>
    public static void SetIsEnabled(VisualElement element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>Attaches or detaches the behavior according to the element's current attached values.</summary>
    public static void Ensure(VisualElement element)
    {
        if (element.IsSet(IsEnabledProperty) && GetIsEnabled(element))
        {
            States.GetValue(element, static owner => new ConnectionState(owner)).Subscribe();
        }
        else if (States.TryGetValue(element, out var state))
        {
            state.Unsubscribe();
        }
    }

    private sealed class ConnectionState
    {
        private readonly VisualElement owner;
        private bool subscribed;

        public ConnectionState(VisualElement owner)
        {
            this.owner = owner;
        }

        public void Subscribe()
        {
            if (subscribed)
            {
                return;
            }

            subscribed = true;
            owner.PointerPressed += OnPointerPressed;
            owner.PointerReleased += OnPointerReleased;
        }

        public void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }

            subscribed = false;
            owner.PointerPressed -= OnPointerPressed;
            owner.PointerReleased -= OnPointerReleased;
        }

        private void OnPointerPressed(object? sender, VisualPointerEventArgs args)
        {
            if (args.Button != MouseButtons.Left
                || owner.DataContext is not ISlotComponent slot
                || WorkflowSurfaceBehavior.GetPressHandle(owner)?.PreventDefault == true)
            {
                return;
            }

            slot.SendConnectionCommand.Execute(null);
            args.Handled = true;
        }

        private void OnPointerReleased(object? sender, VisualPointerEventArgs args)
        {
            if (owner.DataContext is not ISlotComponent senderSlot || owner.Surface is not { } surface)
            {
                return;
            }

            // VisualHost captures the sender, so the release arrives here even when the pointer is over the
            // receiver. Resolve the slot under the release point and let it accept the connection.
            var hit = surface.FindElementAt(args.Position);
            for (var current = hit; current is not null; current = current.ParentElement)
            {
                if (current.DataContext is ISlotComponent receiver
                    && !ReferenceEquals(receiver, senderSlot))
                {
                    receiver.ReceiveConnectionCommand.Execute(null);
                    args.Handled = true;
                    return;
                }
            }
        }
    }
}
