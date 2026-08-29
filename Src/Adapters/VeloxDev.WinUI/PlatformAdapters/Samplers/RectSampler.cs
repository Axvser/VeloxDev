using Windows.Foundation;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class RectSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var r1 = (Rect)(start ?? default(Rect));
            var r2 = (Rect)(end ?? r1);

            var deltaX = r2.X - r1.X;
            var deltaY = r2.Y - r1.Y;
            var deltaWidth = r2.Width - r1.Width;
            var deltaHeight = r2.Height - r1.Height;

            property.SetValue(target, new Rect(
                r1.X + deltaX * t,
                r1.Y + deltaY * t,
                r1.Width + deltaWidth * t,
                r1.Height + deltaHeight * t
            ));
        }
    }
}
