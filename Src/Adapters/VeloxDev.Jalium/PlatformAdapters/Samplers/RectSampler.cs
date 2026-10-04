using Jalium.UI;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class RectSampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            var r1 = (Rect)(start ?? new Rect(0, 0, 0, 0));
            var r2 = (Rect)(end ?? r1);
            // 两个合法矩形的凸插值必为非负（Jalium 的 Rect 构造对负值抛异常）；宽高共用同一进度、在 0 处停住，
            // 负尺寸无法表示；位置不受限。
            var size = new BoundedProgress(t, 0d, double.PositiveInfinity);
            size.Add(r1.Width, r2.Width);
            size.Add(r1.Height, r2.Height);
            property.SetValue(target, new Rect(
                r1.X + (r2.X - r1.X) * t,
                r1.Y + (r2.Y - r1.Y) * t,
                r1.Width + (r2.Width - r1.Width) * size.Progress,
                r1.Height + (r2.Height - r1.Height) * size.Progress));
        }
    }
}
