namespace VeloxDev.Adapters.NativeSamplers
{
    public class SizeFSampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            // null 时给默认值。
            var s1 = (SizeF)(start ?? new SizeF());
            var s2 = (SizeF)(end ?? new SizeF());

            var deltaWidth = s2.Width - s1.Width;
            var deltaHeight = s2.Height - s1.Height;

            // 宽高共用同一进度，越界时不会变形，且在 0 处停住（负尺寸无法表示）。
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
