// VeloxDev customization: Relay view for the adapter's shared WorkflowLinkOverlay. Exposes the overlay's
// bindables (WorkflowTree + decorator scroll/content offsets + ruler + colors + stroke) on this ContentView
// and forwards them to the inner overlay via ElementName bindings in the XAML. Links are drawn by the ONE
// viewport-sized overlay, never one GraphicsView per link (the per-link strategy hit the Win2D texture cap
// at deep zoom and was removed from the Trimmed demos).
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

public partial class TemplateClass : ContentView
{
    public TemplateClass()
    {
        InitializeComponent();
    }

    public static readonly BindableProperty WorkflowTreeProperty = BindableProperty.Create(
        nameof(WorkflowTree), typeof(IWorkflowTreeViewModel), typeof(TemplateClass), null);
    public static readonly BindableProperty ScrollOffsetXProperty = BindableProperty.Create(
        nameof(ScrollOffsetX), typeof(double), typeof(TemplateClass), 0d);
    public static readonly BindableProperty ScrollOffsetYProperty = BindableProperty.Create(
        nameof(ScrollOffsetY), typeof(double), typeof(TemplateClass), 0d);
    public static readonly BindableProperty ContentOffsetXProperty = BindableProperty.Create(
        nameof(ContentOffsetX), typeof(double), typeof(TemplateClass), 0d);
    public static readonly BindableProperty ContentOffsetYProperty = BindableProperty.Create(
        nameof(ContentOffsetY), typeof(double), typeof(TemplateClass), 0d);
    public static readonly BindableProperty RulerThicknessProperty = BindableProperty.Create(
        nameof(RulerThickness), typeof(double), typeof(TemplateClass), 0d);
    public static readonly BindableProperty LinkLineColorProperty = BindableProperty.Create(
        nameof(LinkLineColor), typeof(Color), typeof(TemplateClass), Color.FromArgb("TemplateLinkColor"));
    public static readonly BindableProperty VirtualLineColorProperty = BindableProperty.Create(
        nameof(VirtualLineColor), typeof(Color), typeof(TemplateClass), Color.FromArgb("TemplateLinkColor"));
    public static readonly BindableProperty StrokeWidthProperty = BindableProperty.Create(
        nameof(StrokeWidth), typeof(double), typeof(TemplateClass), (double)TemplateLinkThickness);

    public IWorkflowTreeViewModel? WorkflowTree { get => (IWorkflowTreeViewModel?)GetValue(WorkflowTreeProperty); set => SetValue(WorkflowTreeProperty, value); }
    public double ScrollOffsetX { get => (double)GetValue(ScrollOffsetXProperty); set => SetValue(ScrollOffsetXProperty, value); }
    public double ScrollOffsetY { get => (double)GetValue(ScrollOffsetYProperty); set => SetValue(ScrollOffsetYProperty, value); }
    public double ContentOffsetX { get => (double)GetValue(ContentOffsetXProperty); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => (double)GetValue(ContentOffsetYProperty); set => SetValue(ContentOffsetYProperty, value); }
    public double RulerThickness { get => (double)GetValue(RulerThicknessProperty); set => SetValue(RulerThicknessProperty, value); }
    public Color LinkLineColor { get => (Color)GetValue(LinkLineColorProperty); set => SetValue(LinkLineColorProperty, value); }
    public Color VirtualLineColor { get => (Color)GetValue(VirtualLineColorProperty); set => SetValue(VirtualLineColorProperty, value); }
    public double StrokeWidth { get => (double)GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }
}
