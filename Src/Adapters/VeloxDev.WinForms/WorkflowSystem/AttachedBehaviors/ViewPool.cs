using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Easel;
using Easel.Visuals;
using VeloxDev.WorkflowSystem.ViewHosting;
using VisualPanel = Easel.Visuals.Controls.Panel;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// A workflow-free visual view pool: keeps one visual child per item in an observable source, choosing the view
/// through a <see cref="VisualTemplateSelector"/>.
/// </summary>
/// <remarks>
/// <para>
/// Attach it to a visual <see cref="Easel.Visuals.Controls.Canvas"/> and bind
/// <see cref="ItemsSourceProperty"/> to the virtualized item collection (normally <c>Helper.VisibleItems</c>). The
/// pool bookkeeping lives in Core; <see cref="ViewSourcePipeline"/> closes the WinForms layers.
/// </para>
/// <para>
/// Either attached property changing rebuilds the system, so a source or selector change never leaves an old
/// handler attached. <see cref="Refresh"/> forces the same rebuild and is what the surface calls after it resolves
/// the selector from resources.
/// </para>
/// </remarks>
public sealed class ViewPool
{
    /// <summary>Identifies the <c>ItemsSource</c> attached property.</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.RegisterAttached(
        "ItemsSource", typeof(INotifyCollectionChanged), typeof(ViewPool),
        new PropertyMetadata(null));

    /// <summary>Identifies the <c>TemplateSelector</c> attached property.</summary>
    public static readonly DependencyProperty TemplateSelectorProperty = DependencyProperty.RegisterAttached(
        "TemplateSelector", typeof(VisualTemplateSelector), typeof(ViewPool),
        new PropertyMetadata(null));

    /// <summary>
    /// Raised once after a materialization batch that created or reused views on a host.
    /// </summary>
    /// <remarks>
    /// A surface subscribes once so attached behaviors on newly materialized views can be ensured and link styles
    /// applied; the pool itself stays workflow-free. The event is per batch, not per view.
    /// </remarks>
    public static event EventHandler<ViewPoolChangedEventArgs>? Changed;

    private static readonly ConditionalWeakTable<VisualPanel, ViewSourcePipeline> Systems = new();

    /// <summary>Reads the pooled items source attached to <paramref name="element"/>.</summary>
    public static INotifyCollectionChanged? GetItemsSource(VisualElement element)
        => (INotifyCollectionChanged?)element.GetValue(ItemsSourceProperty);

    /// <summary>Sets the pooled items source attached to <paramref name="element"/>.</summary>
    /// <remarks>
    /// Clearing the source disposes the running system, including the views it attached. Replacing it rebuilds the
    /// system in place; setting it for the first time only stores the value until <see cref="Refresh"/> runs.
    /// </remarks>
    public static void SetItemsSource(VisualElement element, INotifyCollectionChanged? value)
    {
        element.SetValue(ItemsSourceProperty, value);
        if (element is not VisualPanel panel)
        {
            return;
        }

        if (value is null)
        {
            Cleanup(panel);
        }
        else if (Systems.TryGetValue(panel, out _))
        {
            Rebuild(panel);
        }
    }

    /// <summary>Reads the template selector attached to <paramref name="element"/>.</summary>
    public static VisualTemplateSelector? GetTemplateSelector(VisualElement element)
        => (VisualTemplateSelector?)element.GetValue(TemplateSelectorProperty);

    /// <summary>Sets the template selector attached to <paramref name="element"/>.</summary>
    /// <remarks>A running system rebuilds so the new selector applies to every view, not only future ones.</remarks>
    public static void SetTemplateSelector(VisualElement element, VisualTemplateSelector? value)
    {
        element.SetValue(TemplateSelectorProperty, value);
        if (element is VisualPanel panel && Systems.TryGetValue(panel, out _))
        {
            Rebuild(panel);
        }
    }

    /// <summary>Gets the number of items that currently have a bound view on <paramref name="host"/>.</summary>
    public static int GetActiveViewCount(VisualElement host)
        => host is VisualPanel panel && Systems.TryGetValue(panel, out var system)
            ? system.Pool.ActiveViewCount
            : 0;

    /// <summary>Gets the number of items waiting for a scheduled materialization batch on <paramref name="host"/>.</summary>
    public static int GetPendingCount(VisualElement host)
        => host is VisualPanel panel && Systems.TryGetValue(panel, out var system)
            ? system.Pool.PendingViewCount
            : 0;

    /// <summary>Rebuilds the views for the current source and configuration.</summary>
    /// <param name="host">The pool host.</param>
    /// <remarks>
    /// The previous system is disposed first, so its views leave the canvas instead of accumulating as hidden
    /// children across rebuilds.
    /// </remarks>
    public static void Refresh(VisualElement host)
    {
        if (host is null) throw new ArgumentNullException(nameof(host));

        if (host is VisualPanel panel)
        {
            Rebuild(panel);
        }
    }

    private static void Rebuild(VisualPanel panel)
    {
        Cleanup(panel);

        if (GetItemsSource(panel) is not { } source)
        {
            return;
        }

        var system = new ViewSourcePipeline(
            panel,
            GetTemplateSelector(panel),
            source,
            () => Changed?.Invoke(null, new ViewPoolChangedEventArgs(panel)));
        Systems.Add(panel, system);
    }

    private static void Cleanup(VisualPanel panel)
    {
        if (Systems.TryGetValue(panel, out var system))
        {
            Systems.Remove(panel);
            system.Dispose();
        }
    }
}

/// <summary>Identifies the visual Canvas whose pooled views changed.</summary>
public sealed class ViewPoolChangedEventArgs(VisualElement host) : EventArgs
{
    /// <summary>The Canvas that owns the pool.</summary>
    public VisualElement Host { get; } = host;
}
