using System.Windows;

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

            var size1 = (Size)(start ?? new Size(0, 0));
            var size2 = (Size)(end ?? size1);
            // 宽高共用同一进度，越界时不会变形，且在 0 处停住（负尺寸无法表示）。
            var size = new BoundedProgress(t, 0d, double.PositiveInfinity);
            size.Add(size1.Width, size2.Width);
            size.Add(size1.Height, size2.Height);
            property.SetValue(target, new Size(
                size1.Width + size.Progress * (size2.Width - size1.Width),
                size1.Height + size.Progress * (size2.Height - size1.Height)));
        }
    }
}
