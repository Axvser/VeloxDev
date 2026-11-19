using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using VeloxDev.WorkflowSystem.ViewHosting;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Behavior > Host the Core-driven view pool on a Panel.
/// <code>helper:ViewPool.ItemsSource="{Binding Items}"</code>
/// </summary>
/// <remarks>
/// The pool bookkeeping lives in <see cref="ViewPool{THost,TView,TItem,TPriorityCore}"/>;
/// <see cref="ViewSourcePipeline"/> closes the WPF layers. Either attached property changing rebuilds
/// the system, so a source or selector change never leaves an old handler attached.
/// </remarks>
public sealed class ViewPool : DependencyObject
{
    /// <summary>Identifies the <c>ItemsSource</c> attached property.</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.RegisterAttached(
        "ItemsSource",
        typeof(INotifyCollectionChanged),
        typeof(ViewPool),
        new PropertyMetadata(null, OnItemsSourceChanged));

    /// <summary>Identifies the <c>TemplateSelector</c> attached property.</summary>
    public static readonly DependencyProperty TemplateSelectorProperty = DependencyProperty.RegisterAttached(
        "TemplateSelector",
        typeof(DataTemplateSelector),
        typeof(ViewPool),
        new PropertyMetadata(null, OnTemplateSelectorChanged));

    /// <summary>Reads the pooled items source attached to <paramref name="element"/>.</summary>
    public static INotifyCollectionChanged? GetItemsSource(Panel element)
        => (INotifyCollectionChanged?)element.GetValue(ItemsSourceProperty);

    /// <summary>Sets the pooled items source attached to <paramref name="element"/>.</summary>
    public static void SetItemsSource(Panel element, INotifyCollectionChanged? value)
        => element.SetValue(ItemsSourceProperty, value);

    /// <summary>Reads the template selector attached to <paramref name="element"/>.</summary>
    public static DataTemplateSelector? GetTemplateSelector(Panel element)
        => (DataTemplateSelector?)element.GetValue(TemplateSelectorProperty);

    /// <summary>Sets the template selector attached to <paramref name="element"/>.</summary>
    public static void SetTemplateSelector(Panel element, DataTemplateSelector? value)
        => element.SetValue(TemplateSelectorProperty, value);

    private static readonly ConditionalWeakTable<Panel, ViewSourcePipeline> _systems = new();

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => RebuildIfPanel(d);

    private static void OnTemplateSelectorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => RebuildIfPanel(d);

    private static void RebuildIfPanel(DependencyObject d)
    {
        if (d is Panel panel)
        {
            Rebuild(panel);
        }
    }

    // 换源/换模板都重建系统：旧 system 先 Dispose 并摘掉 Unloaded 处理器，避免面板根住旧链路。
    private static void Rebuild(Panel panel)
    {
        Cleanup(panel);

        if (GetItemsSource(panel) is not { } source)
        {
            return;
        }

        var system = new ViewSourcePipeline(panel, GetTemplateSelector(panel), source);
        _systems.Add(panel, system);
        panel.Unloaded += OnPanelUnloaded;
    }

    private static void OnPanelUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Panel panel)
        {
            Cleanup(panel);
        }
    }

    private static void Cleanup(Panel panel)
    {
        panel.Unloaded -= OnPanelUnloaded;

        if (_systems.TryGetValue(panel, out var system))
        {
            system.Dispose();
            _systems.Remove(panel);
        }
    }
}
