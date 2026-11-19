using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using VeloxDev.WorkflowSystem.ViewHosting;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Behavior > Host the Core-driven view pool on a Panel.
/// <code>behaviors:ViewPool.ItemsSource="{Binding Items}"</code>
/// </summary>
/// <remarks>
/// The pool bookkeeping lives in <see cref="ViewPool{THost,TView,TItem,TPriorityCore}"/>;
/// <see cref="ViewSourcePipeline"/> closes the WinUI layers. This attached property is the only
/// WinUI entry for the Core-driven pipeline; the old ViewManager implementation no longer exists.
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

    private static readonly ConditionalWeakTable<Panel, ViewSourcePipeline> Systems = [];

    /// <summary>Reads the pooled items source attached to <paramref name="element"/>.</summary>
    /// <param name="element">The panel to read from.</param>
    /// <returns>The attached collection, or <see langword="null"/>.</returns>
    public static INotifyCollectionChanged? GetItemsSource(Panel element)
        => (INotifyCollectionChanged?)element.GetValue(ItemsSourceProperty);

    /// <summary>Sets the pooled items source attached to <paramref name="element"/>.</summary>
    /// <param name="element">The panel to write to.</param>
    /// <param name="value">The visible-item collection, or <see langword="null"/> to stop pooling.</param>
    public static void SetItemsSource(Panel element, INotifyCollectionChanged? value)
        => element.SetValue(ItemsSourceProperty, value);

    /// <summary>Reads the template selector attached to <paramref name="element"/>.</summary>
    /// <param name="element">The panel to read from.</param>
    /// <returns>The attached selector, or <see langword="null"/>.</returns>
    public static DataTemplateSelector? GetTemplateSelector(Panel element)
        => (DataTemplateSelector?)element.GetValue(TemplateSelectorProperty);

    /// <summary>Sets the template selector attached to <paramref name="element"/>.</summary>
    /// <param name="element">The panel to write to.</param>
    /// <param name="value">The selector consulted before resource keys, or <see langword="null"/>.</param>
    public static void SetTemplateSelector(Panel element, DataTemplateSelector? value)
        => element.SetValue(TemplateSelectorProperty, value);

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Panel panel)
        {
            Rebuild(panel);
        }
    }

    private static void OnTemplateSelectorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Panel panel)
        {
            Rebuild(panel);
        }
    }

    private static void Rebuild(Panel panel)
    {
        Cleanup(panel);

        if (GetItemsSource(panel) is not { } source)
        {
            return;
        }

        var system = new ViewSourcePipeline(panel, GetTemplateSelector(panel), source);
        Systems.Add(panel, system);
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
        // 换源/换模板都会重新 +=，不先 -= 会让面板根住旧的处理链。
        panel.Unloaded -= OnPanelUnloaded;

        if (Systems.TryGetValue(panel, out var system))
        {
            system.Dispose();
            Systems.Remove(panel);
        }
    }
}
