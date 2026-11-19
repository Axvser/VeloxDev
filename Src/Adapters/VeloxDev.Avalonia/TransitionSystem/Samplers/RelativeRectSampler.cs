using Avalonia;
using System;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class RelativeRectSampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            var r1 = (RelativeRect)(start ?? new RelativeRect());
            var r2 = (RelativeRect)(end ?? r1);

            // 单位不同就无法插值，保持起始值。
            if (r1.Unit != r2.Unit)
            {
                property.SetValue(target, r1);
                return;
            }

            var deltaX = r2.Rect.X - r1.Rect.X;
            var deltaY = r2.Rect.Y - r1.Rect.Y;
            var deltaWidth = r2.Rect.Width - r1.Rect.Width;
            var deltaHeight = r2.Rect.Height - r1.Rect.Height;

            // 宽高共用同一进度，越界时不会变形，且在 0 处停住（负尺寸无法表示）；位置不受限。
            var size = new BoundedProgress(t, 0d, double.PositiveInfinity);
            size.Add(r1.Rect.Width, r2.Rect.Width);
            size.Add(r1.Rect.Height, r2.Rect.Height);
            property.SetValue(target, new RelativeRect(
                r1.Rect.X + deltaX * t,
                r1.Rect.Y + deltaY * t,
                Math.Max(0, r1.Rect.Width + deltaWidth * size.Progress),
                Math.Max(0, r1.Rect.Height + deltaHeight * size.Progress),
                r1.Unit
            ));
        }
    }
}