using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>
/// Resolves a WinUI view for one workflow item from a <see cref="DataTemplateSelector"/> or a
/// resource key, and caches the template by runtime type.
/// </summary>
public sealed class ViewFactory : IViewFactory<IWorkflowComponent, FrameworkElement>
{
    private readonly Panel _panel;
    private readonly DataTemplateSelector? _templateSelector;
    private readonly Dictionary<Type, DataTemplate> _templates = [];

    /// <summary>Creates a factory that searches <paramref name="panel"/>, its ancestors and the application resources, consulting <paramref name="templateSelector"/> first.</summary>
    /// <param name="panel">The panel whose resource chain is searched.</param>
    /// <param name="templateSelector">The selector consulted first, or <see langword="null"/> to use resource keys only.</param>
    /// <exception cref="ArgumentNullException"><paramref name="panel"/> is <see langword="null"/>.</exception>
    public ViewFactory(Panel panel, DataTemplateSelector? templateSelector)
    {
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _templateSelector = templateSelector;
    }

    /// <summary>Gets whether this factory materializes the item; WinUI materializes every item, links included.</summary>
    /// <param name="item">The item to test.</param>
    public bool WantsItem(IWorkflowComponent item) => true;

    /// <summary>Creates a view for <paramref name="item"/>, or returns <see langword="null"/> when no template matches its runtime type.</summary>
    /// <param name="item">The item to materialize.</param>
    /// <returns>The template's root <see cref="FrameworkElement"/>, or <see langword="null"/>.</returns>
    public FrameworkElement? CreateView(IWorkflowComponent item)
    {
        if (!FindTemplate(item, out var template) || template is null)
        {
            return null;
        }

        return template.LoadContent() as FrameworkElement;
    }

    private bool FindTemplate(object context, out DataTemplate? template)
    {
        var contextType = context.GetType();
        if (_templates.TryGetValue(contextType, out template))
        {
            return true;
        }

        if (_templateSelector is not null)
        {
            var presenter = new ContentPresenter { Content = context };
            if (_templateSelector.SelectTemplate(context, presenter) is DataTemplate selected)
            {
                _templates[contextType] = selected;
                template = selected;
                return true;
            }
        }

        for (FrameworkElement? current = _panel; current is not null; current = current.Parent as FrameworkElement)
        {
            if (TryFindTemplateIn(current.Resources, contextType, out template))
            {
                return true;
            }
        }

        if (Application.Current is not null &&
            TryFindTemplateIn(Application.Current.Resources, contextType, out template))
        {
            return true;
        }

        template = null;
        return false;
    }

    private bool TryFindTemplateIn(ResourceDictionary resources, Type contextType, out DataTemplate? template)
    {
        foreach (var entry in resources)
        {
            if (entry.Value is DataTemplate dataTemplate && IsMatchingTemplateKey(entry.Key, contextType))
            {
                _templates[contextType] = dataTemplate;
                template = dataTemplate;
                return true;
            }
        }

        template = null;
        return false;
    }

    private static bool IsMatchingTemplateKey(object key, Type contextType)
        => key switch
        {
            string name => string.Equals(name, contextType.Name, StringComparison.Ordinal)
                || string.Equals(name, contextType.FullName, StringComparison.Ordinal)
                || string.Equals(name, GetTemplateKeyName(contextType), StringComparison.Ordinal),
            Type typeKey => typeKey == contextType,
            _ => false,
        };

    private static string GetTemplateKeyName(Type contextType)
        => contextType.Name.EndsWith("ViewModel", StringComparison.Ordinal)
            ? contextType.Name[..^"ViewModel".Length] + "Template"
            : contextType.Name + "Template";
}
