using System.Windows;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class VectorSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var vector1 = (Vector)(start ?? new Vector(0, 0));
            var vector2 = (Vector)(end ?? vector1);
            property.SetValue(target, new Vector(
                vector1.X + t * (vector2.X - vector1.X),
                vector1.Y + t * (vector2.Y - vector1.Y)));
        }
    }
}
