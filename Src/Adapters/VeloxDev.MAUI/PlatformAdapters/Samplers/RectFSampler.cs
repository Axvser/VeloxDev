namespace VeloxDev.Adapters.NativeSamplers
{
    public class RectFSampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            // null 时给默认值。
            var r1 = (RectF)(start ?? new RectF());
            var r2 = (RectF)(end ?? new RectF());

            var deltaX = r2.X - r1.X;
            var deltaY = r2.Y - r1.Y;
            var deltaWidth = r2.Width - r1.Width;
            var deltaHeight = r2.Height - r1.Height;

            // 宽高共用同一进度，越界时不会变形，且在 0 处停住（负尺寸无法表示）；位置不受限。
            var size = new BoundedProgress(t, 0d, double.PositiveInfinity);
            size.Add(r1.Width, r2.Width);
            size.Add(r1.Height, r2.Height);
            property.SetValue(target, new RectF(
                r1.X + deltaX * (float)t,
                r1.Y + deltaY * (float)t,
                r1.Width + deltaWidth * (float)size.Progress,
                r1.Height + deltaHeight * (float)size.Progress
            ));
        }
    }
}
