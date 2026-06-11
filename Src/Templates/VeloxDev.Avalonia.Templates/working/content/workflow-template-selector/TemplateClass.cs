using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

/// <summary>
/// Selects a view template for each of the four workflow component interfaces.
/// Add this selector to resources, assign NodeTemplate, SlotTemplate,
/// LinkTemplate, and TreeTemplate, then pass it to behaviors:ViewPool.TemplateSelector.
/// </summary>
public sealed class TemplateClass : IDataTemplate
{
    public IDataTemplate? NodeTemplate { get; set; }

    public IDataTemplate? SlotTemplate { get; set; }

    public IDataTemplate? LinkTemplate { get; set; }

    public IDataTemplate? TreeTemplate { get; set; }

    public Control Build(object? parameter)
        => SelectTemplate(parameter).Build(parameter)
            ?? throw new InvalidOperationException(
                $"The selected template could not build {parameter?.GetType().FullName}.");

    public bool Match(object? data)
        => data is IWorkflowNodeViewModel
            or IWorkflowSlotViewModel
            or IWorkflowLinkViewModel
            or IWorkflowTreeViewModel;

    private IDataTemplate SelectTemplate(object? item)
        => item switch
        {
            IWorkflowLinkViewModel => LinkTemplate
                ?? throw new InvalidOperationException("LinkTemplate is not set."),
            IWorkflowSlotViewModel => SlotTemplate
                ?? throw new InvalidOperationException("SlotTemplate is not set."),
            IWorkflowNodeViewModel => NodeTemplate
                ?? throw new InvalidOperationException("NodeTemplate is not set."),
            IWorkflowTreeViewModel => TreeTemplate
                ?? throw new InvalidOperationException("TreeTemplate is not set."),
            _ => throw new InvalidOperationException($"Unsupported workflow item: {item?.GetType().FullName}")
        };
}
