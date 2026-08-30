using System.Drawing;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class RectFSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            // Handle null values by providing defaults.
            var r1 = (RectangleF)(start ?? RectangleF.Empty);
            var r2 = (RectangleF)(end ?? RectangleF.Empty);

            var deltaX = r2.X - r1.X;
            var deltaY = r2.Y - r1.Y;
            var deltaWidth = r2.Width - r1.Width;
            var deltaHeight = r2.Height - r1.Height;

            property.SetValue(target, new RectangleF(
                r1.X + deltaX * (float)t,
                r1.Y + deltaY * (float)t,
                r1.Width + deltaWidth * (float)t,
                r1.Height + deltaHeight * (float)t
            ));
        }
    }
}
