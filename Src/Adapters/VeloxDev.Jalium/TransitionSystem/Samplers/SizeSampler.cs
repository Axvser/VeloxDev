using Jalium.UI;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class SizeSampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            var s1 = (Size)(start ?? default(Size));
            var s2 = (Size)(end ?? s1);
            var deltaWidth = s2.Width - s1.Width;
            var deltaHeight = s2.Height - s1.Height;
            // 两个合法尺寸的凸插值必为非负（Jalium 的 Size 构造对负值抛异常）；宽高共用同一进度、在 0 处停住，负尺寸无法表示。
            var size = new BoundedProgress(t, 0d, double.PositiveInfinity);
            size.Add(s1.Width, s2.Width);
            size.Add(s1.Height, s2.Height);
            property.SetValue(target, new Size(
                s1.Width + deltaWidth * size.Progress,
                s1.Height + deltaHeight * size.Progress));
        }
    }
}
