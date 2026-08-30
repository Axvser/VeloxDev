using Avalonia;
using System;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class RelativeRectSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var r1 = (RelativeRect)(start ?? new RelativeRect());
            var r2 = (RelativeRect)(end ?? r1);

            // If units differ, interpolation is impossible; hold the start value.
            if (r1.Unit != r2.Unit)
            {
                property.SetValue(target, r1);
                return;
            }

            var deltaX = r2.Rect.X - r1.Rect.X;
            var deltaY = r2.Rect.Y - r1.Rect.Y;
            var deltaWidth = r2.Rect.Width - r1.Rect.Width;
            var deltaHeight = r2.Rect.Height - r1.Rect.Height;

            property.SetValue(target, new RelativeRect(
                r1.Rect.X + deltaX * t,
                r1.Rect.Y + deltaY * t,
                Math.Max(0, r1.Rect.Width + deltaWidth * t),
                Math.Max(0, r1.Rect.Height + deltaHeight * t),
                r1.Unit
            ));
        }
    }
}