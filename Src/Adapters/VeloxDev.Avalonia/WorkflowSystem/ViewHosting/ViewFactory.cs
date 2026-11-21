using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using System;
using System.Collections.Generic;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Resolves an Avalonia view for one workflow item and caches the template by runtime type.</summary>
public sealed class ViewFactory : IViewFactory<IWorkflowComponent, Control>
{
    private readonly Panel _panel;
    private readonly IDataTemplate? _templateSelector;
    private readonly Dictionary<Type, IDataTemplate> _templates = [];

    /// <summary>Creates a factory that resolves templates from <paramref name="panel"/> and an optional selector.</summary>
    /// <param name="panel">The panel whose data templates and parent chain are searched.</param>
    /// <param name="templateSelector">The selector consulted first, or <see langword="null"/> to use the panel's templates only.</param>
    /// <exception cref="ArgumentNullException"><paramref name="panel"/> is <see langword="null"/>.</exception>
    public ViewFactory(Panel panel, IDataTemplate? templateSelector)
    {
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _templateSelector = templateSelector;
    }

    /// <inheritdoc />
    public bool WantsItem(IWorkflowComponent item) => true;

    /// <inheritdoc />
    public Control? CreateView(IWorkflowComponent item)
    {
        if (!FindTemplate(item, out var template))
        {
            return null;
        }

        return template?.Build(item) as Control;
    }

    private bool FindTemplate(object context, out IDataTemplate? template)
    {
        var contextType = context.GetType();
        if (_templates.TryGetValue(contextType, out template))
        {
            return true;
        }

        if (_templateSelector?.Match(context) == true)
        {
            template = _templateSelector;
            _templates[contextType] = template;
            return true;
        }

        if (TryFindTemplateIn(_panel.DataTemplates, context, out template))
        {
            _templates[contextType] = template!;
            return true;
        }

        var parent = _panel.Parent;
        while (parent is Control control)
        {
            if (TryFindTemplateIn(control.DataTemplates, context, out template))
            {
                _templates[contextType] = template!;
                return true;
            }

            parent = parent.Parent;
        }

        if (Application.Current?.DataTemplates is { Count: > 0 } appTemplates &&
            TryFindTemplateIn(appTemplates, context, out template))
        {
            _templates[contextType] = template!;
            return true;
        }

        template = null;
        return false;
    }

    private static bool TryFindTemplateIn(
        IList<IDataTemplate>? templates,
        object context,
        out IDataTemplate? template)
    {
        template = null;
        if (templates is null)
        {
            return false;
        }

        foreach (var candidate in templates)
        {
            if (candidate.Match(context))
            {
                template = candidate;
                return true;
            }
        }

        return false;
    }
}
