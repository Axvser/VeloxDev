using VeloxDev.WorkflowSystem.CSharp;

namespace Demo.Controls;

public partial class CSharpObjectView : ContentView
{
    public CSharpObjectView()
    {
        InitializeComponent();
    }

    private void OnMethodSelectionChanged(object? sender, EventArgs e)
    {
        if (BindingContext is CSharpObject node
            && sender is Picker { SelectedItem: MethodMember method })
        {
            node.SelectedMethod = method.Signature;
        }
    }
}
