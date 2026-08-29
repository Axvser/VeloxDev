using Jalium.UI;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class ThicknessSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var t1 = (Thickness)(start ?? new Thickness(0));
            var t2 = (Thickness)(end ?? t1);
            property.SetValue(target, new Thickness(
                t1.Left + (t2.Left - t1.Left) * t,
                t1.Top + (t2.Top - t1.Top) * t,
                t1.Right + (t2.Right - t1.Right) * t,
                t1.Bottom + (t2.Bottom - t1.Bottom) * t));
        }
    }
}
