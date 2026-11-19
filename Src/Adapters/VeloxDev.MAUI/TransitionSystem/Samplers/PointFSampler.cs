namespace VeloxDev.Adapters.NativeSamplers
{
    public class PointFSampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            // null 时给默认值。
            var p1 = (PointF)(start ?? new PointF());
            var p2 = (PointF)(end ?? new PointF());

            var deltaX = p2.X - p1.X;
            var deltaY = p2.Y - p1.Y;

            property.SetValue(target, new PointF(
                p1.X + deltaX * (float)t,
                p1.Y + deltaY * (float)t
            ));
        }
    }
}
