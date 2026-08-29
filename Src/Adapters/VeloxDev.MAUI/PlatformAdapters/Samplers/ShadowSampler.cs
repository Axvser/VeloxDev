namespace VeloxDev.Adapters.NativeSamplers
{
    public class ShadowSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            // Create default shadow values.
            var defaultShadow = new Shadow
            {
                Offset = new Point(0, 0),
                Radius = 0,
                Opacity = 0,
                Brush = new SolidColorBrush(Colors.Transparent)
            };

            var s1 = (Shadow)(start ?? defaultShadow);
            var s2 = (Shadow)(end ?? defaultShadow);

            // Handle the case where Brush is null.
            var brush1 = s1.Brush ?? new SolidColorBrush(Colors.Transparent);
            var brush2 = s2.Brush ?? new SolidColorBrush(Colors.Transparent);

            var deltaOffsetX = s2.Offset.X - s1.Offset.X;
            var deltaOffsetY = s2.Offset.Y - s1.Offset.Y;
            var deltaRadius = s2.Radius - s1.Radius;
            var deltaOpacity = s2.Opacity - s1.Opacity;

            property.SetValue(target, new Shadow
            {
                Offset = new Point(
                    s1.Offset.X + deltaOffsetX * (float)t,
                    s1.Offset.Y + deltaOffsetY * (float)t
                ),
                Radius = s1.Radius + deltaRadius * (float)t,
                Opacity = Math.Max(0, Math.Min(1, s1.Opacity + deltaOpacity * (float)t)),
                Brush = t >= 0.5 ? brush2 : brush1 // Simple transition handling.
            });
        }
    }
}
