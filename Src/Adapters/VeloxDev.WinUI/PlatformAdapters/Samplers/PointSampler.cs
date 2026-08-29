using Windows.Foundation;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class PointSampler : ISampleable, ISampler
    {
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var p1 = start is Point s ? s : new(0, 0);
            var p2 = end is Point e ? e : p1;

            property.SetValue(target, new Point(Lerp(p1.X, p2.X, t), Lerp(p1.Y, p2.Y, t)));
        }
    }
}
