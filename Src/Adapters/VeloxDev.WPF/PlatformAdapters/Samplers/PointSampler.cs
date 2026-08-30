using System.Windows;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class PointSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var point1 = (Point)(start ?? new Point(0, 0));
            var point2 = (Point)(end ?? point1);
            property.SetValue(target, new Point(
                point1.X + t * (point2.X - point1.X),
                point1.Y + t * (point2.Y - point1.Y)));
        }
    }
}
