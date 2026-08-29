using System.Windows;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class CornerRadiusSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var radius1 = (CornerRadius)(start ?? new CornerRadius(0));
            var radius2 = (CornerRadius)(end ?? radius1);
            property.SetValue(target, new CornerRadius(
                radius1.TopLeft + t * (radius2.TopLeft - radius1.TopLeft),
                radius1.TopRight + t * (radius2.TopRight - radius1.TopRight),
                radius1.BottomRight + t * (radius2.BottomRight - radius1.BottomRight),
                radius1.BottomLeft + t * (radius2.BottomLeft - radius1.BottomLeft)));
        }
    }
}
