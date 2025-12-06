using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.MAUI.PlatformAdapters.Interpolators
{
    public class SizeFInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            // 处理空值，提供默认值
            var s1 = (SizeF)(start ?? new SizeF());
            var s2 = (SizeF)(end ?? new SizeF());

            if (steps <= 0) return [];
            if (steps == 1) return [s2];

            List<object?> result = new(steps);
            var deltaWidth = s2.Width - s1.Width;
            var deltaHeight = s2.Height - s1.Height;

            for (int i = 0; i < steps; i++)
            {
                var t = (float)(i + 1) / steps;
                result.Add(new SizeF(
                    s1.Width + deltaWidth * t,
                    s1.Height + deltaHeight * t
                ));
            }

            result[0] = start ?? s1;
            result[steps - 1] = end ?? s2;
            return result;
        }
    }
}
