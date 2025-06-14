using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.MAUI.TransitionSystem
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

                // 处理空值情况
                Brush startBrush = (start as Brush) ?? new SolidColorBrush(Colors.Transparent);
                Brush endBrush = (end as Brush) ?? new SolidColorBrush(Colors.Transparent);

                if (steps <= 1)
                {
                    result.Add(endBrush);
                    return result;
                }

                // 情况1：两个都是纯色画刷 - 完全RGBA插值
                if (startBrush is SolidColorBrush startSolid && endBrush is SolidColorBrush endSolid)
                {
                    result.AddRange(InterpolateSolidColors(startSolid, endSolid, steps));
                }
                // 情况2：两个都是线性渐变画刷
                else if (startBrush is LinearGradientBrush startLinear && endBrush is LinearGradientBrush endLinear)
                {
                    result.AddRange(InterpolateGradientBrushes(startLinear, endLinear, steps));
                }
                // 情况3：两个都是径向渐变画刷
                else if (startBrush is RadialGradientBrush startRadial && endBrush is RadialGradientBrush endRadial)
                {
                    result.AddRange(InterpolateGradientBrushes(startRadial, endRadial, steps));
                }
                // 情况4：类型不匹配或混合类型 - 使用透明度混合
                else
                {
                    result.AddRange(InterpolateWithAlphaBlending(startBrush, endBrush, steps));
                }

                return result;
            }

            private static IEnumerable<Brush> InterpolateSolidColors(SolidColorBrush start, SolidColorBrush end, int steps)
            {
                Color startColor = start.Color ?? Colors.Transparent;
                Color endColor = end.Color ?? startColor;

                for (int i = 0; i < steps; i++)
                {
                    float ratio = (float)i / (steps - 1);

                    var r = (byte)(startColor.Red * 255 + (endColor.Red * 255 - startColor.Red * 255) * ratio);
                    var g = (byte)(startColor.Green * 255 + (endColor.Green * 255 - startColor.Green * 255) * ratio);
                    var b = (byte)(startColor.Blue * 255 + (endColor.Blue * 255 - startColor.Blue * 255) * ratio);
                    var a = (byte)(startColor.Alpha * 255 + (endColor.Alpha * 255 - startColor.Alpha * 255) * ratio);

                    yield return new SolidColorBrush(Color.FromRgba(r, g, b, a));
                }
            }

            private static IEnumerable<Brush> InterpolateGradientBrushes(GradientBrush start, GradientBrush end, int steps)
            {
                // 确保渐变点数量相同
                var startStops = start.GradientStops.OrderBy(gs => gs.Offset).ToArray();
                var endStops = end.GradientStops.OrderBy(gs => gs.Offset).ToArray();

                // 如果渐变点数量不同，取两者中较多的数量
                int maxStops = Math.Max(startStops.Length, endStops.Length);

                for (int i = 0; i < steps; i++)
                {
                    float ratio = (float)i / (steps - 1);

                    var gradientStops = new GradientStopCollection();

                    for (int j = 0; j < maxStops; j++)
                    {
                        var startStop = j < startStops.Length ? startStops[j] : GetNearestStop(startStops, (float)j / maxStops);
                        var endStop = j < endStops.Length ? endStops[j] : GetNearestStop(endStops, (float)j / maxStops);

                        // 插值offset和color
                        float offset = startStop.Offset + (endStop.Offset - startStop.Offset) * ratio;
                        Color color = InterpolateColor(startStop.Color, endStop.Color, ratio);

                        gradientStops.Add(new GradientStop(color, offset));
                    }

                    // 创建相同类型的渐变画刷
                    if (start is LinearGradientBrush startLinear && end is LinearGradientBrush endLinear)
                    {
                        yield return new LinearGradientBrush(gradientStops)
                        {
                            StartPoint = InterpolatePoint(startLinear.StartPoint, endLinear.StartPoint, ratio),
                            EndPoint = InterpolatePoint(startLinear.EndPoint, endLinear.EndPoint, ratio)
                        };
                    }
                    else if (start is RadialGradientBrush startRadial && end is RadialGradientBrush endRadial)
                    {
                        yield return new RadialGradientBrush(gradientStops)
                        {
                            Center = InterpolatePoint(startRadial.Center, endRadial.Center, ratio),
                            Radius = startRadial.Radius + (endRadial.Radius - startRadial.Radius) * ratio
                        };
                    }
                }
            }

            private static IEnumerable<Brush> InterpolateWithAlphaBlending(Brush start, Brush end, int steps)
            {
                // 提取主要颜色（对于渐变画刷取第一个渐变点）
                Color startColor = GetPrimaryColor(start);
                Color endColor = GetPrimaryColor(end);

                for (int i = 0; i < steps; i++)
                {
                    float ratio = (float)i / (steps - 1);
                    float alpha = startColor.Alpha + (endColor.Alpha - startColor.Alpha) * ratio;

                    // 保持结束画刷的类型和属性，只改变透明度
                    var brush = CloneBrush(end);
                    SetBrushAlpha(brush, alpha);

                    yield return brush;
                }
            }

            private static Color GetPrimaryColor(Brush brush)
            {
                return brush switch
                {
                    SolidColorBrush solid => solid.Color,
                    LinearGradientBrush linear => linear.GradientStops.OrderBy(gs => gs.Offset).FirstOrDefault()?.Color ?? Colors.Transparent,
                    RadialGradientBrush radial => radial.GradientStops.OrderBy(gs => gs.Offset).FirstOrDefault()?.Color ?? Colors.Transparent,
                    _ => Colors.Transparent
                };
            }

            private static Brush CloneBrush(Brush original)
            {
                return original switch
                {
                    SolidColorBrush solid => new SolidColorBrush(solid.Color),
                    LinearGradientBrush linear => new LinearGradientBrush
                    {
                        StartPoint = linear.StartPoint,
                        EndPoint = linear.EndPoint,
                        GradientStops = [.. linear.GradientStops.Select(gs => new GradientStop(gs.Color, gs.Offset))]
                    },
                    RadialGradientBrush radial => new RadialGradientBrush
                    {
                        Center = radial.Center,
                        Radius = radial.Radius,
                        GradientStops = [.. radial.GradientStops.Select(gs => new GradientStop(gs.Color, gs.Offset))]
                    },
                    _ => new SolidColorBrush(Colors.Transparent)
                };
            }

            private static void SetBrushAlpha(Brush brush, float alpha)
            {
                switch (brush)
                {
                    case SolidColorBrush solid:
                        solid.Color = new Color(solid.Color.Red, solid.Color.Green, solid.Color.Blue, alpha);
                        break;
                    case GradientBrush gradient:
                        foreach (var stop in gradient.GradientStops)
                        {
                            stop.Color = new Color(stop.Color.Red, stop.Color.Green, stop.Color.Blue, alpha * stop.Color.Alpha);
                        }
                        break;
                }
            }

            private static GradientStop GetNearestStop(GradientStop[] stops, float offset)
            {
                if (stops.Length == 0) return new GradientStop(Colors.Transparent, offset);

                var nearest = stops.OrderBy(gs => Math.Abs(gs.Offset - offset)).First();
                return new GradientStop(nearest.Color, offset);
            }

            private static Color InterpolateColor(Color start, Color end, float ratio)
            {
                return new Color(
                    start.Red + (end.Red - start.Red) * ratio,
                    start.Green + (end.Green - start.Green) * ratio,
                    start.Blue + (end.Blue - start.Blue) * ratio,
                    start.Alpha + (end.Alpha - start.Alpha) * ratio);
            }

            private static Point InterpolatePoint(Point start, Point end, float ratio)
            {
                return new Point(
                    start.X + (end.X - start.X) * ratio,
                    start.Y + (end.Y - start.Y) * ratio);
            }
        }
    }
}
