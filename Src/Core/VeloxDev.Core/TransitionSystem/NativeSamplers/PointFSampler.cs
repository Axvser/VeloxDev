using System.Drawing;

namespace VeloxDev.TransitionSystem.NativeSamplers
{
    public class PointFSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

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
