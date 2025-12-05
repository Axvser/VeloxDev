using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem.NativeInterpolators
{
    public class FloatInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            if (steps <= 0)
                return [];

            var f1 = (float)(start ?? 0f);
            var f2 = (float)(end ?? f1);

            if (steps == 1)
                return [f2];

            List<object?> result = new(steps);
            var delta = f2 - f1;

            for (int i = 0; i < steps; i++)
            {
                var t = (float)i / (steps - 1);
                var value = f1 + t * delta;
                result.Add(value);
            }

            // 保证最后一帧为接收到的end参数
            result[steps - 1] = end;

            return result;
        }
    }
}
