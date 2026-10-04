using System.Drawing;

namespace VeloxDev.TransitionSystem.NativeSamplers
{
    /// <summary>Samples a <see cref="SizeF"/>.</summary>
    public class SizeFSampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            var s1 = (SizeF)(start ?? default(SizeF));
            var s2 = (SizeF)(end ?? s1);
            var deltaWidth = s2.Width - s1.Width;
            var deltaHeight = s2.Height - s1.Height;

            // 宽与高共用一个进度，超调才不会把形状拧歪；且停在零——负尺寸无法表示。
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
