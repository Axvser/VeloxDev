using Jalium.UI;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class CornerRadiusSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var c1 = (CornerRadius)(start ?? new CornerRadius(0));
            var c2 = (CornerRadius)(end ?? c1);
            property.SetValue(target, new CornerRadius(
                c1.TopLeft + (c2.TopLeft - c1.TopLeft) * t,
                c1.TopRight + (c2.TopRight - c1.TopRight) * t,
                c1.BottomRight + (c2.BottomRight - c1.BottomRight) * t,
                c1.BottomLeft + (c2.BottomLeft - c1.BottomLeft) * t));
        }
    }
}
