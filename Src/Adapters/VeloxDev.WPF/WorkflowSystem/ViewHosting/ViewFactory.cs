using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Resolves a WPF view for one workflow item and caches the template by runtime type.</summary>
public sealed class ViewFactory : IViewFactory<IWorkflowViewModel, FrameworkElement>
{
    private readonly Panel _panel;
    private readonly DataTemplateSelector? _templateSelector;
    private readonly Dictionary<Type, DataTemplate> _templates = [];

    /// <summary>Creates a factory that resolves templates from <paramref name="panel"/> and an optional selector.</summary>
    /// <param name="panel">The panel whose visual-tree resources and Application resources are searched.</param>
    /// <param name="templateSelector">The selector consulted first, or <see langword="null"/> to use resources only.</param>
    /// <exception cref="ArgumentNullException"><paramref name="panel"/> is <see langword="null"/>.</exception>
    public ViewFactory(Panel panel, DataTemplateSelector? templateSelector)
    {
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _templateSelector = templateSelector;
    }

    /// <inheritdoc />
    public bool WantsItem(IWorkflowViewModel item) => true;

    /// <inheritdoc />
    public FrameworkElement? CreateView(IWorkflowViewModel item)
    {
        if (!FindTemplate(item, out var template) || template is null)
        {
            return null;
        }

        return template.LoadContent() as FrameworkElement;
    }

    // 三级回退：选择器 → 可视树 Resources → Application.Resources；命中后按运行时类型缓存。
    private bool FindTemplate(object context, out DataTemplate? template)
    {
        var contextType = context.GetType();
        if (_templates.TryGetValue(contextType, out template))
        {
            return true;
        }

        if (_templateSelector is not null &&
            _templateSelector.SelectTemplate(context, new ContentPresenter { Content = context })
                is { } selected)
        {
            _templates[contextType] = selected;
            template = selected;
            return true;
        }

        for (var current = (DependencyObject?)_panel; current is not null; current = ParentOf(current))
        {
            if (current is FrameworkElement element &&
                TryFindInResources(element.Resources, contextType, out template))
            {
                _templates[contextType] = template!;
                return true;
            }
        }

        if (Application.Current is { } application &&
            TryFindInResources(application.Resources, contextType, out template))
        {
            _templates[contextType] = template!;
            return true;
        }

        template = null;
        return false;
    }

    // 先走视觉树父链；不在视觉树里时退回逻辑父，与旧的查找落点一致。
    private static DependencyObject? ParentOf(DependencyObject node)
        => node is Visual
            ? VisualTreeHelper.GetParent(node) ?? (node as FrameworkElement)?.Parent
            : LogicalTreeHelper.GetParent(node);

    private static bool TryFindInResources(
        ResourceDictionary resources,
        Type contextType,
        out DataTemplate? template)
    {
        foreach (var key in resources.Keys)
        {
            if (resources[key] is DataTemplate candidate &&
                candidate.DataType is Type dataType &&
                dataType == contextType)
            {
                template = candidate;
                return true;
            }
        }

        template = null;
        return false;
    }
}
