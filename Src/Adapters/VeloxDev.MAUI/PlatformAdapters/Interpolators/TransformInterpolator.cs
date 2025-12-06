using Microsoft.Maui.Controls.Shapes;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.MAUI.PlatformAdapters.Interpolators
{
    public class TransformInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var m1 = start as Transform ?? new Transform() { Value = Matrix.Identity };
            var m2 = end as Transform ?? m1;

            if (steps == 1) return [m2];

            var result = new List<object?>(steps);
            var matrix1 = m1.Value;
            var matrix2 = m2.Value;

            // 确保初始和结束状态准确
            if (steps > 1)
            {
                result.Add(m1); // 第一步使用原始值

                // 中间步骤
                for (var i = 1; i < steps - 1; i++)
                {
                    var t = (double)i / (steps - 1);
                    var matrix = new Matrix(
                        matrix1.M11 + t * (matrix2.M11 - matrix1.M11),
                        matrix1.M12 + t * (matrix2.M12 - matrix1.M12),
                        matrix1.M21 + t * (matrix2.M21 - matrix1.M21),
                        matrix1.M22 + t * (matrix2.M22 - matrix1.M22),
                        matrix1.OffsetX + t * (matrix2.OffsetX - matrix1.OffsetX),
                        matrix1.OffsetY + t * (matrix2.OffsetY - matrix1.OffsetY)
                    );

                    var transform = new Transform { Value = matrix };
                    result.Add(transform);
                }

                result.Add(m2); // 最后一步使用目标值
            }

            return result;
        }
    }
}
