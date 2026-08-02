using System;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// WinForms API shim for workflow view-pool configuration.
/// </summary>
/// <remarks>
/// Mirrors the XAML adapters' <c>ViewPool.ItemsSource</c> / <c>ViewPool.TemplateSelector</c>
/// attached properties. Because WinForms has no attached-property system, the values are
/// stored per control in a <see cref="ConditionalWeakTable{TKey, TValue}"/>. When both an
/// <see cref="ItemsSource"/> and an <see cref="IWorkflowTemplateSelector"/> are configured, a
/// <see cref="ViewManager"/> is started that materializes pooled views into the host control.
/// </remarks>
public sealed class ViewPool
{
    private sealed class PoolState
    {
        public INotifyCollectionChanged? ItemsSource { get; set; }
        public IWorkflowTemplateSelector? TemplateSelector { get; set; }
    }

    private static readonly ConditionalWeakTable<Control, PoolState> States = new();
    private static readonly ConditionalWeakTable<Control, ViewManager> Managers = new();

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
        UpdateManager(element);
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
    /// Sets the configured template selector. Together with <see cref="ItemsSource"/> this
    /// starts view pooling for the host control.
    /// </summary>
    public static void SetTemplateSelector(Control element, IWorkflowTemplateSelector? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).TemplateSelector = value;
        UpdateManager(element);
    }

    private static void UpdateManager(Control element)
    {
        CleanupManager(element);

        var state = GetState(element);
        if (state.ItemsSource is { } collection && state.TemplateSelector is { } selector)
        {
            var manager = new ViewManager(element);
            manager.SetTemplateSelector(selector);
            manager.Attach(collection);
            Managers.Add(element, manager);
            element.Disposed += OnHostDisposed;
        }
    }

    private static void OnHostDisposed(object? sender, EventArgs e)
    {
        if (sender is Control control)
        {
            control.Disposed -= OnHostDisposed;
            CleanupManager(control);
        }
    }

    private static void CleanupManager(Control element)
    {
        if (Managers.TryGetValue(element, out var manager) && manager is not null)
        {
            manager.Detach();
            Managers.Remove(element);
        }
    }

    private static PoolState GetState(Control element)
        => States.GetValue(element, static _ => new PoolState());
}
