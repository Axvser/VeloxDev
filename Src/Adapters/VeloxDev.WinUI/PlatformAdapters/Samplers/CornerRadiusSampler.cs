using Microsoft.UI.Xaml;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class CornerRadiusSampler : ISampler
    {
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var c1 = start is CornerRadius s ? s : new(0);
            var c2 = end is CornerRadius e ? e : c1;

            property.SetValue(target, new CornerRadius(
                Lerp(c1.TopLeft, c2.TopLeft, t),
                Lerp(c1.TopRight, c2.TopRight, t),
                Lerp(c1.BottomRight, c2.BottomRight, t),
                Lerp(c1.BottomLeft, c2.BottomLeft, t)));
        }
    }
}
