using Avalonia.Controls;
using Avalonia.Controls.Selection;
using VeloxDev.WorkflowSystem.CSharp;

namespace Demo;

public partial class CSharpObjectView : UserControl
{
    public CSharpObjectView()
    {
        InitializeComponent();
    }

    private void OnMethodSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (DataContext is CSharpObject node
            && sender is ComboBox { SelectedItem: MethodMember method })
        {
            node.SelectedMethod = method.Signature;
        }
    }
}
