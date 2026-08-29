using System.Windows;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class SizeSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var size1 = (Size)(start ?? new Size(0, 0));
            var size2 = (Size)(end ?? size1);
            property.SetValue(target, new Size(
                size1.Width + t * (size2.Width - size1.Width),
                size1.Height + t * (size2.Height - size1.Height)));
        }
    }
}
