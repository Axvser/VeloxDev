using Demo.ViewModels;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.Views.Workflow;

public partial class NodeView : ContentView
{
    private bool _isDragging;
    private Point _lastPosition;

    public NodeView()
    {
        InitializeComponent();

        var panGesture = new PanGestureRecognizer();
        panGesture.PanUpdated += OnPanUpdated;
        GestureRecognizers.Add(panGesture);
    }

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _isDragging = true;
                _lastPosition = new Point(this.TranslationX, this.TranslationY);
                break;

            case GestureStatus.Running:
                if (_isDragging && this.BindingContext is NodeViewModel viewModel)
                {
                    var delta = new Offset(e.TotalX, e.TotalY);
                    viewModel.MoveCommand?.Execute(delta);
                }
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _isDragging = false;
                break;
        }
    }
}