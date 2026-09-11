using Jalium.UI;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class RectSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            var r1 = (Rect)(start ?? new Rect(0, 0, 0, 0));
            var r2 = (Rect)(end ?? r1);
            // Convex lerp of two valid rects stays non-negative (Jalium's Rect ctor throws on negatives).
            // Width and height share one progress so an overshoot cannot skew the shape, and stop at
            // zero: a negative size is not representable. Position stays unbounded.
            var size = new BoundedProgress(t, 0d, double.PositiveInfinity);
            size.Add(r1.Width, r2.Width);
            size.Add(r1.Height, r2.Height);
            property.SetValue(target, new Rect(
                r1.X + (r2.X - r1.X) * t,
                r1.Y + (r2.Y - r1.Y) * t,
                r1.Width + (r2.Width - r1.Width) * size.Progress,
                r1.Height + (r2.Height - r1.Height) * size.Progress));
        }
    }
}
