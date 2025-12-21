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

            // 确保首尾帧精确
            result[0] = s;
            result[steps - 1] = e;  // 修复这行：使用索引而不是赋值

            return result;
        }

        #region 标准化和对齐

        private static Brush Normalize(object? obj)
        {
            if (obj is Brush b)
                return b;

            if (obj is Color c)
                return new SolidColorBrush(c);

            return new SolidColorBrush(Colors.Transparent);
        }

        /// <summary>
        /// 对齐画刷类型以便插值
        /// </summary>
        private static (Brush, Brush) AlignBrushTypes(Brush s, Brush e)
        {
            if (s.GetType() == e.GetType())
                return (s, e);

            // SolidColorBrush ↔ LinearGradientBrush 转换
            if (s is SolidColorBrush sb && e is LinearGradientBrush le)
                return (ToLinearEquivalent(sb, le), e);

            if (e is SolidColorBrush eb && s is LinearGradientBrush ls)
                return (s, ToLinearEquivalent(eb, ls));

            // SolidColorBrush ↔ RadialGradientBrush 转换  
            if (s is SolidColorBrush sb2 && e is RadialGradientBrush re)
                return (ToRadialEquivalent(sb2, re), e);

            if (e is SolidColorBrush eb2 && s is RadialGradientBrush rs)
                return (s, ToRadialEquivalent(eb2, rs));

            // 无法对齐的类型保持原样
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
                return (s, e) switch
                {
                    (SolidColorBrush sb, SolidColorBrush eb) => InterpolateSolidColors(sb, eb, t),
                    (LinearGradientBrush sl, LinearGradientBrush el) => InterpolateLinearGradient(sl, el, t),
                    (RadialGradientBrush sr, RadialGradientBrush er) => InterpolateRadialGradient(sr, er, t),
                    _ => CreateFallbackInterpolation(s, e, t),
                };
            }
            catch
            {
                // 出错时返回结束值
                return e;
            }
        }

        private static SolidColorBrush InterpolateSolidColors(SolidColorBrush start, SolidColorBrush end, double t)
        {
            return new SolidColorBrush(LerpColorPremultiplied(start.Color, end.Color, t));
        }

        private static LinearGradientBrush InterpolateLinearGradient(LinearGradientBrush start, LinearGradientBrush end, double t)
        {
            var result = new LinearGradientBrush
            {
                StartPoint = LerpPoint(start.StartPoint, end.StartPoint, t),
                EndPoint = LerpPoint(start.EndPoint, end.EndPoint, t)
            };

            var count = Math.Min(start.GradientStops.Count, end.GradientStops.Count);
            for (var i = 0; i < count; i++)
            {
                result.GradientStops.Add(new GradientStop
                {
                    Color = LerpColorPremultiplied(start.GradientStops[i].Color, end.GradientStops[i].Color, t),
                    Offset = (float)Lerp(start.GradientStops[i].Offset, end.GradientStops[i].Offset, t)
                });
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

            var count = Math.Min(start.GradientStops.Count, end.GradientStops.Count);
            for (var i = 0; i < count; i++)
            {
                result.GradientStops.Add(new GradientStop
                {
                    Color = LerpColorPremultiplied(start.GradientStops[i].Color, end.GradientStops[i].Color, t),
                    Offset = (float)Lerp(start.GradientStops[i].Offset, end.GradientStops[i].Offset, t)
                });
            }
            return result;
        }

        private static SolidColorBrush CreateFallbackInterpolation(Brush start, Brush end, double t)
        {
            var c1 = ExtractRepresentativeColor(start);
            var c2 = ExtractRepresentativeColor(end);
            return new SolidColorBrush(LerpColorPremultiplied(c1, c2, t));
        }

        #endregion

        #region 数学辅助方法

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        private static Point LerpPoint(Point a, Point b, double t)
        {
            return new Point(Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t));
        }

        private static Color ExtractRepresentativeColor(Brush brush)
        {
            if (brush is SolidColorBrush sb)
                return sb.Color;

            if (brush is GradientBrush gb && gb.GradientStops.Count > 0)
                return gb.GradientStops[0].Color;  // 修复：使用索引而不是属性

            return Colors.Transparent;
        }

        /// <summary>
        /// 使用预乘Alpha的颜色插值（更准确的颜色混合）
        /// </summary>
        private static Color LerpColorPremultiplied(Color a, Color b, double t)
        {
            var aA = a.Alpha / 255.0;
            var bA = b.Alpha / 255.0;

            // 预乘RGB分量
            var ar = a.Red * aA;
            var ag = a.Green * aA;
            var ab = a.Blue * aA;

            var br = b.Red * bA;
            var bg = b.Green * bA;
            var bb = b.Blue * bA;

            // 插值预乘后的分量
            var rr = ar * (1.0 - t) + br * t;
            var gg = ag * (1.0 - t) + bg * t;
            var bbResult = ab * (1.0 - t) + bb * t;
            var aa = aA * (1.0 - t) + bA * t;

            // 反预乘
            if (aa > 0)
            {
                rr /= aa;
                gg /= aa;
                bbResult /= aa;
            }

            var alpha = (byte)Math.Clamp(aa * 255.0, 0, 255);
            var red = (byte)Math.Clamp(rr * 255.0, 0, 255);
            var green = (byte)Math.Clamp(gg * 255.0, 0, 255);
            var blue = (byte)Math.Clamp(bbResult * 255.0, 0, 255);

            return Color.FromRgba(red, green, blue, alpha);
        }

        #endregion
    }
}