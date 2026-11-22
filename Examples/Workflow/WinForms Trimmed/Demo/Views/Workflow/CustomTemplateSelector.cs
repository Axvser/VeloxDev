// VeloxDev customization: assign the templates your surface should create; a missing template falls through.
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>Chooses the visual element each workflow component is shown with.</summary>
public sealed class CustomTemplateSelector : VisualTemplateSelector
{
    /// <summary>The template used for a node, or <see langword="null"/>.</summary>
    public VisualDataTemplate? NodeTemplate { get; set; }

    /// <summary>The template used for a slot, or <see langword="null"/>.</summary>
    public VisualDataTemplate? SlotTemplate { get; set; }

    /// <summary>The template used for a link, or <see langword="null"/>.</summary>
    public VisualDataTemplate? LinkTemplate { get; set; }

    /// <summary>The template used for a tree, or <see langword="null"/>.</summary>
    public VisualDataTemplate? TreeTemplate { get; set; }

    /// <inheritdoc />
    public override VisualElement? SelectTemplate(object? item) => item switch
    {
        ILinkComponent => LinkTemplate?.Create(),
        ISlotComponent => SlotTemplate?.Create(),
        INodeComponent => NodeTemplate?.Create(),
        ITreeComponent => TreeTemplate?.Create(),
        _ => null,
    };
}
