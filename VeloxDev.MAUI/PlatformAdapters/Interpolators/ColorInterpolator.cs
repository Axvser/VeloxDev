using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.MAUI.PlatformAdapters.Interpolators
{
    public class ColorInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            // 处理空值，提供默认值
            var c1 = (Color)(start ?? Colors.Transparent);
            var c2 = (Color)(end ?? Colors.Transparent);

            if (steps <= 0) return [];
            if (steps == 1) return [c2];

            List<object?> result = new(steps);
            var deltaA = c2.Alpha - c1.Alpha;
            var deltaR = c2.Red - c1.Red;
            var deltaG = c2.Green - c1.Green;
            var deltaB = c2.Blue - c1.Blue;

            for (int i = 0; i < steps; i++)
            {
                var t = (float)(i + 1) / steps;
                result.Add(new Color(
                    c1.Red + deltaR * t,
                    c1.Green + deltaG * t,
                    c1.Blue + deltaB * t,
                    c1.Alpha + deltaA * t
                ));
            }

            result[0] = start ?? c1;
            result[steps - 1] = end ?? c2;
            return result;
        }
    }
}
