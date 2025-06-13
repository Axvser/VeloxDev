using Avalonia;
using System.Collections.Generic;

namespace VeloxDev.Avalonia.TransitionSystem
{
    internal static class NativeInterpolators
    {

        internal static List<object?> DoubleComputing(object? start, object? end, int steps)
        {
            var d1 = (double)(start ?? 0);
            var d2 = (double)(end ?? d1);
            if (steps == 1)
            {
                return [d2];
            }

            List<object?> result = new(steps);

            var delta = d2 - d1;

            for (var i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                result.Add(d1 + t * delta);
            }
            result[0] = start;
            result[steps - 1] = end;

            return result;
        }
        internal static List<object?> PointComputing(object? start, object? end, int steps)
        {
            var point1 = (Point)(start ?? new Point(0, 0));
            var point2 = (Point)(end ?? point1);
            if (steps == 1)
            {
                return [point2];
            }

            List<object?> result = new(steps);

            for (var i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                var x = point1.X + t * (point2.X - point1.X);
                var y = point1.Y + t * (point2.Y - point1.Y);
                result.Add(new Point(x, y));
            }
            result[0] = start;
            result[steps - 1] = end;

            return result;
        }
        internal static List<object?> ThicknessComputing(object? start, object? end, int steps)
        {
            var thickness1 = (Thickness)(start ?? new Thickness(0));
            var thickness2 = (Thickness)(end ?? thickness1);
            if (steps == 1)
            {
                return [thickness2];
            }

            List<object?> result = new(steps);

            for (var i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                var left = thickness1.Left + t * (thickness2.Left - thickness1.Left);
                var top = thickness1.Top + t * (thickness2.Top - thickness1.Top);
                var right = thickness1.Right + t * (thickness2.Right - thickness1.Right);
                var bottom = thickness1.Bottom + t * (thickness2.Bottom - thickness1.Bottom);
                result.Add(new Thickness(left, top, right, bottom));
            }
            result[0] = start;
            result[steps - 1] = end;

            return result;
        }
        internal static List<object?> CornerRadiusComputing(object? start, object? end, int steps)
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
