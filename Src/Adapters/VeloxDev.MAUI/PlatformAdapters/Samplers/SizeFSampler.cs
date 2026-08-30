namespace VeloxDev.Adapters.NativeSamplers
{
    public class SizeFSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            // Handle null values by providing defaults.
            var s1 = (SizeF)(start ?? new SizeF());
            var s2 = (SizeF)(end ?? new SizeF());

            var deltaWidth = s2.Width - s1.Width;
            var deltaHeight = s2.Height - s1.Height;

            property.SetValue(target, new SizeF(
                s1.Width + deltaWidth * (float)t,
                s1.Height + deltaHeight * (float)t
            ));
        }
    }
}
