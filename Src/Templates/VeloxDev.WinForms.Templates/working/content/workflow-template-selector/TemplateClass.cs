// VeloxDev customization: assign the DataTemplates your tree declares; add the slot/tree templates if the host pools them.
using VeloxDev.WinForms.Xaml;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

public sealed class TemplateClass : TemplateSelector
{
    public DataTemplate? NodeTemplate { get; set; }

    public DataTemplate? SlotTemplate { get; set; }

    public DataTemplate? LinkTemplate { get; set; }

    public DataTemplate? TreeTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item) => item switch
    {
        IWorkflowLinkViewModel => LinkTemplate,
        IWorkflowSlotViewModel => SlotTemplate,
        IWorkflowNodeViewModel => NodeTemplate,
        IWorkflowTreeViewModel => TreeTemplate,
        _ => null,
    };
}
