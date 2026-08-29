using Windows.Foundation;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class SizeSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var s1 = (Size)(start ?? default(Size));
            var s2 = (Size)(end ?? s1);

            var deltaWidth = s2.Width - s1.Width;
            var deltaHeight = s2.Height - s1.Height;

            property.SetValue(target, new Size(
                s1.Width + deltaWidth * t,
                s1.Height + deltaHeight * t
            ));
        }
    }
}
