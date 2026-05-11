using System;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// WinForms API shim for workflow view-pool configuration.
/// </summary>
public sealed class ViewPool
{
    private sealed class PoolState
    {
        public INotifyCollectionChanged? ItemsSource { get; set; }
        public object? TemplateSelector { get; set; }
    }

    private static readonly ConditionalWeakTable<Control, PoolState> States = new();

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
    }

    /// <summary>
    /// Gets the configured template selector analogue.
    /// </summary>
    public static object? GetTemplateSelector(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).TemplateSelector;
    }

    /// <summary>
    /// Sets the configured template selector analogue.
    /// </summary>
    public static void SetTemplateSelector(Control element, object? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).TemplateSelector = value;
    }

    private static PoolState GetState(Control element)
        => States.GetValue(element, static _ => new PoolState());
}
