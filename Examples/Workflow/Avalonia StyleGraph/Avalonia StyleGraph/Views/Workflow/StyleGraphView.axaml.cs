using Avalonia.Controls;
using Avalonia.Input;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace Avalonia_StyleGraph;

public partial class StyleGraphView : UserControl
{
    public StyleGraphView()
    {
        InitializeComponent();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if(DataContext is IWorkflowTreeViewModel tree)
        {
            var point = e.GetPosition(this);
            tree.SetPointerCommand.Execute(new Anchor(point.X, point.Y, 0));
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is IWorkflowTreeViewModel tree)
        {
            tree.ResetVirtualLinkCommand.Execute(null);
        }
    }
}