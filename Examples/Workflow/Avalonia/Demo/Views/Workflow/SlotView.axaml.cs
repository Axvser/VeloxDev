using Avalonia;
using Avalonia.Controls;
using VeloxDev.WorkflowSystem;

namespace Demo;

public partial class SlotView : UserControl
{
    public SlotView()
    {
        InitializeComponent();
    }

    public static readonly StyledProperty<SlotState> SlotStateProperty =
        AvaloniaProperty.Register<SlotView, SlotState>(nameof(SlotState));

    public SlotState SlotState
    {
        get => this.GetValue(SlotStateProperty);
        set => SetValue(SlotStateProperty, value);
    }
}