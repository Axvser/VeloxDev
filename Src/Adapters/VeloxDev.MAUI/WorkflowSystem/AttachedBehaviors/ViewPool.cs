using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using VeloxDev.WorkflowSystem.ViewHosting;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Behavior > Host the Core-driven view pool on a <see cref="Layout"/>.
/// <code>behaviors:ViewPool.ItemsSource="{Binding Helper.VisibleItems}"</code>
/// </summary>
/// <remarks>
/// The pool bookkeeping lives in <see cref="ViewPool{THost,TView,TItem,TPriorityCore}"/>;
/// <see cref="ViewSourcePipeline"/> closes the MAUI layers. This attached property is the only MAUI
/// entry for the Core-driven pipeline.
/// </remarks>
public sealed class ViewPool
{
    /// <summary>Identifies the <c>ItemsSource</c> attached property.</summary>
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.CreateAttached(
        "ItemsSource",
        typeof(INotifyCollectionChanged),
        typeof(ViewPool),
        default(INotifyCollectionChanged),
        propertyChanged: OnItemsSourceChanged);

    /// <summary>Identifies the <c>TemplateSelector</c> attached property.</summary>
    public static readonly BindableProperty TemplateSelectorProperty = BindableProperty.CreateAttached(
        "TemplateSelector",
        typeof(DataTemplateSelector),
        typeof(ViewPool),
        default(DataTemplateSelector),
        propertyChanged: OnTemplateSelectorChanged);

    /// <summary>Reads the pooled items source attached to <paramref name="element"/>.</summary>
    /// <param name="element">The element carrying the attached property.</param>
    /// <returns>The items source, or <see langword="null"/> when none is attached.</returns>
    public static INotifyCollectionChanged? GetItemsSource(BindableObject element)
        => (INotifyCollectionChanged?)element.GetValue(ItemsSourceProperty);

    /// <summary>Sets the pooled items source attached to <paramref name="element"/>.</summary>
    /// <param name="element">The element carrying the attached property.</param>
    /// <param name="value">The visible-item collection, or <see langword="null"/> to detach the pool.</param>
    public static void SetItemsSource(BindableObject element, INotifyCollectionChanged? value)
        => element.SetValue(ItemsSourceProperty, value);

    /// <summary>Reads the template selector attached to <paramref name="element"/>.</summary>
    /// <param name="element">The element carrying the attached property.</param>
    /// <returns>The selector, or <see langword="null"/> when none is attached.</returns>
    public static DataTemplateSelector? GetTemplateSelector(BindableObject element)
        => (DataTemplateSelector?)element.GetValue(TemplateSelectorProperty);

    /// <summary>Sets the template selector attached to <paramref name="element"/>.</summary>
    /// <param name="element">The element carrying the attached property.</param>
    /// <param name="value">The selector, or <see langword="null"/> to detach the pool.</param>
    public static void SetTemplateSelector(BindableObject element, DataTemplateSelector? value)
        => element.SetValue(TemplateSelectorProperty, value);

    private static readonly ConditionalWeakTable<Layout, ViewSourcePipeline> Systems = [];

    private static void OnItemsSourceChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not Layout layout)
        {
            return;
        }

        if (oldValue is INotifyCollectionChanged)
        {
            Cleanup(layout);
        }

        if (newValue is INotifyCollectionChanged)
        {
            Ensure(layout);
        }
    }

    private static void OnTemplateSelectorChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not Layout layout)
        {
            return;
        }

        // 换模板必须重建系统：选择器结果按类型缓存在 ViewFactory 里。
        Cleanup(layout);
        Ensure(layout);
    }

    private static void OnLayoutHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is Layout { Handler: null } layout)
        {
            layout.HandlerChanged -= OnLayoutHandlerChanged;
            Cleanup(layout);
        }
    }

    private static void Ensure(Layout layout)
    {
        if (Systems.TryGetValue(layout, out _))
        {
            return;
        }

        if (GetItemsSource(layout) is not INotifyCollectionChanged source ||
            GetTemplateSelector(layout) is not DataTemplateSelector selector)
        {
            return;
        }

        var system = new ViewSourcePipeline(layout, selector, source);
        Systems.Add(layout, system);
        layout.HandlerChanged -= OnLayoutHandlerChanged;
        layout.HandlerChanged += OnLayoutHandlerChanged;
    }

    private static void Cleanup(Layout layout)
    {
        if (Systems.TryGetValue(layout, out var system))
        {
            system.Dispose();
            Systems.Remove(layout);
        }

        layout.HandlerChanged -= OnLayoutHandlerChanged;
    }
}
