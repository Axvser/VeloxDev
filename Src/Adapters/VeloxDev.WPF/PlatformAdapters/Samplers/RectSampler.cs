using System.Windows;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class RectSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            var rect1 = (Rect)(start ?? new Rect(0, 0, 0, 0));
            var rect2 = (Rect)(end ?? rect1);

            // Width and height share one progress so an overshoot cannot skew the shape, and stop at zero: a
            // negative size is not representable. Position stays unbounded.
            var size = new BoundedProgress(t, 0d, double.PositiveInfinity);
            size.Add(rect1.Width, rect2.Width);
            size.Add(rect1.Height, rect2.Height);

            property.SetValue(target, new Rect(
                rect1.X + t * (rect2.X - rect1.X),
                rect1.Y + t * (rect2.Y - rect1.Y),
                rect1.Width + size.Progress * (rect2.Width - rect1.Width),
                rect1.Height + size.Progress * (rect2.Height - rect1.Height)));
        }
    }
}
