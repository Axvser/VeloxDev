using VeloxDev.Core.Interfaces.WorkflowSystem;

namespace Demo.Views.Workflow;

public partial class SlotView : ContentView
{
    public SlotView()
    {
        InitializeComponent();

        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += OnTapped;
        GestureRecognizers.Add(tapGesture);
    }

    private void OnTapped(object? sender, EventArgs e)
    {
        var context = this.BindingContext as IWorkflowSlotViewModel;
        context?.ApplyConnectionCommand?.Execute(null);
    }
}