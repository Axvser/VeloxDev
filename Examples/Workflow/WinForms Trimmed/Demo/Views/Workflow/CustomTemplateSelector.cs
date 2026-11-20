// VeloxDev customization: which view each pooled item kind gets.
using VeloxDev.WinForms.Xaml;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

public sealed class CustomTemplateSelector : TemplateSelector
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
