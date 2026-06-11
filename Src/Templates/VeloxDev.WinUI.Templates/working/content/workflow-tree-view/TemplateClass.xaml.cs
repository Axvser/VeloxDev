// VeloxDev customization: Initialize tree-specific UI behavior here; provide an IWorkflowTreeViewModel as the data context.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VeloxDev.WorkflowSystem;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

public sealed partial class TemplateClass : UserControl
{
    public TemplateClass()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        => WorkflowBehaviors.ViewPool.SetItemsSource(
            PART_Canvas,
            (args.NewValue as IWorkflowTreeViewModel)?.GetHelper().VisibleItems);
}
