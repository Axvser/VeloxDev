// VeloxDev customization: Add connector-specific interaction here only when the platform behavior does not already cover it.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

public partial class TemplateClass : UserControl
{
    public static readonly DependencyProperty SlotStateProperty = DependencyProperty.Register(
        nameof(SlotState),
        typeof(SlotState),
        typeof(TemplateClass),
        new PropertyMetadata(SlotState.StandBy, OnSlotStateChanged));

    public TemplateClass()
    {
        InitializeComponent();
        UpdateForeground();
    }

    public SlotState SlotState
    {
        get => (SlotState)GetValue(SlotStateProperty);
        set => SetValue(SlotStateProperty, value);
    }

    private static void OnSlotStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TemplateClass)d).UpdateForeground();

    private void UpdateForeground()
    {
        Foreground = SlotState switch
        {
            var state when state.HasFlag(SlotState.Sender) && state.HasFlag(SlotState.Receiver) => Brushes.Violet,
            var state when state.HasFlag(SlotState.Sender) => Brushes.Tomato,
            var state when state.HasFlag(SlotState.Receiver) => Brushes.Lime,
            _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("TemplateSlotColor")),
        };
    }

}
