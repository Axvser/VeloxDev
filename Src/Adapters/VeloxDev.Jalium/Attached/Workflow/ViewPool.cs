using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>Attached properties that drive a <see cref="ViewManager"/> from a Panel, binding its
/// ItemsSource (usually the tree's Helper.VisibleItems) and a factory template selector.</summary>
public static class ViewPool
{
    private static readonly ConditionalWeakTable<Panel, ViewManager> s_managers = new();

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.RegisterAttached(
        "ItemsSource",
        typeof(INotifyCollectionChanged),
        typeof(ViewPool),
        new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty TemplateSelectorProperty = DependencyProperty.RegisterAttached(
        "TemplateSelector",
        typeof(IWorkflowTemplateSelector),
        typeof(ViewPool),
        new PropertyMetadata(null, OnChanged));

    public static INotifyCollectionChanged? GetItemsSource(Panel element)
        => (INotifyCollectionChanged?)element.GetValue(ItemsSourceProperty);

    public static void SetItemsSource(Panel element, INotifyCollectionChanged? value)
        => element.SetValue(ItemsSourceProperty, value);

    public static IWorkflowTemplateSelector? GetTemplateSelector(Panel element)
        => (IWorkflowTemplateSelector?)element.GetValue(TemplateSelectorProperty);

    public static void SetTemplateSelector(Panel element, IWorkflowTemplateSelector? value)
        => element.SetValue(TemplateSelectorProperty, value);

    /// <summary>Pushes the canvas content translate onto every pooled view's RenderTransform.</summary>
    internal static void UpdateRenderTransforms(Panel panel, Transform transform)
    {
        if (s_managers.TryGetValue(panel, out var manager))
        {
            manager.UpdateRenderTransforms(transform);
        }
    }

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
