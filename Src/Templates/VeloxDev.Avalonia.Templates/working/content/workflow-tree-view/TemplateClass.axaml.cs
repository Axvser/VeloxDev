// VeloxDev customization: Initialize tree-specific UI behavior here; provide an IWorkflowTreeViewModel as the data context.
using System;
using Avalonia.Controls;
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

    private void OnDataContextChanged(object? sender, EventArgs e)
        => WorkflowBehaviors.ViewPool.SetItemsSource(
            PART_Canvas,
            (DataContext as IWorkflowTreeViewModel)?.GetHelper().VisibleItems);
}
