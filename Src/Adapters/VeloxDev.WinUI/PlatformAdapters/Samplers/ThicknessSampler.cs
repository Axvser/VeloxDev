using Microsoft.UI.Xaml;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class ThicknessSampler : ISampler
    {
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var t1 = start is Thickness s ? s : new(0);
            var t2 = end is Thickness e ? e : t1;

            property.SetValue(target, new Thickness(
                Lerp(t1.Left, t2.Left, t),
                Lerp(t1.Top, t2.Top, t),
                Lerp(t1.Right, t2.Right, t),
                Lerp(t1.Bottom, t2.Bottom, t)));
        }
    }
}
