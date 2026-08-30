using System.Windows;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class ThicknessSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var thickness1 = (Thickness)(start ?? new Thickness(0));
            var thickness2 = (Thickness)(end ?? thickness1);
            property.SetValue(target, new Thickness(
                thickness1.Left + t * (thickness2.Left - thickness1.Left),
                thickness1.Top + t * (thickness2.Top - thickness1.Top),
                thickness1.Right + t * (thickness2.Right - thickness1.Right),
                thickness1.Bottom + t * (thickness2.Bottom - thickness1.Bottom)));
        }
    }
}
