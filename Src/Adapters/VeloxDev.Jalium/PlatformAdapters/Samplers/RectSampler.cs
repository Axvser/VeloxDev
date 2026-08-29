using Jalium.UI;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class RectSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var r1 = (Rect)(start ?? new Rect(0, 0, 0, 0));
            var r2 = (Rect)(end ?? r1);
            // Convex lerp of two valid rects stays non-negative (Jalium's Rect ctor throws on negatives).
            property.SetValue(target, new Rect(
                r1.X + (r2.X - r1.X) * t,
                r1.Y + (r2.Y - r1.Y) * t,
                r1.Width + (r2.Width - r1.Width) * t,
                r1.Height + (r2.Height - r1.Height) * t));
        }
    }
}
