using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VisualCanvas = VeloxDev.WinForms.Xaml.Visuals.Controls.Canvas;
using VisualItemsControl = VeloxDev.WinForms.Xaml.Visuals.Controls.ItemsControl;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// WPF-shaped attached behavior that measures the node template's declared slot parts and writes their
/// world-space anchors back to the model.
/// </summary>
/// <remarks>
/// Attach it to the node view root and name the parts with <see cref="SlotNamesProperty"/> and
/// <see cref="SlotEnumeratorNamesProperty"/> (comma-separated <c>x:Name</c>s). The behavior reads the live visual
/// tree; it does not own a host element and does not create slot views.
/// </remarks>
public sealed class WorkflowSlotLayoutBehavior
{
    /// <summary>Whether the behavior measures the declared slot parts.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(false));

    /// <summary>Comma-separated names of visual elements whose data context is one slot.</summary>
    public static readonly DependencyProperty SlotNamesProperty = DependencyProperty.RegisterAttached(
        "SlotNames", typeof(string), typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(null));

    /// <summary>Comma-separated names of visual items controls whose realized items are slots.</summary>
    public static readonly DependencyProperty SlotEnumeratorNamesProperty = DependencyProperty.RegisterAttached(
        "SlotEnumeratorNames", typeof(string), typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(null));

    /// <summary>Name of the element that supplies the coordinate space for slot anchors.</summary>
    public static readonly DependencyProperty CoordinateHostNameProperty = DependencyProperty.RegisterAttached(
        "CoordinateHostName", typeof(string), typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(null));

    private static readonly ConditionalWeakTable<VisualElement, SlotState> States = new();

    private WorkflowSlotLayoutBehavior()
    {
    }

    /// <summary>Reads the layout switch.</summary>
    public static bool GetIsEnabled(VisualElement element) => (bool)element.GetValue(IsEnabledProperty)!;

    /// <summary>Writes the layout switch.</summary>
    public static void SetIsEnabled(VisualElement element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>Reads the named slot parts.</summary>
    public static string? GetSlotNames(VisualElement element) => (string?)element.GetValue(SlotNamesProperty);

    /// <summary>Writes the named slot parts.</summary>
    public static void SetSlotNames(VisualElement element, string? value) => element.SetValue(SlotNamesProperty, value);

    /// <summary>Reads the named slot enumerator parts.</summary>
    public static string? GetSlotEnumeratorNames(VisualElement element)
        => (string?)element.GetValue(SlotEnumeratorNamesProperty);

    /// <summary>Writes the named slot enumerator parts.</summary>
    public static void SetSlotEnumeratorNames(VisualElement element, string? value)
        => element.SetValue(SlotEnumeratorNamesProperty, value);

    /// <summary>Reads the coordinate-host name.</summary>
    public static string? GetCoordinateHostName(VisualElement element)
        => (string?)element.GetValue(CoordinateHostNameProperty);

    /// <summary>Writes the coordinate-host name.</summary>
    public static void SetCoordinateHostName(VisualElement element, string? value)
        => element.SetValue(CoordinateHostNameProperty, value);

    /// <summary>Attaches or detaches the behavior according to the element's current attached values.</summary>
    public static void Ensure(VisualElement element)
    {
        var enabled = element.IsSet(IsEnabledProperty) && GetIsEnabled(element);
        if (enabled)
        {
            Attach(element);
        }
        else
        {
            Detach(element);
        }
    }

    private static void Attach(VisualElement element)
    {
        var state = States.GetValue(element, static owner => new SlotState(owner));
        state.Subscribe();
    }

    private static void Detach(VisualElement element)
    {
        if (States.TryGetValue(element, out var state))
        {
            state.Unsubscribe();
        }
    }

    private sealed class SlotState
    {
        private readonly VisualElement owner;
        private readonly List<VisualItemsControl> enumerators = [];
        private VisualHost? surface;

        public SlotState(VisualElement owner)
        {
            this.owner = owner;
        }

        public void Subscribe()
        {
            owner.PropertyChanged -= OnOwnerPropertyChanged;
            owner.PropertyChanged += OnOwnerPropertyChanged;
            HookSurface();
        }

        public void Unsubscribe()
        {
            owner.PropertyChanged -= OnOwnerPropertyChanged;
            foreach (var enumerator in enumerators)
            {
                enumerator.ItemsRebuilt -= OnItemsRebuilt;
            }

            enumerators.Clear();
            if (surface is not null)
            {
                surface.LayoutUpdated -= OnLayoutUpdated;
                surface = null;
            }
        }

        private void HookSurface()
        {
            var next = owner.Surface;
            if (ReferenceEquals(next, surface))
            {
                return;
            }

            if (surface is not null)
            {
                surface.LayoutUpdated -= OnLayoutUpdated;
            }

            surface = next;
            if (surface is not null)
            {
                surface.LayoutUpdated += OnLayoutUpdated;
            }
        }

        private void OnLayoutUpdated(object? sender, EventArgs e)
        {
            HookSurface();
            Sync();
        }

        private void OnOwnerPropertyChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (ReferenceEquals(e.Property, VisualElement.DataContextProperty)
                || ReferenceEquals(e.Property, SlotNamesProperty)
                || ReferenceEquals(e.Property, SlotEnumeratorNamesProperty)
                || ReferenceEquals(e.Property, CoordinateHostNameProperty)
                || ReferenceEquals(e.Property, VisualElement.WidthProperty)
                || ReferenceEquals(e.Property, VisualElement.HeightProperty))
            {
                owner.InvalidateMeasure();
            }
        }

        private void OnItemsRebuilt(object? sender, EventArgs e) => owner.InvalidateMeasure();

        private void Sync()
        {
            if (owner.DataContext is not IWorkflowComponent)
            {
                return;
            }

            var coordinateHost = ResolveCoordinateHost();
            if (coordinateHost is null)
            {
                return;
            }

            foreach (var name in ParseNames(GetSlotNames(owner)))
            {
                var found = FindNamed(owner, name);
                if (found is not null && found.DataContext is ISlotComponent)
                {
                    WriteAnchor(found, coordinateHost);
                }
            }

            foreach (var name in ParseNames(GetSlotEnumeratorNames(owner)))
            {
                if (FindNamed(owner, name) is not VisualItemsControl items)
                {
                    continue;
                }

                if (!enumerators.Contains(items))
                {
                    enumerators.Add(items);
                    items.ItemsRebuilt += OnItemsRebuilt;
                }

                foreach (var element in EnumerateVisuals(items))
                {
                    if (element.DataContext is ISlotComponent)
                    {
                        WriteAnchor(element, coordinateHost);
                    }
                }
            }
        }

        private void WriteAnchor(VisualElement element, VisualElement coordinateHost)
        {
            if (element.DataContext is not ISlotComponent slot || element.Bounds.Width <= 0f || element.Bounds.Height <= 0f)
            {
                return;
            }

            var surfacePoint = element.PointToSurface(new System.Drawing.PointF(
                element.Bounds.Width / 2f, element.Bounds.Height / 2f));
            var hostPoint = coordinateHost.PointFromSurface(surfacePoint);
            var anchor = WorkflowSurfaceMath.SlotAnchorFromCanvasLocal(hostPoint.X, hostPoint.Y, slot.Anchor.Layer);
            if (double.IsNaN(slot.Anchor.Horizontal)
                || double.IsNaN(slot.Anchor.Vertical)
                || Math.Abs(slot.Anchor.Horizontal - anchor.Horizontal) > 0.01d
                || Math.Abs(slot.Anchor.Vertical - anchor.Vertical) > 0.01d
                || slot.Anchor.Layer != anchor.Layer)
            {
                slot.Anchor = anchor;
            }
        }

        private VisualElement? ResolveCoordinateHost()
        {
            var name = GetCoordinateHostName(owner);
            if (!string.IsNullOrWhiteSpace(name))
            {
                for (var current = owner.ParentElement; current is not null; current = current.ParentElement)
                {
                    if (string.Equals(current.Name, name, StringComparison.Ordinal))
                    {
                        return current;
                    }
                }
            }

            for (var current = owner.ParentElement; current is not null; current = current.ParentElement)
            {
                if (current is VisualCanvas)
                {
                    return current;
                }
            }

            return owner.Surface?.Root as VisualElement;
        }
    }

    private static IEnumerable<VisualElement> EnumerateNamedSlots(VisualElement root, string? names)
    {
        foreach (var name in ParseNames(names))
        {
            if (FindNamed(root, name) is { } element && element.DataContext is ISlotComponent)
            {
                yield return element;
            }
        }
    }

    private static string[] ParseNames(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split([','], StringSplitOptions.RemoveEmptyEntries)
                .Select(static name => name.Trim())
                .Where(static name => name.Length > 0)
                .ToArray();

    private static VisualElement? FindNamed(VisualElement root, string name)
    {
        if (string.Equals(root.Name, name, StringComparison.Ordinal))
        {
            return root;
        }

        foreach (var child in root.Children)
        {
            if (child is VisualElement element && FindNamed(element, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static IEnumerable<VisualElement> EnumerateVisuals(VisualElement root)
    {
        yield return root;
        foreach (var child in root.Children)
        {
            if (child is VisualElement element)
            {
                foreach (var nested in EnumerateVisuals(element))
                {
                    yield return nested;
                }
            }
        }
    }
}
