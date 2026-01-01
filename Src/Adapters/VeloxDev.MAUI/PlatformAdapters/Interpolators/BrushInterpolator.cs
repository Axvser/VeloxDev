using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.MAUI.PlatformAdapters.Interpolators
{
    public class BrushInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            if (steps <= 0)
                return [];

            var s = Normalize(start);
            var e = Normalize(end);

            if (steps <= 1)
                return [e];

            // 对齐画刷类型
            (var alignedS, var alignedE) = AlignBrushTypes(s, e);

            var result = new List<object?>(steps);
            for (var i = 0; i < steps; i++)
            {
                var t = (double)i / (steps - 1);
                result.Add(InterpolateAligned(alignedS, alignedE, t));
            }

            return result;
        }

        #region 标准化和对齐

        private static Brush Normalize(object? obj)
        {
            if (obj is Color c)
                return new SolidColorBrush(c);

            if (obj is Brush b)
                return b;

            return new SolidColorBrush(Colors.Transparent);
        }

        private static (Brush, Brush) AlignBrushTypes(Brush s, Brush e)
        {
            if (s.GetType() == e.GetType())
                return (s, e);

            if (s is SolidColorBrush sb && e is LinearGradientBrush le)
                return (ToLinearEquivalent(sb, le), e);

            if (e is SolidColorBrush eb && s is LinearGradientBrush ls)
                return (s, ToLinearEquivalent(eb, ls));

            if (s is SolidColorBrush sb2 && e is RadialGradientBrush re)
                return (ToRadialEquivalent(sb2, re), e);

            if (e is SolidColorBrush eb2 && s is RadialGradientBrush rs)
                return (s, ToRadialEquivalent(eb2, rs));

            return (s, e);
        }

        private static LinearGradientBrush ToLinearEquivalent(SolidColorBrush solid, LinearGradientBrush template)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = template.StartPoint,
                EndPoint = template.EndPoint
            };

            brush.GradientStops.Add(new GradientStop { Color = solid.Color, Offset = 0f });
            brush.GradientStops.Add(new GradientStop { Color = solid.Color, Offset = 1f });
            return brush;
        }

        private static RadialGradientBrush ToRadialEquivalent(SolidColorBrush solid, RadialGradientBrush template)
        {
            var brush = new RadialGradientBrush
            {
                Center = template.Center,
                Radius = template.Radius
            };

            brush.GradientStops.Add(new GradientStop { Color = solid.Color, Offset = 0f });
            brush.GradientStops.Add(new GradientStop { Color = solid.Color, Offset = 1f });
            return brush;
        }

        #endregion

        #region 核心插值逻辑

        private static Brush InterpolateAligned(Brush s, Brush e, double t)
        {
            try
            {
                if (t <= 0.0) return CloneBrush(s);
                if (t >= 1.0) return CloneBrush(e);

                // 修复：使用is模式匹配而不是switch表达式
                if (s is SolidColorBrush startSolid && e is SolidColorBrush endSolid)
                    return InterpolateSolidColors(startSolid, endSolid, t);

                if (s is LinearGradientBrush startLinear && e is LinearGradientBrush endLinear)
                    return InterpolateLinearGradient(startLinear, endLinear, t);

                if (s is RadialGradientBrush startRadial && e is RadialGradientBrush endRadial)
                    return InterpolateRadialGradient(startRadial, endRadial, t);

                return CreateFallbackInterpolation(s, e, t);
            }
            catch
            {
                return t < 0.5 ? CloneBrush(s) : CloneBrush(e);
            }
        }

        private static SolidColorBrush InterpolateSolidColors(SolidColorBrush start, SolidColorBrush end, double t)
        {
            var startColor = start.Color;
            var endColor = end.Color;

            double red = Lerp(startColor.Red, endColor.Red, t);
            double green = Lerp(startColor.Green, endColor.Green, t);
            double blue = Lerp(startColor.Blue, endColor.Blue, t);
            double alpha = Lerp(startColor.Alpha, endColor.Alpha, t);

            red = ClampToUnit(red);
            green = ClampToUnit(green);
            blue = ClampToUnit(blue);
            alpha = ClampToUnit(alpha);

            return new SolidColorBrush(Color.FromRgba(red, green, blue, alpha));
        }

        private static LinearGradientBrush InterpolateLinearGradient(LinearGradientBrush start, LinearGradientBrush end, double t)
        {
            var result = new LinearGradientBrush
            {
                StartPoint = LerpPoint(start.StartPoint, end.StartPoint, t),
                EndPoint = LerpPoint(start.EndPoint, end.EndPoint, t)
            };

            var maxStops = Math.Max(start.GradientStops.Count, end.GradientStops.Count);

            for (var i = 0; i < maxStops; i++)
            {
                var startStop = i < start.GradientStops.Count ? start.GradientStops[i] : null;
                var endStop = i < end.GradientStops.Count ? end.GradientStops[i] : null;

                if (startStop != null && endStop != null)
                {
                    var startColor = startStop.Color;
                    var endColor = endStop.Color;

                    double red = Lerp(startColor.Red, endColor.Red, t);
                    double green = Lerp(startColor.Green, endColor.Green, t);
                    double blue = Lerp(startColor.Blue, endColor.Blue, t);
                    double alpha = Lerp(startColor.Alpha, endColor.Alpha, t);

                    red = ClampToUnit(red);
                    green = ClampToUnit(green);
                    blue = ClampToUnit(blue);
                    alpha = ClampToUnit(alpha);

                    result.GradientStops.Add(new GradientStop
                    {
                        Color = Color.FromRgba(red, green, blue, alpha),
                        Offset = (float)Lerp(startStop.Offset, endStop.Offset, t)
                    });
                }
                else if (startStop != null)
                {
                    result.GradientStops.Add(new GradientStop
                    {
                        Color = startStop.Color,
                        Offset = startStop.Offset
                    });
                }
                else if (endStop != null)
                {
                    result.GradientStops.Add(new GradientStop
                    {
                        Color = endStop.Color,
                        Offset = endStop.Offset
                    });
                }
            }
            return result;
        }

        private static RadialGradientBrush InterpolateRadialGradient(RadialGradientBrush start, RadialGradientBrush end, double t)
        {
            var result = new RadialGradientBrush
            {
                Center = LerpPoint(start.Center, end.Center, t),
                Radius = (float)Lerp(start.Radius, end.Radius, t)
            };

            var maxStops = Math.Max(start.GradientStops.Count, end.GradientStops.Count);

            for (var i = 0; i < maxStops; i++)
            {
                var startStop = i < start.GradientStops.Count ? start.GradientStops[i] : null;
                var endStop = i < end.GradientStops.Count ? end.GradientStops[i] : null;

                if (startStop != null && endStop != null)
                {
                    var startColor = startStop.Color;
                    var endColor = endStop.Color;

                    double red = Lerp(startColor.Red, endColor.Red, t);
                    double green = Lerp(startColor.Green, endColor.Green, t);
                    double blue = Lerp(startColor.Blue, endColor.Blue, t);
                    double alpha = Lerp(startColor.Alpha, endColor.Alpha, t);

                    red = ClampToUnit(red);
                    green = ClampToUnit(green);
                    blue = ClampToUnit(blue);
                    alpha = ClampToUnit(alpha);

                    result.GradientStops.Add(new GradientStop
                    {
                        Color = Color.FromRgba(red, green, blue, alpha),
                        Offset = (float)Lerp(startStop.Offset, endStop.Offset, t)
                    });
                }
                else if (startStop != null)
                {
                    result.GradientStops.Add(new GradientStop
                    {
                        Color = startStop.Color,
                        Offset = startStop.Offset
                    });
                }
                else if (endStop != null)
                {
                    result.GradientStops.Add(new GradientStop
                    {
                        Color = endStop.Color,
                        Offset = endStop.Offset
                    });
                }
            }
            return result;
        }

        private static SolidColorBrush CreateFallbackInterpolation(Brush start, Brush end, double t)
        {
            var c1 = ExtractRepresentativeColor(start);
            var c2 = ExtractRepresentativeColor(end);

            double red = Lerp(c1.Red, c2.Red, t);
            double green = Lerp(c1.Green, c2.Green, t);
            double blue = Lerp(c1.Blue, c2.Blue, t);
            double alpha = Lerp(c1.Alpha, c2.Alpha, t);

            red = ClampToUnit(red);
            green = ClampToUnit(green);
            blue = ClampToUnit(blue);
            alpha = ClampToUnit(alpha);

            return new SolidColorBrush(Color.FromRgba(red, green, blue, alpha));
        }

        #endregion

        #region 数学辅助方法

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        private static Point LerpPoint(Point a, Point b, double t)
        {
            return new Point(Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t));
        }

        private static double ClampToUnit(double value)
        {
            if (value < 0.0) return 0.0;
            if (value > 1.0) return 1.0;
            return value;
        }

        private static Color ExtractRepresentativeColor(Brush brush)
        {
            if (brush is SolidColorBrush sb)
                return sb.Color;

            if (brush is GradientBrush gb && gb.GradientStops.Count > 0)
                return gb.GradientStops[0].Color;

            return Colors.Transparent;
        }

        private static Brush CloneBrush(Brush original)
        {
            if (original is SolidColorBrush sb)
                return new SolidColorBrush(sb.Color);

            if (original is LinearGradientBrush lb)
                return CloneLinearGradient(lb);

            if (original is RadialGradientBrush rb)
                return CloneRadialGradient(rb);

            return new SolidColorBrush(Colors.Transparent);
        }

        private static LinearGradientBrush CloneLinearGradient(LinearGradientBrush original)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = original.StartPoint,
                EndPoint = original.EndPoint
            };

            foreach (var stop in original.GradientStops)
            {
                brush.GradientStops.Add(new GradientStop
                {
                    Color = stop.Color,
                    Offset = stop.Offset
                });
            }
            return brush;
        }

        private static RadialGradientBrush CloneRadialGradient(RadialGradientBrush original)
        {
            var brush = new RadialGradientBrush
            {
                Center = original.Center,
                Radius = original.Radius
            };

            foreach (var stop in original.GradientStops)
            {
                brush.GradientStops.Add(new GradientStop
                {
                    Color = stop.Color,
                    Offset = stop.Offset
                });
            }
            return brush;
        }

        #endregion
    }
}