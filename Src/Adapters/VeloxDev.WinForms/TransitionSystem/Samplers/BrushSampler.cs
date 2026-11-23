using System.Drawing;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;
using Easel;

namespace VeloxDev.Adapters.NativeSamplers
{
    /// <summary>
    /// Interpolates the representative colour of two brushes and writes a solid brush for each frame.
    /// </summary>
    /// <remarks>
    /// GDI+ brushes are not value types and gradients are not interpolable channel-by-channel here; a gradient
    /// endpoint is flattened to <see cref="Brush.ApproximateColor"/>. The written value is therefore always a
    /// <see cref="SolidColorBrush"/>, which is what the existing paint paths consume.
    /// </remarks>
    public class BrushSampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;

        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(
            object target,
            ITransitionProperty property,
            ref object? working,
            object? start,
            object? end,
            object? options,
            double t)
        {
            var first = (start as Easel.Brush)?.ApproximateColor ?? Color.Transparent;
            var second = (end as Easel.Brush)?.ApproximateColor ?? first;
            property.SetValue(target, new SolidColorBrush(Lerp(first, second, t)));
        }

        private static Color Lerp(Color start, Color end, double t)
            => Color.FromArgb(
                Channel(start.A, end.A, t),
                Channel(start.R, end.R, t),
                Channel(start.G, end.G, t),
                Channel(start.B, end.B, t));

        // Color.FromArgb 会把通道夹到 0..255；越过 [0,1] 的时间因此饱和而不是继续外推。
        private static int Channel(byte start, byte end, double t)
            => Math.Min(255, Math.Max(0, (int)Math.Round(start + (end - start) * t)));
    }
}
