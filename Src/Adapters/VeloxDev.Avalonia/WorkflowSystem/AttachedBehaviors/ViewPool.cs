using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using VeloxDev.WorkflowSystem.ViewHosting;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Behavior > Host the Core-driven view pool on a Panel.
/// <code>local:ViewPool.ItemsSource="{Binding Helper.VisibleItems}"</code>
/// </summary>
/// <remarks>
/// The pool bookkeeping lives in <see cref="ViewPool{THost,TView,TItem,TPriorityCore}"/>;
/// <see cref="ViewSourcePipeline"/> closes the platform layers. This attached property is the only
/// Avalonia entry for the Core-driven pipeline; the old ViewManager implementation no longer exists.
/// </remarks>
public sealed class ViewPool : AvaloniaObject
{
    static ViewPool()
    {
        ItemsSourceProperty.Changed.AddClassHandler<Panel>(OnItemsSourceChanged);
        TemplateSelectorProperty.Changed.AddClassHandler<Panel>(OnTemplateSelectorChanged);
    }

    /// <summary>Identifies the <c>ItemsSource</c> attached property.</summary>
    public static readonly AttachedProperty<INotifyCollectionChanged?> ItemsSourceProperty =
        AvaloniaProperty.RegisterAttached<ViewPool, Panel, INotifyCollectionChanged?>(
            "ItemsSource", defaultValue: null);

    /// <summary>Identifies the <c>TemplateSelector</c> attached property.</summary>
    public static readonly AttachedProperty<IDataTemplate?> TemplateSelectorProperty =
        AvaloniaProperty.RegisterAttached<ViewPool, Panel, IDataTemplate?>(
            "TemplateSelector", defaultValue: null);

    /// <summary>Reads the pooled items source attached to <paramref name="element"/>.</summary>
    public static INotifyCollectionChanged? GetItemsSource(Panel element)
        => element.GetValue(ItemsSourceProperty);

    /// <summary>Sets the pooled items source attached to <paramref name="element"/>.</summary>
    public static void SetItemsSource(Panel element, INotifyCollectionChanged? value)
        => element.SetValue(ItemsSourceProperty, value);

    /// <summary>Reads the template selector attached to <paramref name="element"/>.</summary>
    public static IDataTemplate? GetTemplateSelector(Panel element)
        => element.GetValue(TemplateSelectorProperty);

    /// <summary>Sets the template selector attached to <paramref name="element"/>.</summary>
    public static void SetTemplateSelector(Panel element, IDataTemplate? value)
        => element.SetValue(TemplateSelectorProperty, value);

    private static readonly ConditionalWeakTable<Panel, ViewSourcePipeline> Systems = new();

    private static void OnItemsSourceChanged(Panel panel, AvaloniaPropertyChangedEventArgs args)
        => Rebuild(panel);

    private static void OnTemplateSelectorChanged(Panel panel, AvaloniaPropertyChangedEventArgs args)
        => Rebuild(panel);

    private static void Rebuild(Panel panel)
    {
        Cleanup(panel);

        if (GetItemsSource(panel) is not { } source)
        {
            return;
        }

        var system = new ViewSourcePipeline(panel, GetTemplateSelector(panel), source);
        Systems.Add(panel, system);
        panel.DetachedFromVisualTree += OnPanelDetached;
    }

    private static void OnPanelDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Panel panel)
        {
            Cleanup(panel);
        }
    }

    private static void Cleanup(Panel panel)
    {
        // 换源/换模板都会重新 +=，不先 -= 会让面板根住旧的处理链。
        panel.DetachedFromVisualTree -= OnPanelDetached;

        if (Systems.TryGetValue(panel, out var system))
        {
            system.Dispose();
            Systems.Remove(panel);
        }
    }
}
