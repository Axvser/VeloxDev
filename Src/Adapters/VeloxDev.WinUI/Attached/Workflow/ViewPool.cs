using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Behavior > Provide object pool support for Panel
/// <code>behaviors:ViewPool.ItemsSource="{Binding Items}"</code>
/// </summary>
public sealed class ViewPool : DependencyObject
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.RegisterAttached(
        "ItemsSource",
        typeof(INotifyCollectionChanged),
        typeof(ViewPool),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty TemplateSelectorProperty = DependencyProperty.RegisterAttached(
        "TemplateSelector",
        typeof(DataTemplateSelector),
        typeof(ViewPool),
        new PropertyMetadata(null, OnTemplateSelectorChanged));

    private static readonly ConditionalWeakTable<Panel, ViewManager> Managers = new();

    public static INotifyCollectionChanged? GetItemsSource(Panel element)
        => (INotifyCollectionChanged?)element.GetValue(ItemsSourceProperty);

    public static void SetItemsSource(Panel element, INotifyCollectionChanged? value)
        => element.SetValue(ItemsSourceProperty, value);

    public static DataTemplateSelector? GetTemplateSelector(Panel element)
        => (DataTemplateSelector?)element.GetValue(TemplateSelectorProperty);

    public static void SetTemplateSelector(Panel element, DataTemplateSelector? value)
        => element.SetValue(TemplateSelectorProperty, value);

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Panel panel)
        {
            return;
        }

        if (e.OldValue is INotifyCollectionChanged)
        {
            CleanupManager(panel);
        }

        if (e.NewValue is not INotifyCollectionChanged newValue)
        {
            return;
        }

        var manager = new ViewManager(panel);
        manager.SetTemplateSelector(GetTemplateSelector(panel));
        manager.Attach(newValue);
        Managers.Add(panel, manager);
        panel.Unloaded += OnPanelUnloaded;
    }

    private static void OnTemplateSelectorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Panel panel)
        {
            return;
        }

        if (Managers.TryGetValue(panel, out var manager))
        {
            manager.SetTemplateSelector(e.NewValue as DataTemplateSelector);
        }
    }

    private static void OnPanelUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Panel panel)
        {
            panel.Unloaded -= OnPanelUnloaded;
            CleanupManager(panel);
        }
    }

    private static void CleanupManager(Panel panel)
    {
        if (Managers.TryGetValue(panel, out var manager))
        {
            manager.Detach();
            Managers.Remove(panel);
        }
    }
}
