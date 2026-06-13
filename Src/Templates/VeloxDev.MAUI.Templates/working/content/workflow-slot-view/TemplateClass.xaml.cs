// VeloxDev customization: Add connector-specific visual state here; workflow interaction is configured in XAML.
using Microsoft.Maui.Controls.Shapes;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

public partial class TemplateClass : ContentView
{
    public static readonly BindableProperty SlotStateProperty = BindableProperty.Create(
        nameof(SlotState),
        typeof(SlotState),
        typeof(TemplateClass),
        SlotState.StandBy,
        propertyChanged: OnSlotStateChanged);

    public TemplateClass()
    {
        InitializeComponent();
        UpdateFill();
    }

    public SlotState SlotState
    {
        get => (SlotState)GetValue(SlotStateProperty);
        set => SetValue(SlotStateProperty, value);
    }

    private static void OnSlotStateChanged(BindableObject bindable, object? oldValue, object? newValue)
        => ((TemplateClass)bindable).UpdateFill();

    private void UpdateFill()
    {
        RootPath.Fill = new SolidColorBrush(SlotState switch
        {
            var value when value.HasFlag(SlotState.Sender) && value.HasFlag(SlotState.Receiver) => Colors.Violet,
            var value when value.HasFlag(SlotState.Sender) => Colors.Tomato,
            var value when value.HasFlag(SlotState.Receiver) => Colors.Lime,
            _ => Color.FromArgb("TemplateSlotColor"),
        });
    }
}
