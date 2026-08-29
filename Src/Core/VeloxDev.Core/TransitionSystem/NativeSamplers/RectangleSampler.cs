using System.Drawing;

namespace VeloxDev.TransitionSystem.NativeSamplers
{
    public class RectangleSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var r1 = (Rectangle)(start ?? default(Rectangle));
            var r2 = (Rectangle)(end ?? r1);
            var deltaX = r2.X - r1.X;
            var deltaY = r2.Y - r1.Y;
            var deltaWidth = r2.Width - r1.Width;
            var deltaHeight = r2.Height - r1.Height;

            property.SetValue(target, new Rectangle(
                r1.X + (int)Math.Round(deltaX * t),
                r1.Y + (int)Math.Round(deltaY * t),
                r1.Width + (int)Math.Round(deltaWidth * t),
                r1.Height + (int)Math.Round(deltaHeight * t)
            ));
        }
    }
}
