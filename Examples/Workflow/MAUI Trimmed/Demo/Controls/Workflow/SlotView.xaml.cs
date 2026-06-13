using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

public partial class SlotView : ContentView
{
    public static readonly BindableProperty SlotStateProperty = BindableProperty.Create(
        nameof(SlotState),
        typeof(SlotState),
        typeof(SlotView),
        SlotState.StandBy,
        propertyChanged: OnSlotStateChanged);

    public SlotView()
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
        if (bindable is SlotView slotView)
        {
            slotView.IconView.Invalidate();
        }
    }

    private sealed class SlotDrawable(SlotView owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var color = ResolveSlotColor(owner.SlotState);
            var centerX = dirtyRect.Center.X;
            var centerY = dirtyRect.Center.Y;
            var radius = Math.Max(0, Math.Min(dirtyRect.Width, dirtyRect.Height) / 2f - 1f);

            canvas.FillColor = color;
            canvas.FillCircle(centerX, centerY, radius);
            canvas.StrokeColor = Colors.White;
            canvas.StrokeSize = 1.5f;
            canvas.DrawCircle(centerX, centerY, radius);
        }

        private static Color ResolveSlotColor(SlotState state)
            => state switch
            {
                var value when value.HasFlag(SlotState.Sender) && value.HasFlag(SlotState.Receiver) => Colors.Violet,
                var value when value.HasFlag(SlotState.Sender) => Colors.Tomato,
                var value when value.HasFlag(SlotState.Receiver) => Colors.Lime,
                _ => Colors.White,
            };
    }
}
