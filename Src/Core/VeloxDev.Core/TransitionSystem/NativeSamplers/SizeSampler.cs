using System.Drawing;

namespace VeloxDev.TransitionSystem.NativeSamplers
{
    public class SizeSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var s1 = (Size)(start ?? default(Size));
            var s2 = (Size)(end ?? s1);
            var deltaWidth = s2.Width - s1.Width;
            var deltaHeight = s2.Height - s1.Height;

            property.SetValue(target, new Size(
                s1.Width + (int)Math.Round(deltaWidth * t),
                s1.Height + (int)Math.Round(deltaHeight * t)
            ));
        }
    }
}
