using Avalonia;
using Avalonia.Controls;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;

namespace Demo;

/// <summary>
/// Behavior > Provide object pool support for Panel
/// <code>local:ViewPool.ItemsSource="{Binding Items}"</code>
/// </summary>
public sealed class ViewPool : AvaloniaObject
{
    static ViewPool()
    {
        ItemsSourceProperty.Changed.AddClassHandler<Panel>(OnItemsSourceChanged);
    }

    public static readonly AttachedProperty<INotifyCollectionChanged?> ItemsSourceProperty =
        AvaloniaProperty.RegisterAttached<ViewPool, Panel, INotifyCollectionChanged?>(
            "ItemsSource", defaultValue: null);

    public static INotifyCollectionChanged? GetItemsSource(Panel element)
        => element.GetValue(ItemsSourceProperty);

    public static void SetItemsSource(Panel element, INotifyCollectionChanged? value)
        => element.SetValue(ItemsSourceProperty, value);

    private static void OnItemsSourceChanged(Panel panel, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.OldValue is INotifyCollectionChanged oldValue)
        {
            CleanupManager(panel);
        }

        if (args.NewValue is INotifyCollectionChanged newValue)
        {
            var manager = new ViewManager(panel);
            manager.Attach(newValue);
            _managers.Add(panel, manager);
            panel.DetachedFromVisualTree += OnPanelDetached;
        }
    }

    private static readonly ConditionalWeakTable<Panel, ViewManager> _managers = [];

    private static void OnPanelDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Panel panel)
        {
            panel.DetachedFromVisualTree -= OnPanelDetached;
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