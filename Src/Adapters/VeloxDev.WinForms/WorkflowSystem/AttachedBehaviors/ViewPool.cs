using System;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem.ViewHosting;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// WinForms entry point for the Core-driven workflow view pool.
/// </summary>
/// <remarks>
/// Mirrors the XAML adapters' <c>ViewPool.ItemsSource</c> / <c>ViewPool.TemplateSelector</c> attached properties.
/// Because WinForms has no attached-property system, the values are stored per control in a
/// <see cref="ConditionalWeakTable{TKey, TValue}"/>. When both an items source and an
/// <see cref="IWorkflowTemplateSelector"/> are configured, a <see cref="ViewSourcePipeline"/> starts and the Core
/// pool materializes one view per visible node into the host control. Link items are handled by
/// <see cref="WorkflowLinkLayer"/> on the surface canvas instead; a host that uses this attached property directly
/// must enable that layer, or links will not be drawn.
/// </remarks>
public sealed class ViewPool
{
    private sealed class PoolState
    {
        public INotifyCollectionChanged? ItemsSource { get; set; }
        public IWorkflowTemplateSelector? TemplateSelector { get; set; }
    }

    private static readonly ConditionalWeakTable<Control, PoolState> States = new();
    private static readonly ConditionalWeakTable<Control, ViewSourcePipeline> Systems = new();

    /// <summary>
    /// Gets the configured pooled items source.
    /// </summary>
    public static INotifyCollectionChanged? GetItemsSource(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).ItemsSource;
    }

    /// <summary>
    /// Sets the configured pooled items source.
    /// </summary>
    public static void SetItemsSource(Control element, INotifyCollectionChanged? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).ItemsSource = value;
        UpdateSystem(element);
    }

    /// <summary>Gets the number of views still waiting to be materialized by the pool on <paramref name="element"/>.</summary>
    public static int GetPendingViewCount(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return Systems.TryGetValue(element, out var system) ? system.Pool.PendingViewCount : 0;
    }

    /// <summary>
    /// Gets the configured template selector.
    /// </summary>
    public static IWorkflowTemplateSelector? GetTemplateSelector(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).TemplateSelector;
    }

    /// <summary>
    /// Sets the configured template selector. Together with <see cref="SetItemsSource"/> this starts view pooling
    /// for the host control.
    /// </summary>
    public static void SetTemplateSelector(Control element, IWorkflowTemplateSelector? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).TemplateSelector = value;
        UpdateSystem(element);
    }

    private static void UpdateSystem(Control element)
    {
        Cleanup(element);

        var state = GetState(element);
        if (state.ItemsSource is { } collection && state.TemplateSelector is { } selector)
        {
            var system = new ViewSourcePipeline(element, selector, collection);
            Systems.Add(element, system);
            element.Disposed += OnHostDisposed;
        }
    }

    private static void OnHostDisposed(object? sender, EventArgs e)
    {
        if (sender is Control control)
        {
            control.Disposed -= OnHostDisposed;
            Cleanup(control);
        }
    }

    private static void Cleanup(Control element)
    {
        element.Disposed -= OnHostDisposed;

        if (Systems.TryGetValue(element, out var system))
        {
            system.Dispose();
            Systems.Remove(element);
        }
    }

    private static PoolState GetState(Control element)
        => States.GetValue(element, static _ => new PoolState());
}
