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

                if (steps <= 1)
                {
                    result.Add(endBrush);
                    return result;
                }

                // 双纯色：使用 RGBA 插值
                if (startBrush is SolidColorBrush startSolid && endBrush is SolidColorBrush endSolid)
                {
                    result.AddRange(InterpolateSolidColors(startSolid, endSolid, steps));
                }
                // 复杂画刷：使用颜色 Alpha 混合
                else
                {
                    result.AddRange(InterpolateComplexBrushes(startBrush, endBrush, steps));
                }

                return result;
            }

            private static IEnumerable<Brush> InterpolateSolidColors(SolidColorBrush start, SolidColorBrush end, int steps)
            {
                Color startColor = start.Color;
                Color endColor = end.Color;

                for (int i = 0; i < steps; i++)
                {
                    double ratio = (double)i / (steps - 1);
                    var color = new Color(
                        startColor.Red + (float)(ratio * (endColor.Red - startColor.Red)),
                        startColor.Green + (float)(ratio * (endColor.Green - startColor.Green)),
                        startColor.Blue + (float)(ratio * (endColor.Blue - startColor.Blue)),
                        startColor.Alpha + (float)(ratio * (endColor.Alpha - startColor.Alpha))
                    );
                    yield return new SolidColorBrush(color);
                }
            }

            private static IEnumerable<Brush> InterpolateComplexBrushes(Brush start, Brush end, int steps)
            {
                for (int i = 0; i < steps; i++)
                {
                    double ratio = (double)i / (steps - 1);

                    // 创建混合画刷
                    Brush blendedBrush = BlendBrushes(start, end, ratio);
                    yield return blendedBrush;
                }
            }

            private static Brush BlendBrushes(Brush brushA, Brush brushB, double ratio)
            {
                // 纯色画刷混合
                if (brushA is SolidColorBrush solidA && brushB is SolidColorBrush solidB)
                {
                    return BlendSolidColors(solidA, solidB, ratio);
                }

                // 渐变画刷混合
                if (brushA is GradientBrush gradientA && brushB is GradientBrush gradientB)
                {
                    return BlendGradientBrushes(gradientA, gradientB, ratio);
                }

                // 默认回退：返回第一个画刷
                return brushA;
            }

            private static SolidColorBrush BlendSolidColors(SolidColorBrush brushA, SolidColorBrush brushB, double ratio)
            {
                Color colorA = brushA.Color;
                Color colorB = brushB.Color;

                Color blendedColor = new(
                    colorA.Red + (float)(ratio * (colorB.Red - colorA.Red)),
                    colorA.Green + (float)(ratio * (colorB.Green - colorA.Green)),
                    colorA.Blue + (float)(ratio * (colorB.Blue - colorA.Blue)),
                    colorA.Alpha + (float)(ratio * (colorB.Alpha - colorA.Alpha))
                );

                return new SolidColorBrush(blendedColor);
            }

            private static GradientBrush BlendGradientBrushes(GradientBrush brushA, GradientBrush brushB, double ratio)
            {
                // 创建混合后的渐变画刷
                GradientBrush blendedBrush = brushA switch
                {
                    LinearGradientBrush linearA when brushB is LinearGradientBrush linearB =>
                        BlendLinearGradients(linearA, linearB, ratio),
                    RadialGradientBrush radialA when brushB is RadialGradientBrush radialB =>
                        BlendRadialGradients(radialA, radialB, ratio),
                    _ => brushA
                };

                return blendedBrush;
            }

            private static LinearGradientBrush BlendLinearGradients(LinearGradientBrush brushA, LinearGradientBrush brushB, double ratio)
            {
                // 插值起点和终点
                Point startPoint = new(
                    brushA.StartPoint.X + ratio * (brushB.StartPoint.X - brushA.StartPoint.X),
                    brushA.StartPoint.Y + ratio * (brushB.StartPoint.Y - brushA.StartPoint.Y));

                Point endPoint = new(
                    brushA.EndPoint.X + ratio * (brushB.EndPoint.X - brushA.EndPoint.X),
                    brushA.EndPoint.Y + ratio * (brushB.EndPoint.Y - brushA.EndPoint.Y));

                // 创建新画刷
                var blendedBrush = new LinearGradientBrush
                {
                    StartPoint = startPoint,
                    EndPoint = endPoint,
                    GradientStops = BlendGradientStops(brushA.GradientStops, brushB.GradientStops, ratio)
                };

                return blendedBrush;
            }

            private static RadialGradientBrush BlendRadialGradients(RadialGradientBrush brushA, RadialGradientBrush brushB, double ratio)
            {
                // 插值圆心和半径
                Point center = new(
                    brushA.Center.X + ratio * (brushB.Center.X - brushA.Center.X),
                    brushA.Center.Y + ratio * (brushB.Center.Y - brushA.Center.Y));

                double radius = brushA.Radius + ratio * (brushB.Radius - brushA.Radius);

                // 创建新画刷
                var blendedBrush = new RadialGradientBrush
                {
                    Center = center,
                    Radius = radius,
                    GradientStops = BlendGradientStops(brushA.GradientStops, brushB.GradientStops, ratio)
                };

                return blendedBrush;
            }

            private static GradientStopCollection BlendGradientStops(GradientStopCollection stopsA, GradientStopCollection stopsB, double ratio)
            {
                var blendedStops = new GradientStopCollection();

                // 确定最大停止点数
                int maxStops = Math.Max(stopsA.Count, stopsB.Count);

                for (int i = 0; i < maxStops; i++)
                {
                    GradientStop stopA = i < stopsA.Count ? stopsA[i] : stopsA.Last();
                    GradientStop stopB = i < stopsB.Count ? stopsB[i] : stopsB.Last();

                    // 插值偏移量
                    float offset = (float)(stopA.Offset + ratio * (stopB.Offset - stopA.Offset));

                    // 插值颜色
                    Color color = BlendColors(stopA.Color, stopB.Color, ratio);

                    blendedStops.Add(new GradientStop(color, offset));
                }

                return blendedStops;
            }

            private static Color BlendColors(Color colorA, Color colorB, double ratio)
            {
                return new Color(
                    colorA.Red + (float)(ratio * (colorB.Red - colorA.Red)),
                    colorA.Green + (float)(ratio * (colorB.Green - colorA.Green)),
                    colorA.Blue + (float)(ratio * (colorB.Blue - colorA.Blue)),
                    colorA.Alpha + (float)(ratio * (colorB.Alpha - colorA.Alpha))
                );
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
