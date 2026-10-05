using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Jalium.UI;
using Jalium.UI.Controls;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>Attached properties that drive a <see cref="ViewManager"/> from a <see cref="Panel"/>, binding its
/// items source (usually the tree's visible set) and the <see cref="DataTemplateSelector"/> that resolves each
/// item's view.</summary>
public static class ViewPool
{
    private static readonly ConditionalWeakTable<Panel, ViewManager> s_managers = new();

    /// <summary>Identifies the <c>ItemsSource</c> attached property.</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.RegisterAttached(
        "ItemsSource",
        typeof(INotifyCollectionChanged),
        typeof(ViewPool),
        new PropertyMetadata(null, OnChanged));

    /// <summary>Identifies the <c>TemplateSelector</c> attached property.</summary>
    public static readonly DependencyProperty TemplateSelectorProperty = DependencyProperty.RegisterAttached(
        "TemplateSelector",
        typeof(DataTemplateSelector),
        typeof(ViewPool),
        new PropertyMetadata(null, OnChanged));

    /// <summary>Reads the items source attached to <paramref name="element"/>.</summary>
    public static INotifyCollectionChanged? GetItemsSource(Panel element)
        => (INotifyCollectionChanged?)element.GetValue(ItemsSourceProperty);

    /// <summary>Sets the items source attached to <paramref name="element"/>.</summary>
    public static void SetItemsSource(Panel element, INotifyCollectionChanged? value)
        => element.SetValue(ItemsSourceProperty, value);

    /// <summary>Reads the template selector attached to <paramref name="element"/>.</summary>
    public static DataTemplateSelector? GetTemplateSelector(Panel element)
        => (DataTemplateSelector?)element.GetValue(TemplateSelectorProperty);

    /// <summary>Sets the template selector attached to <paramref name="element"/>.</summary>
    public static void SetTemplateSelector(Panel element, DataTemplateSelector? value)
        => element.SetValue(TemplateSelectorProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Panel panel)
        {
            return;
        }

        var items = GetItemsSource(panel);
        var selector = GetTemplateSelector(panel);

        if (items is not null && selector is not null)
        {
            if (!s_managers.TryGetValue(panel, out var manager))
            {
                manager = new ViewManager(panel);
                s_managers.Add(panel, manager);
                panel.Unloaded += (_, _) => manager.Dispose();
            }

            manager.SetTemplateSelector(selector);
            manager.Attach(items);
        }
        else if (s_managers.TryGetValue(panel, out var existing))
        {
            existing.Detach();
        }
    }
}
