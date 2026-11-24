using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using VeloxDev.WorkflowSystem;

namespace Demo;

/// <summary>
/// Selects a view template for each of the three workflow component interfaces.
/// Add this selector to resources, assign NodeTemplate, SlotTemplate,
/// LinkTemplate, then pass it to behaviors:ViewPool.TemplateSelector.
/// </summary>
public sealed class CustomTemplateSelector : IDataTemplate
{
    public IDataTemplate? NodeTemplate { get; set; }

    public IDataTemplate? SlotTemplate { get; set; }

    public IDataTemplate? LinkTemplate { get; set; }


    public Control Build(object? parameter)
        => SelectTemplate(parameter).Build(parameter)
            ?? throw new InvalidOperationException(
                $"The selected template could not build {parameter?.GetType().FullName}.");

    public bool Match(object? data)
        => data is INodeComponent
            or ISlotComponent
            or ILinkComponent;

    private IDataTemplate SelectTemplate(object? item)
        => item switch
        {
            ILinkComponent => LinkTemplate
                ?? throw new InvalidOperationException("LinkTemplate is not set."),
            ISlotComponent => SlotTemplate
                ?? throw new InvalidOperationException("SlotTemplate is not set."),
            INodeComponent => NodeTemplate
                ?? throw new InvalidOperationException("NodeTemplate is not set."),
            _ => throw new InvalidOperationException($"Unsupported workflow item: {item?.GetType().FullName}")
        };
}
