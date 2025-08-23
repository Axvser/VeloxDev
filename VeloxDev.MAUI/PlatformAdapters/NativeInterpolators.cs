using Microsoft.Maui.Controls.Shapes;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.MAUI.PlatformAdapters
{
    internal static class NativeInterpolators
    {
        public class DoubleInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
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
        }
        public class ThicknessInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
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
        }

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

        public class PointInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
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
        }

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
}
