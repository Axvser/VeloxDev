using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.MAUI.PlatformAdapters.Interpolators
{
    public class BrushInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var result = new List<object?>();
            Brush startBrush = start as Brush ?? new SolidColorBrush(Colors.Transparent);
            Brush endBrush = end as Brush ?? new SolidColorBrush(Colors.Transparent);

            // 步骤≤1时直接返回结束值
            if (steps <= 1)
            {
                result.Add(endBrush);
                return result;
            }

            // 仅处理纯色到纯色的插值
            if (startBrush is SolidColorBrush startSolid && endBrush is SolidColorBrush endSolid)
            {
                result.AddRange(InterpolateSolidColors(startSolid, endSolid, steps));
            }
            else
            {
                // 非纯色画刷：前steps-1帧使用起始值，最后一帧使用结束值
                for (int i = 0; i < steps - 1; i++)
                {
                    result.Add(startBrush);
                }
                result.Add(endBrush);
            }

            return result;
        }

        // 纯色画刷插值
        private static IEnumerable<Brush> InterpolateSolidColors(
            SolidColorBrush start, SolidColorBrush end, int steps)
        {
            Color startColor = start.Color;
            Color endColor = end.Color;

            for (int i = 0; i < steps; i++)
            {
                double ratio = (double)i / (steps - 1);

                // 线性插值每个颜色分量
                byte r = (byte)(startColor.Red + (endColor.Red - startColor.Red) * ratio);
                byte g = (byte)(startColor.Green + (endColor.Green - startColor.Green) * ratio);
                byte b = (byte)(startColor.Blue + (endColor.Blue - startColor.Blue) * ratio);
                byte a = (byte)(startColor.Alpha + (endColor.Alpha - startColor.Alpha) * ratio);

                yield return new SolidColorBrush(new Color(r, g, b, a));
            }
        }
    }
}
