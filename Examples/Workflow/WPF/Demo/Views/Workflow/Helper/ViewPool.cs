using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Demo.Views.Workflow.Helper;

/// <summary>
/// Behavior > Provide object pool support for Panel
/// <code>helper:ViewPool.ItemsSource="{Binding Items}"</code>
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
        new PropertyMetadata(null));

    public static INotifyCollectionChanged? GetItemsSource(Panel element)
        => (INotifyCollectionChanged?)element.GetValue(ItemsSourceProperty);

    public static void SetItemsSource(Panel element, INotifyCollectionChanged? value)
        => element.SetValue(ItemsSourceProperty, value);

    public static DataTemplateSelector? GetTemplateSelector(Panel element)
        => (DataTemplateSelector?)element.GetValue(TemplateSelectorProperty);

    public static void SetTemplateSelector(Panel element, DataTemplateSelector? value)
        => element.SetValue(TemplateSelectorProperty, value);

    private static readonly ConditionalWeakTable<Panel, ViewManager> _managers = [];

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Panel panel)
            return;

        if (e.OldValue is INotifyCollectionChanged)
            CleanupManager(panel);

        if (e.NewValue is INotifyCollectionChanged newValue)
        {
            var manager = new ViewManager(panel);
            manager.Attach(newValue);
            _managers.Add(panel, manager);
            panel.Unloaded += OnPanelUnloaded;
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
        if (_managers.TryGetValue(panel, out var manager))
        {
            manager.Detach();
            _managers.Remove(panel);
        }
    }
}
