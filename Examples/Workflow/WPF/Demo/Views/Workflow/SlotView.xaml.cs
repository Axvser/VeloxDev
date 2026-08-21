using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>Which way the chip pin points: input slots point left (In), output slots point right (Out).</summary>
public enum SlotPinDirection
{
    In,
    Out,
}

public partial class SlotView : UserControl
{
    public static readonly DependencyProperty SlotStateProperty = DependencyProperty.Register(
        nameof(SlotState),
        typeof(SlotState),
        typeof(SlotView),
        new PropertyMetadata(SlotState.StandBy, OnSlotStateChanged));

    public static readonly DependencyProperty PinDirectionProperty = DependencyProperty.Register(
        nameof(PinDirection),
        typeof(SlotPinDirection),
        typeof(SlotView),
        new PropertyMetadata(SlotPinDirection.In, OnPinDirectionChanged));

    public SlotView()
    {
        InitializeComponent();
        UpdateForeground();
        ApplyPinDirection();
        SizeChanged += (_, _) => ApplyPinDirection();
    }

    /// <summary>Input slots point left, output slots point right (defaults to <see cref="SlotPinDirection.In"/>).</summary>
    public SlotPinDirection PinDirection
    {
        get => (SlotPinDirection)GetValue(PinDirectionProperty);
        set => SetValue(PinDirectionProperty, value);
    }

    public SlotState SlotState
    {
        get => (SlotState)GetValue(SlotStateProperty);
        set => SetValue(SlotStateProperty, value);
    }

    private static void OnSlotStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SlotView)d).UpdateForeground();

    private void UpdateForeground()
    {
        Foreground = SlotState switch
        {
            var state when state.HasFlag(SlotState.Sender) && state.HasFlag(SlotState.Receiver) => Brushes.Violet,
            var state when state.HasFlag(SlotState.Sender) => Brushes.Tomato,
            var state when state.HasFlag(SlotState.Receiver) => Brushes.Lime,
            _ => Brushes.White,
        };
    }

    private static void OnPinDirectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SlotView)d).ApplyPinDirection();

    /// <summary>
    /// Flips the pin horizontally around the slot's actual center so it points left for inputs / right for
    /// outputs. The vertical alignment with the connection line is done purely in the pin geometry
    /// (SlotView.xaml shifts it 2px up) — no adapter involvement.
    /// </summary>
    private void ApplyPinDirection()
    {
        if (PinRoot is not null)
            PinRoot.RenderTransform = new ScaleTransform(
                PinDirection == SlotPinDirection.In ? -1 : 1, 1,
                ActualWidth / 2, ActualHeight / 2);
    }

    private void OnPointerPressed(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not IWorkflowSlotViewModel context) return;

        context.SendConnectionCommand.Execute(null);

        e.Handled = true;
    }

    private void OnPointerReleased(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not IWorkflowSlotViewModel context) return;

        context.ReceiveConnectionCommand.Execute(null);

        e.Handled = true;
    }
}