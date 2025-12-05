using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.WinUI.PlatformAdapters.Interpolators
{
    public class ProjectionInterpolator : IValueInterpolator
    {
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var s = Normalize(start);
            var e = Normalize(end);

            if (steps <= 1) return [e];

            List<object?> result = new(steps);
            for (var i = 0; i < steps; i++)
            {
                var t = (double)i / (steps - 1);

                result.Add(new PlaneProjection
                {
                    RotationX = Lerp(s.RotationX, e.RotationX, t),
                    RotationY = Lerp(s.RotationY, e.RotationY, t),
                    RotationZ = Lerp(s.RotationZ, e.RotationZ, t),

                    CenterOfRotationX = Lerp(s.CenterOfRotationX, e.CenterOfRotationX, t),
                    CenterOfRotationY = Lerp(s.CenterOfRotationY, e.CenterOfRotationY, t),
                    CenterOfRotationZ = Lerp(s.CenterOfRotationZ, e.CenterOfRotationZ, t),

                    GlobalOffsetX = Lerp(s.GlobalOffsetX, e.GlobalOffsetX, t),
                    GlobalOffsetY = Lerp(s.GlobalOffsetY, e.GlobalOffsetY, t),
                    GlobalOffsetZ = Lerp(s.GlobalOffsetZ, e.GlobalOffsetZ, t)
                });
            }

            result[0] = s;
            result[^1] = e;
            return result;
        }

        private static PlaneProjection Normalize(object? obj)
        {
            if (obj is PlaneProjection p)
                return p;

            // 默认初始状态：无旋转、无偏移
            return new PlaneProjection
            {
                RotationX = 0,
                RotationY = 0,
                RotationZ = 0,
                CenterOfRotationX = 0.5,
                CenterOfRotationY = 0.5,
                CenterOfRotationZ = 0,
                GlobalOffsetX = 0,
                GlobalOffsetY = 0,
                GlobalOffsetZ = 0
            };
        }
    }
}
