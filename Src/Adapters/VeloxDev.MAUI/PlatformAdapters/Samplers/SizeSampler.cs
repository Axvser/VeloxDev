namespace VeloxDev.Adapters.NativeSamplers
{
    public class SizeSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            // Handle null values by providing defaults.
            var s1 = (Size)(start ?? Size.Zero);
            var s2 = (Size)(end ?? Size.Zero);

            var deltaWidth = s2.Width - s1.Width;
            var deltaHeight = s2.Height - s1.Height;

            // Width and height share one progress so an overshoot cannot skew the shape, and stop at
            // zero: a negative size is not representable.
            var size = new BoundedProgress(t, 0d, double.PositiveInfinity);
            size.Add(s1.Width, s2.Width);
            size.Add(s1.Height, s2.Height);
            property.SetValue(target, new Size(
                s1.Width + deltaWidth * size.Progress,
                s1.Height + deltaHeight * size.Progress
            ));
        }
    }
}
