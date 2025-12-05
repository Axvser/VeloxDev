using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem.NativeInterpolators
{
    public class DecimalInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            if (steps <= 0)
                return [];

            var d1 = (decimal)(start ?? 0m);
            var d2 = (decimal)(end ?? d1);

            if (steps == 1)
                return [d2];

            List<object?> result = new(steps);
            var delta = d2 - d1;

            for (int i = 0; i < steps; i++)
            {
                var t = (decimal)i / (steps - 1);
                var value = d1 + t * delta;
                result.Add(value);
            }

            // 保证最后一帧为接收到的end参数
            result[steps - 1] = end;

            return result;
        }
    }
}
