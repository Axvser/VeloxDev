using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem.NativeInterpolators
{
    public class LongInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            if (steps <= 0)
                return [];

            var l1 = (long)(start ?? 0L);
            var l2 = (long)(end ?? l1);

            if (steps == 1)
                return [l2];

            List<object?> result = new(steps);

            // 处理边界情况：起始值和结束值相同
            if (l1 == l2)
            {
                for (int i = 0; i < steps; i++)
                {
                    result.Add(l1);
                }
                return result;
            }

            // 使用decimal进行中间计算以避免溢出
            var delta = (decimal)l2 - (decimal)l1;

            for (int i = 0; i < steps; i++)
            {
                var t = (decimal)i / (steps - 1);
                var intermediateValue = (decimal)l1 + t * delta;
                var value = (long)intermediateValue;
                result.Add(value);
            }

            // 保证首尾帧精确
            result[0] = start;
            result[steps - 1] = end;

            return result;
        }
    }
}
