using System.Drawing;

namespace VeloxDev.TransitionSystem.NativeSamplers
{
    public class PointSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var p1 = (Point)(start ?? default(Point));
            var p2 = (Point)(end ?? p1);
            var deltaX = p2.X - p1.X;
            var deltaY = p2.Y - p1.Y;

            property.SetValue(target, new Point(
                p1.X + (int)Math.Round(deltaX * t),
                p1.Y + (int)Math.Round(deltaY * t)
            ));
        }
    }
}
