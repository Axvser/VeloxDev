// VeloxDev customization: Initialize tree-specific UI behavior here; provide an IWorkflowTreeViewModel as the data context.
using System.Windows;
using System.Windows.Controls;
using VeloxDev.WorkflowSystem;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

public partial class TemplateClass : UserControl
{
    public TemplateClass()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        => WorkflowBehaviors.ViewPool.SetItemsSource(
            PART_Canvas,
            (e.NewValue as IWorkflowTreeViewModel)?.GetHelper().VisibleItems);
}
