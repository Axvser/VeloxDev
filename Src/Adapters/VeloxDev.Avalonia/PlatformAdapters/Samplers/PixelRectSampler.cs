using Avalonia;
using System;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class PixelRectSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var r1 = (PixelRect)(start ?? default(PixelRect));
            var r2 = (PixelRect)(end ?? r1);

            var deltaX = r2.X - r1.X;
            var deltaY = r2.Y - r1.Y;
            var deltaWidth = r2.Width - r1.Width;
            var deltaHeight = r2.Height - r1.Height;

            property.SetValue(target, new PixelRect(
                r1.X + (int)(deltaX * t),
                r1.Y + (int)(deltaY * t),
                Math.Max(0, r1.Width + (int)(deltaWidth * t)),
                Math.Max(0, r1.Height + (int)(deltaHeight * t))
            ));
        }
    }
}