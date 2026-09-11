using System.Drawing;

namespace VeloxDev.TransitionSystem.NativeSamplers
{
    public class SizeFSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            var s1 = (SizeF)(start ?? default(SizeF));
            var s2 = (SizeF)(end ?? s1);
            var deltaWidth = s2.Width - s1.Width;
            var deltaHeight = s2.Height - s1.Height;

            // Width and height share one progress so an overshoot cannot skew the shape, and stop at
            // zero: a negative size is not representable.
            var size = new BoundedProgress(t, 0d, double.PositiveInfinity);
            size.Add(s1.Width, s2.Width);
            size.Add(s1.Height, s2.Height);
            property.SetValue(target, new SizeF(
                s1.Width + deltaWidth * (float)size.Progress,
                s1.Height + deltaHeight * (float)size.Progress
            ));
        }
    }
}
