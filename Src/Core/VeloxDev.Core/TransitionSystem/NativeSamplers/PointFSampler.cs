using System.Drawing;

namespace VeloxDev.TransitionSystem.NativeSamplers
{
    public class PointFSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var p1 = (PointF)(start ?? default(PointF));
            var p2 = (PointF)(end ?? p1);
            var deltaX = p2.X - p1.X;
            var deltaY = p2.Y - p1.Y;

            property.SetValue(target, new PointF(
                p1.X + deltaX * (float)t,
                p1.Y + deltaY * (float)t
            ));
        }
    }
}
