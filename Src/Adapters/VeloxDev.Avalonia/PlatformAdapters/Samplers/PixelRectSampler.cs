using Avalonia;
using System;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class PixelRectSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            var r1 = (PixelRect)(start ?? default(PixelRect));
            var r2 = (PixelRect)(end ?? r1);

            var deltaX = r2.X - r1.X;
            var deltaY = r2.Y - r1.Y;
            var deltaWidth = r2.Width - r1.Width;
            var deltaHeight = r2.Height - r1.Height;

            // Width and height share one progress so an overshoot cannot skew the shape, and stop at
            // zero: a negative size is not representable. Position stays unbounded.
            var size = new BoundedProgress(t, 0d, double.PositiveInfinity);
            size.Add(r1.Width, r2.Width);
            size.Add(r1.Height, r2.Height);
            property.SetValue(target, new PixelRect(
                r1.X + (int)(deltaX * t),
                r1.Y + (int)(deltaY * t),
                Math.Max(0, r1.Width + (int)(deltaWidth * size.Progress)),
                Math.Max(0, r1.Height + (int)(deltaHeight * size.Progress))
            ));
        }
    }
}