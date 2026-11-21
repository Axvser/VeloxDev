using System;
using System.Collections.Generic;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Resolves a Jalium view for one workflow item and caches the template by runtime type.</summary>
/// <remarks>
/// Lookup order is the selector, then each visual-tree ancestor's resources by <c>DataType</c>, then
/// <see cref="Application"/> resources. A type whose lookup fails is reported as unsupported by returning
/// <see langword="null"/>; the pool remembers that answer for the type.
/// </remarks>
public sealed class ViewFactory : IViewFactory<IWorkflowComponent, FrameworkElement>
{
    private readonly Panel _host;
    private readonly DataTemplateSelector? _selector;
    private readonly Dictionary<Type, DataTemplate> _templates = [];

    /// <summary>Creates a factory that resolves templates from <paramref name="host"/> and an optional selector.</summary>
    /// <param name="host">The panel whose resources and visual-tree ancestors are searched.</param>
    /// <param name="templateSelector">The selector consulted first, or <see langword="null"/> to use resources only.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <see langword="null"/>.</exception>
    public ViewFactory(Panel host, DataTemplateSelector? templateSelector)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _selector = templateSelector;
    }

    /// <inheritdoc />
    public bool WantsItem(IWorkflowComponent item) => true;

    /// <inheritdoc />
    public FrameworkElement? CreateView(IWorkflowComponent item)
        => FindTemplate(item)?.LoadContent() as FrameworkElement;

    // 解析顺序与旧管理器一致：选择器 → 沿视觉树上溯的 Resources → Application.Resources。
    // 按 item 的运行时类型缓存，避免每个条目重走一遍资源树；失败不缓存，交给池按类型记一次。
    private DataTemplate? FindTemplate(object item)
    {
        var itemType = item.GetType();
        if (_templates.TryGetValue(itemType, out var cached))
        {
            return cached;
        }

        if (_selector is not null)
        {
            // 临时 ContentPresenter 只作选择器的容器参数，不参与视觉树。
            var container = new ContentPresenter { Content = item };
            if (_selector.SelectTemplate(item, container) is DataTemplate selected)
            {
                _templates[itemType] = selected;
                return selected;
            }
        }

        DependencyObject? current = _host;
        while (current is not null)
        {
            if (current is FrameworkElement element)
            {
                foreach (var key in element.Resources.Keys)
                {
                    if (element.Resources[key] is DataTemplate template
                        && template.DataType is Type dataType
                        && dataType == itemType)
                    {
                        _templates[itemType] = template;
                        return template;
                    }
                }
            }

            current = VisualTreeHelper.GetParent(current)
                ?? (current is FrameworkElement parent ? parent.Parent : null);
        }

        if (Application.Current is { } application)
        {
            foreach (var key in application.Resources.Keys)
            {
                if (application.Resources[key] is DataTemplate template
                    && template.DataType is Type dataType
                    && dataType == itemType)
                {
                    _templates[itemType] = template;
                    return template;
                }
            }
        }

        return null;
    }
}
