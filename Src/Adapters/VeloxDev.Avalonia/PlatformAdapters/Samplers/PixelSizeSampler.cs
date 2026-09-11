using Avalonia;
using System;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class PixelSizeSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            var s1 = (PixelSize)(start ?? default(PixelSize));
            var s2 = (PixelSize)(end ?? s1);

            var deltaWidth = s2.Width - s1.Width;
            var deltaHeight = s2.Height - s1.Height;

            // Width and height share one progress so an overshoot cannot skew the shape, and stop at
            // zero: a negative size is not representable.
            var size = new BoundedProgress(t, 0d, double.PositiveInfinity);
            size.Add(s1.Width, s2.Width);
            size.Add(s1.Height, s2.Height);
            property.SetValue(target, new PixelSize(
                Math.Max(0, s1.Width + (int)(deltaWidth * size.Progress)),
                Math.Max(0, s1.Height + (int)(deltaHeight * size.Progress))
            ));
        }
    }
}