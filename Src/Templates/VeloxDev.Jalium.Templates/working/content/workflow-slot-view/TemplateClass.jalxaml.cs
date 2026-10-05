// VeloxDev customization: The connector's colour by state lives here; the gesture itself is the behavior in the markup.
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

public partial class TemplateClass : UserControl
{
    /// <summary>Which ends of a connection this port is currently playing.</summary>
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

    /// <summary>The port's current role; drives the fill.</summary>
    public SlotState SlotState
    {
        get => GetValue(SlotStateProperty) is SlotState state ? state : SlotState.StandBy;
        set => SetValue(SlotStateProperty, value);
    }

    private static void OnSlotStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TemplateClass)d).UpdateForeground();

    // 状态配色是这张模板的策略，改这里就是改端口长什么样。
    private void UpdateForeground()
    {
        Foreground = SlotState switch
        {
            var state when state.HasFlag(SlotState.Sender) && state.HasFlag(SlotState.Receiver)
                => new SolidColorBrush(Color.FromRgb(0xEE, 0x82, 0xEE)),
            var state when state.HasFlag(SlotState.Sender)
                => new SolidColorBrush(Color.FromRgb(0xFF, 0x63, 0x47)),
            var state when state.HasFlag(SlotState.Receiver)
                => new SolidColorBrush(Color.FromRgb(0x32, 0xCD, 0x32)),
            _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("TemplateSlotColor")!),
        };
    }
}
