using System.Drawing;

namespace VeloxDev.TransitionSystem.NativeSamplers
{
    public class RectangleSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            var r1 = (Rectangle)(start ?? default(Rectangle));
            var r2 = (Rectangle)(end ?? r1);
            var deltaX = r2.X - r1.X;
            var deltaY = r2.Y - r1.Y;
            var deltaWidth = r2.Width - r1.Width;
            var deltaHeight = r2.Height - r1.Height;

            // Width and height share one progress so an overshoot cannot skew the shape, and stop at
            // zero: a negative size is not representable. Position stays unbounded.
            var size = new BoundedProgress(t, 0d, double.PositiveInfinity);
            size.Add(r1.Width, r2.Width);
            size.Add(r1.Height, r2.Height);
            property.SetValue(target, new Rectangle(
                r1.X + (int)Math.Round(deltaX * t),
                r1.Y + (int)Math.Round(deltaY * t),
                r1.Width + (int)Math.Round(deltaWidth * size.Progress),
                r1.Height + (int)Math.Round(deltaHeight * size.Progress)
            ));
        }
    }
}
