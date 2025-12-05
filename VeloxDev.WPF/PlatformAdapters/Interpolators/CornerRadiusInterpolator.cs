using System.Windows;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.WPF.PlatformAdapters.Interpolators
{
    public class CornerRadiusInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var radius1 = (CornerRadius)(start ?? new CornerRadius(0));
            var radius2 = (CornerRadius)(end ?? radius1);
            if (steps == 1)
            {
                return [radius2];
            }

            List<object?> result = new(steps);

            for (var i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                var topLeft = radius1.TopLeft + t * (radius2.TopLeft - radius1.TopLeft);
                var topRight = radius1.TopRight + t * (radius2.TopRight - radius1.TopRight);
                var bottomLeft = radius1.BottomLeft + t * (radius2.BottomLeft - radius1.BottomLeft);
                var bottomRight = radius1.BottomRight + t * (radius2.BottomRight - radius1.BottomRight);
                result.Add(new CornerRadius(topLeft, topRight, bottomRight, bottomLeft));
            }
            result[0] = start;
            result[steps - 1] = end;

            return result;
        }
    }
}
