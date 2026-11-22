using System.ComponentModel;
using System.Runtime.CompilerServices;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.VisualHosting;

/// <summary>
/// Binds a plain visual element to one workflow slot, the visual-backend counterpart of the platform slot behaviors.
/// </summary>
/// <remarks>
/// The view keeps its own markup and paint; this behavior owns only the model link and the repaint when the slot's
/// state or channel changes. A template reads the slot with <see cref="GetSlot"/> — it never derives from a
/// workflow-specific view class.
/// </remarks>
public static class VisualWorkflowSlotBehavior
{
    private static readonly ConditionalWeakTable<VisualElement, SlotState> States = new();

    /// <summary>Returns the slot bound to <paramref name="view"/>, or <see langword="null"/>.</summary>
    public static ISlotComponent? GetSlot(VisualElement view)
        => view is not null && States.TryGetValue(view, out var state) ? state.Slot : null;

    internal static void Attach(VisualElement view, ISlotComponent slot)
    {
        if (view is null) throw new ArgumentNullException(nameof(view));
        if (slot is null) throw new ArgumentNullException(nameof(slot));

        if (States.TryGetValue(view, out var existing) && ReferenceEquals(existing.Slot, slot))
        {
            return;
        }

        Detach(view);
        var state = new SlotState(view, slot);
        States.Add(view, state);
        state.Subscribe();
        view.InvalidateVisual();
    }

    internal static void Detach(VisualElement view)
    {
        if (States.TryGetValue(view, out var state))
        {
            state.Unsubscribe();
            States.Remove(view);
        }
    }

    private sealed class SlotState(VisualElement view, ISlotComponent slot)
    {
        private INotifyPropertyChanged? _notifier;

        internal ISlotComponent Slot { get; } = slot;

        internal void Subscribe()
        {
            if (Slot is not INotifyPropertyChanged notifier)
            {
                return;
            }

            _notifier = notifier;
            _notifier.PropertyChanged += OnSlotPropertyChanged;
        }

        internal void Unsubscribe()
        {
            if (_notifier is not null)
            {
                _notifier.PropertyChanged -= OnSlotPropertyChanged;
                _notifier = null;
            }
        }

        private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ISlotComponent.State)
                or nameof(ISlotComponent.Channel)
                or nameof(ISlotComponent.Anchor))
            {
                view.InvalidateVisual();
            }
        }
    }
}
