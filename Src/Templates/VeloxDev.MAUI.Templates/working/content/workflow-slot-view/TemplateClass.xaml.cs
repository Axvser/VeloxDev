// VeloxDev customization: Customize the connector glyph here (drawn in SlotDrawable); workflow interaction is configured in XAML.
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
        IconView.Drawable = new SlotDrawable(this);
    }

    public SlotState SlotState
    {
        get => (SlotState)GetValue(SlotStateProperty);
        set => SetValue(SlotStateProperty, value);
    }

    private static void OnSlotStateChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is TemplateClass slotView)
        {
            slotView.IconView.Invalidate();
        }
    }

    private sealed class SlotDrawable(TemplateClass owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var color = ResolveSlotColor(owner.SlotState);
            var centerX = dirtyRect.Center.X;
            var centerY = dirtyRect.Center.Y;
            var radius = Math.Max(0, Math.Min(dirtyRect.Width, dirtyRect.Height) / 2f - 1f);

            canvas.FillColor = color;
            canvas.FillCircle(centerX, centerY, radius);
            canvas.StrokeColor = Color.FromArgb("TemplateSlotBorderColor");
            canvas.StrokeSize = 1.5f;
            canvas.DrawCircle(centerX, centerY, radius);
        }

        private static Color ResolveSlotColor(SlotState state)
            => state switch
            {
                var value when value.HasFlag(SlotState.Sender) && value.HasFlag(SlotState.Receiver) => Colors.Violet,
                var value when value.HasFlag(SlotState.Sender) => Colors.Tomato,
                var value when value.HasFlag(SlotState.Receiver) => Colors.Lime,
                _ => Color.FromArgb("TemplateSlotColor"),
            };
    }
}
