using System.Drawing;

namespace VeloxDev.TransitionSystem.NativeSamplers
{
    /// <summary>Samples a <see cref="Size"/>.</summary>
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

            // 宽与高共用一个进度，超调才不会把形状拧歪；且停在零——负尺寸无法表示。
            var size = new BoundedProgress(t, 0d, double.PositiveInfinity);
            size.Add(s1.Width, s2.Width);
            size.Add(s1.Height, s2.Height);
            property.SetValue(target, new Size(
                s1.Width + (int)Math.Round(deltaWidth * size.Progress),
                s1.Height + (int)Math.Round(deltaHeight * size.Progress)
            ));
        }
    }
}
