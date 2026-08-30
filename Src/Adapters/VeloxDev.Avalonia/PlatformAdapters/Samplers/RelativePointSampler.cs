using Avalonia;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class RelativePointSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var p1 = (RelativePoint)(start ?? RelativePoint.TopLeft);
            var p2 = (RelativePoint)(end ?? p1);

            // If units differ, interpolation is impossible; hold the start value.
            if (p1.Unit != p2.Unit)
            {
                property.SetValue(target, p1);
                return;
            }

            var deltaX = p2.Point.X - p1.Point.X;
            var deltaY = p2.Point.Y - p1.Point.Y;

            property.SetValue(target, new RelativePoint(
                p1.Point.X + deltaX * t,
                p1.Point.Y + deltaY * t,
                p1.Unit
            ));
        }
    }
}