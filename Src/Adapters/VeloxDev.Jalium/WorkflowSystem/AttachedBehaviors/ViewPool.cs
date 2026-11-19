using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Jalium.UI;
using Jalium.UI.Controls;
using VeloxDev.WorkflowSystem.ViewHosting;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Behavior > Host the Core-driven view pool on a <see cref="Panel"/>.
/// <code>behaviors:ViewPool.ItemsSource="{Binding Helper.VisibleItems}"</code>
/// <code>behaviors:ViewPool.TemplateSelector="{StaticResource WorkflowTemplateSelector}"</code>
/// </summary>
/// <remarks>
/// The pool bookkeeping lives in <see cref="ViewPool{THost,TView,TItem,TPriorityCore}"/>;
/// <see cref="ViewSourcePipeline"/> closes the Jalium layers. This attached property is the only Jalium
/// entry for the Core-driven pipeline.
/// </remarks>
public static class ViewPool
{
    private static readonly ConditionalWeakTable<Panel, ViewSourcePipeline> s_systems = new();

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

        Rebuild(panel);
    }

    // 换源或换选择器都是全拆重建：旧系统的池按旧的模板解析结果缓存，增量对齐会留下用错模板的视图。
    private static void Rebuild(Panel panel)
    {
        Cleanup(panel);

        if (GetItemsSource(panel) is not { } source)
        {
            return;
        }

        var system = new ViewSourcePipeline(panel, GetTemplateSelector(panel), source);
        s_systems.Add(panel, system);
        panel.Unloaded += OnPanelUnloaded;
    }

    private static void OnPanelUnloaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Panel panel)
        {
            Cleanup(panel);
        }
    }

    private static void Cleanup(Panel panel)
    {
        // 每次重建都先摘旧处理器，否则面板会根住旧系统的整条事件链。
        panel.Unloaded -= OnPanelUnloaded;

        if (s_systems.TryGetValue(panel, out var system))
        {
            system.Dispose();
            s_systems.Remove(panel);
        }
    }
}
