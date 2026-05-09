using System.Collections;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;

namespace Demo.Controls;

/// <summary>
/// Behavior > Provide object pool support for Layout.
/// </summary>
public sealed class ViewPool
{
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.CreateAttached(
        "ItemsSource",
        typeof(INotifyCollectionChanged),
        typeof(ViewPool),
        default(INotifyCollectionChanged),
        propertyChanged: OnItemsSourceChanged);

    public static readonly BindableProperty TemplateSelectorProperty = BindableProperty.CreateAttached(
        "TemplateSelector",
        typeof(DataTemplateSelector),
        typeof(ViewPool),
        default(DataTemplateSelector),
        propertyChanged: OnTemplateSelectorChanged);

    public static INotifyCollectionChanged? GetItemsSource(BindableObject element)
        => (INotifyCollectionChanged?)element.GetValue(ItemsSourceProperty);

    public static void SetItemsSource(BindableObject element, INotifyCollectionChanged? value)
        => element.SetValue(ItemsSourceProperty, value);

    public static DataTemplateSelector? GetTemplateSelector(BindableObject element)
        => (DataTemplateSelector?)element.GetValue(TemplateSelectorProperty);

    public static void SetTemplateSelector(BindableObject element, DataTemplateSelector? value)
        => element.SetValue(TemplateSelectorProperty, value);

    private static readonly ConditionalWeakTable<Layout, ViewManager> Managers = [];

    private static void OnItemsSourceChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not Layout layout)
        {
            return;
        }

        if (oldValue is INotifyCollectionChanged)
        {
            CleanupManager(layout);
        }

        if (newValue is INotifyCollectionChanged)
        {
            EnsureManager(layout);
        }
    }

    private static void OnTemplateSelectorChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not Layout layout)
        {
            return;
        }

        CleanupManager(layout);
        EnsureManager(layout);
    }

    private static void OnLayoutHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is Layout { Handler: null } layout)
        {
            layout.HandlerChanged -= OnLayoutHandlerChanged;
            CleanupManager(layout);
        }
    }

    private static void CleanupManager(Layout layout)
    {
        if (Managers.TryGetValue(layout, out var manager))
        {
            manager.Detach();
            Managers.Remove(layout);
        }

        layout.HandlerChanged -= OnLayoutHandlerChanged;
    }

    private static void EnsureManager(Layout layout)
    {
        if (Managers.TryGetValue(layout, out _))
        {
            return;
        }

        if (GetItemsSource(layout) is not INotifyCollectionChanged collection || GetTemplateSelector(layout) is null)
        {
            return;
        }

        var manager = new ViewManager(layout);
        manager.Attach(collection);
        Managers.Add(layout, manager);
        layout.HandlerChanged -= OnLayoutHandlerChanged;
        layout.HandlerChanged += OnLayoutHandlerChanged;
    }
}
