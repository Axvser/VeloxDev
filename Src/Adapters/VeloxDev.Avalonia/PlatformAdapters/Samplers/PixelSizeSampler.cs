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
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var s1 = (PixelSize)(start ?? default(PixelSize));
            var s2 = (PixelSize)(end ?? s1);

            var deltaWidth = s2.Width - s1.Width;
            var deltaHeight = s2.Height - s1.Height;

            property.SetValue(target, new PixelSize(
                Math.Max(0, s1.Width + (int)(deltaWidth * t)),
                Math.Max(0, s1.Height + (int)(deltaHeight * t))
            ));
        }
    }
}