using System.Windows;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class SizeSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            var size1 = (Size)(start ?? new Size(0, 0));
            var size2 = (Size)(end ?? size1);
            // Width and height share one progress so an overshoot cannot skew the shape, and stop at
            // zero: a negative size is not representable.
            var size = new BoundedProgress(t, 0d, double.PositiveInfinity);
            size.Add(size1.Width, size2.Width);
            size.Add(size1.Height, size2.Height);
            property.SetValue(target, new Size(
                size1.Width + size.Progress * (size2.Width - size1.Width),
                size1.Height + size.Progress * (size2.Height - size1.Height)));
        }
    }
}
