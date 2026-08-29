namespace VeloxDev.Adapters.NativeSamplers
{
    public class RectSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            // Handle null values by providing defaults.
            var r1 = (Rect)(start ?? new Rect(0, 0, 0, 0));
            var r2 = (Rect)(end ?? new Rect(0, 0, 0, 0));

            var deltaX = r2.X - r1.X;
            var deltaY = r2.Y - r1.Y;
            var deltaWidth = r2.Width - r1.Width;
            var deltaHeight = r2.Height - r1.Height;

            property.SetValue(target, new Rect(
                r1.X + deltaX * t,
                r1.Y + deltaY * t,
                r1.Width + deltaWidth * t,
                r1.Height + deltaHeight * t
            ));
        }
    }
}
