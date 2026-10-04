// VeloxDev customization: Relay view for the adapter's shared WorkflowLinkOverlay. Exposes the overlay's
// bindables (WorkflowTree + InteractionSource + decorator scroll/content offsets + ruler + colors + stroke)
// on this ContentView and forwards them to the inner overlay via ElementName bindings in the XAML. Links are
// drawn by the ONE viewport-sized overlay, never one GraphicsView per link (the per-link strategy hit the
// Win2D texture cap at deep zoom and was removed from the Trimmed demos).
//
// InteractionSource is what makes the links interactive: this layer is input-transparent and never receives
// the pointer itself, so the overlay hooks the element you name here instead. The tree-view template points it
// at the surface root; leave it unset and the links draw but cannot be hovered, selected or deleted.
using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

public partial class LinkView : ContentView
{
    public LinkView()
    {
        InitializeComponent();
    }

    public static readonly BindableProperty WorkflowTreeProperty = BindableProperty.Create(
        nameof(WorkflowTree), typeof(IWorkflowTreeViewModel), typeof(LinkView), null);
    public static readonly BindableProperty InteractionSourceProperty = BindableProperty.Create(
        nameof(InteractionSource), typeof(View), typeof(LinkView), null);
    public static readonly BindableProperty ScrollOffsetXProperty = BindableProperty.Create(
        nameof(ScrollOffsetX), typeof(double), typeof(LinkView), 0d);
    public static readonly BindableProperty ScrollOffsetYProperty = BindableProperty.Create(
        nameof(ScrollOffsetY), typeof(double), typeof(LinkView), 0d);
    public static readonly BindableProperty ContentOffsetXProperty = BindableProperty.Create(
        nameof(ContentOffsetX), typeof(double), typeof(LinkView), 0d);
    public static readonly BindableProperty ContentOffsetYProperty = BindableProperty.Create(
        nameof(ContentOffsetY), typeof(double), typeof(LinkView), 0d);
    public static readonly BindableProperty RulerThicknessProperty = BindableProperty.Create(
        nameof(RulerThickness), typeof(double), typeof(LinkView), 0d);
    public static readonly BindableProperty LinkLineColorProperty = BindableProperty.Create(
        nameof(LinkLineColor), typeof(Color), typeof(LinkView), Color.FromArgb("#DDFFFFFF"));
    public static readonly BindableProperty VirtualLineColorProperty = BindableProperty.Create(
        nameof(VirtualLineColor), typeof(Color), typeof(LinkView), Color.FromArgb("#DDFFFFFF"));
    public static readonly BindableProperty StrokeWidthProperty = BindableProperty.Create(
        nameof(StrokeWidth), typeof(double), typeof(LinkView), (double)2);

    public IWorkflowTreeViewModel? WorkflowTree { get => (IWorkflowTreeViewModel?)GetValue(WorkflowTreeProperty); set => SetValue(WorkflowTreeProperty, value); }
    public View? InteractionSource { get => (View?)GetValue(InteractionSourceProperty); set => SetValue(InteractionSourceProperty, value); }
    public double ScrollOffsetX { get => (double)GetValue(ScrollOffsetXProperty); set => SetValue(ScrollOffsetXProperty, value); }
    public double ScrollOffsetY { get => (double)GetValue(ScrollOffsetYProperty); set => SetValue(ScrollOffsetYProperty, value); }
    public double ContentOffsetX { get => (double)GetValue(ContentOffsetXProperty); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => (double)GetValue(ContentOffsetYProperty); set => SetValue(ContentOffsetYProperty, value); }
    public double RulerThickness { get => (double)GetValue(RulerThicknessProperty); set => SetValue(RulerThicknessProperty, value); }
    public Color LinkLineColor { get => (Color)GetValue(LinkLineColorProperty); set => SetValue(LinkLineColorProperty, value); }
    public Color VirtualLineColor { get => (Color)GetValue(VirtualLineColorProperty); set => SetValue(VirtualLineColorProperty, value); }
    public double StrokeWidth { get => (double)GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }

}
