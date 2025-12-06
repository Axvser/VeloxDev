using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using VeloxDev.Core.Interfaces.TransitionSystem;
using Windows.Foundation;
using Windows.UI;

namespace VeloxDev.WinUI.PlatformAdapters.Interpolators
{
    public class BrushInterpolator : IValueInterpolator
    {
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var s = Normalize(start);
            var e = Normalize(end);

            if (steps <= 1) return [e];

            (var alignedS, var alignedE) = AlignBrushTypes(s, e);

            List<object?> result = new(steps);
            for (var i = 0; i < steps; i++)
            {
                var t = (double)i / (steps - 1);
                result.Add(InterpolateAligned(alignedS, alignedE, t));
            }

            result[0] = s;
            result[^1] = e;
            return result;
        }

        //----------- Normalize & Safe Align -----------

        private static Brush Normalize(object? obj) => obj switch
        {
            Brush b => b,
            Color c => new SolidColorBrush(c),
            _ => new SolidColorBrush(Colors.Transparent)
        };

        /// <summary>
        /// 尝试将两端 Brush 对齐为相同类型（若类型不同，则转换为逻辑等价形态）
        /// </summary>
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

            // 类型不可对齐 → 保留原样
            return (s, e);
        }

        //----------- 线性渐变转换 -----------

        private static LinearGradientBrush ToLinearEquivalent(SolidColorBrush solid, LinearGradientBrush template)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = template.StartPoint,
                EndPoint = template.EndPoint,
                MappingMode = template.MappingMode,
                SpreadMethod = template.SpreadMethod,
                Opacity = solid.Opacity
            };

            brush.GradientStops.Add(new GradientStop { Color = solid.Color, Offset = 0 });
            brush.GradientStops.Add(new GradientStop { Color = solid.Color, Offset = 1 });
            return brush;
        }

        //----------- 径向渐变转换 -----------

        private static RadialGradientBrush ToRadialEquivalent(SolidColorBrush solid, RadialGradientBrush template)
        {
            var brush = new RadialGradientBrush
            {
                Center = template.Center,
                GradientOrigin = template.GradientOrigin,
                RadiusX = template.RadiusX,
                RadiusY = template.RadiusY,
                MappingMode = template.MappingMode,
                SpreadMethod = template.SpreadMethod,
                Opacity = solid.Opacity
            };

            brush.GradientStops.Add(new GradientStop { Color = solid.Color, Offset = 0 });
            brush.GradientStops.Add(new GradientStop { Color = solid.Color, Offset = 1 });
            return brush;
        }

        //----------- 实际插值逻辑 -----------

        private static Brush InterpolateAligned(Brush s, Brush e, double t)
        {
            try
            {
                switch (s)
                {
                    case SolidColorBrush sb when e is SolidColorBrush eb:
                        return new SolidColorBrush(LerpColorPremultiplied(sb.Color, eb.Color, t))
                        {
                            Opacity = Lerp(sb.Opacity, eb.Opacity, t)
                        };

                    case LinearGradientBrush sl when e is LinearGradientBrush el:
                        return InterpolateLinear(sl, el, t);

                    case RadialGradientBrush sr when e is RadialGradientBrush er:
                        return InterpolateRadial(sr, er, t);

                    default:
                        // 混合为单色退化
                        var c1 = ExtractRepresentativeColor(s);
                        var c2 = ExtractRepresentativeColor(e);
                        var mixed = LerpColorPremultiplied(c1, c2, t);
                        return new SolidColorBrush(mixed)
                        {
                            Opacity = Lerp(s.Opacity, e.Opacity, t)
                        };
                }
            }
            catch
            {
                // 出错则返回末帧
                return e;
            }
        }

        //----------- 线性渐变插值 -----------

        private static LinearGradientBrush InterpolateLinear(LinearGradientBrush s, LinearGradientBrush e, double t)
        {
            var result = new LinearGradientBrush
            {
                StartPoint = LerpPoint(s.StartPoint, e.StartPoint, t),
                EndPoint = LerpPoint(s.EndPoint, e.EndPoint, t),
                MappingMode = e.MappingMode,
                SpreadMethod = e.SpreadMethod,
                Opacity = Lerp(s.Opacity, e.Opacity, t)
            };

            var count = Math.Min(s.GradientStops.Count, e.GradientStops.Count);
            for (var i = 0; i < count; i++)
            {
                result.GradientStops.Add(new GradientStop
                {
                    Color = LerpColorPremultiplied(s.GradientStops[i].Color, e.GradientStops[i].Color, t),
                    Offset = Lerp(s.GradientStops[i].Offset, e.GradientStops[i].Offset, t)
                });
            }
            return result;
        }

        //----------- 径向渐变插值 -----------

        private static RadialGradientBrush InterpolateRadial(RadialGradientBrush s, RadialGradientBrush e, double t)
        {
            var result = new RadialGradientBrush
            {
                Center = LerpPoint(s.Center, e.Center, t),
                GradientOrigin = LerpPoint(s.GradientOrigin, e.GradientOrigin, t),
                RadiusX = Lerp(s.RadiusX, e.RadiusX, t),
                RadiusY = Lerp(s.RadiusY, e.RadiusY, t),
                MappingMode = e.MappingMode,
                SpreadMethod = e.SpreadMethod,
                Opacity = Lerp(s.Opacity, e.Opacity, t)
            };

            var count = Math.Min(s.GradientStops.Count, e.GradientStops.Count);
            for (var i = 0; i < count; i++)
            {
                result.GradientStops.Add(new GradientStop
                {
                    Color = LerpColorPremultiplied(s.GradientStops[i].Color, e.GradientStops[i].Color, t),
                    Offset = Lerp(s.GradientStops[i].Offset, e.GradientStops[i].Offset, t)
                });
            }
            return result;
        }

        //----------- 基础辅助 -----------

        private static Point LerpPoint(Point a, Point b, double t)
            => new(Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t));

        private static Color ExtractRepresentativeColor(Brush brush) => brush switch
        {
            SolidColorBrush sb => sb.Color,
            GradientBrush gb when gb.GradientStops.Count > 0 => gb.GradientStops[^1].Color,
            _ => Colors.Transparent
        };

        private static Color LerpColorPremultiplied(Color a, Color b, double t)
        {
            var aA = a.A / 255.0;
            var bA = b.A / 255.0;

            var ar = a.R * aA;
            var ag = a.G * aA;
            var ab = a.B * aA;

            var br = b.R * bA;
            var bg = b.G * bA;
            var bb = b.B * bA;

            var rr = ar * (1 - t) + br * t;
            var gg = ag * (1 - t) + bg * t;
            var bbC = ab * (1 - t) + bb * t;
            var aa = aA * (1 - t) + bA * t;

            if (aa > 0)
            {
                rr /= aa; gg /= aa; bbC /= aa;
            }

            var A = (byte)Math.Clamp(aa * 255.0, 0, 255);
            var R = (byte)Math.Clamp(rr, 0, 255);
            var G = (byte)Math.Clamp(gg, 0, 255);
            var B = (byte)Math.Clamp(bbC, 0, 255);

            return Color.FromArgb(A, R, G, B);
        }
    }
}
