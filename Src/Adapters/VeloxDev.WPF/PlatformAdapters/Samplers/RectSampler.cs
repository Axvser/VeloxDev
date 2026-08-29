using System.Windows;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class RectSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var rect1 = (Rect)(start ?? new Rect(0, 0, 0, 0));
            var rect2 = (Rect)(end ?? rect1);
            property.SetValue(target, new Rect(
                rect1.X + t * (rect2.X - rect1.X),
                rect1.Y + t * (rect2.Y - rect1.Y),
                rect1.Width + t * (rect2.Width - rect1.Width),
                rect1.Height + t * (rect2.Height - rect1.Height)));
        }
    }
}
