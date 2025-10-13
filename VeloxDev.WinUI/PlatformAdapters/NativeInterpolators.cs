#nullable enable

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using VeloxDev.Core.Interfaces.TransitionSystem;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;

namespace VeloxDev.WinUI.PlatformAdapters
{
    public static class NativeInterpolators
    {
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        //===================== Double =====================
        public class DoubleInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                var d1 = start is double s ? s : 0;
                var d2 = end is double e ? e : d1;

                if (steps <= 1) return [d2];

                List<object?> result = new(steps);
                for (var i = 0; i < steps; i++)
                {
                    var t = (i + 1d) / steps;
                    result.Add(Lerp(d1, d2, t));
                }

                result[0] = start;
                result[^1] = end;
                return result;
            }
        }

        //===================== Thickness =====================
        public class ThicknessInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                var t1 = start is Thickness s ? s : new(0);
                var t2 = end is Thickness e ? e : t1;

                if (steps <= 1) return [t2];

                List<object?> result = new(steps);
                for (var i = 0; i < steps; i++)
                {
                    var t = (i + 1d) / steps;
                    result.Add(new Thickness(
                        Lerp(t1.Left, t2.Left, t),
                        Lerp(t1.Top, t2.Top, t),
                        Lerp(t1.Right, t2.Right, t),
                        Lerp(t1.Bottom, t2.Bottom, t)));
                }

                result[0] = start;
                result[^1] = end;
                return result;
            }
        }

        //===================== CornerRadius =====================
        public class CornerRadiusInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                var c1 = start is CornerRadius s ? s : new(0);
                var c2 = end is CornerRadius e ? e : c1;

                if (steps <= 1) return [c2];

                List<object?> result = new(steps);
                for (var i = 0; i < steps; i++)
                {
                    var t = (i + 1d) / steps;
                    result.Add(new CornerRadius(
                        Lerp(c1.TopLeft, c2.TopLeft, t),
                        Lerp(c1.TopRight, c2.TopRight, t),
                        Lerp(c1.BottomRight, c2.BottomRight, t),
                        Lerp(c1.BottomLeft, c2.BottomLeft, t)));
                }

                result[0] = start;
                result[^1] = end;
                return result;
            }
        }

        //===================== Point =====================
        public class PointInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                var p1 = start is Point s ? s : new(0, 0);
                var p2 = end is Point e ? e : p1;

                if (steps <= 1) return [p2];

                List<object?> result = new(steps);
                for (var i = 0; i < steps; i++)
                {
                    var t = (i + 1d) / steps;
                    result.Add(new Point(Lerp(p1.X, p2.X, t), Lerp(p1.Y, p2.Y, t)));
                }

                result[0] = start;
                result[^1] = end;
                return result;
            }
        }

        //===================== Brush =====================

        public class BrushInterpolator : IValueInterpolator
        {
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

        //===================== Transform =====================
        public class TransformInterpolator : IValueInterpolator
        {
            private static readonly TransformGroup Identity = new();

            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                var s = Normalize(start);
                var e = Normalize(end);
                if (steps <= 1) return [e];

                var startList = ExtractTransforms(s);
                var endList = ExtractTransforms(e);
                var pairs = MatchPairs(startList, endList);

                List<object?> result = new(steps);
                for (var i = 0; i < steps; i++)
                {
                    var t = (double)i / (steps - 1);
                    result.Add(CombineTransforms(pairs, t));
                }

                result[0] = s;
                result[^1] = e;
                return result;
            }

            private static Transform Normalize(object? obj) => obj switch
            {
                Transform t => t,
                _ => new TransformGroup()
            };

            private static List<Transform> ExtractTransforms(Transform t)
            {
                return t is TransformGroup g ? g.Children.ToList() : [t];
            }

            private static List<(Transform? s, Transform? e)> MatchPairs(List<Transform> s, List<Transform> e)
            {
                var types = s.Select(t => t.GetType()).Union(e.Select(t => t.GetType())).Distinct();
                return types.Select(t => (s.LastOrDefault(x => x.GetType() == t), e.LastOrDefault(x => x.GetType() == t))).ToList();
            }

            private static Transform CombineTransforms(List<(Transform? s, Transform? e)> pairs, double t)
            {
                var list = new List<Transform>();
                foreach (var (s, e) in pairs)
                {
                    var interpolated = InterpolateSingle(s, e, t);
                    if (interpolated != null)
                        list.Add(interpolated);
                }

                switch (list.Count)
                {
                    case 0:
                        return new TransformGroup();
                    case 1:
                        return list[0];
                }

                var g = new TransformGroup();
                foreach (var tr in list)
                    g.Children.Add(tr);

                return g;
            }

            private static Transform? InterpolateSingle(Transform? s, Transform? e, double t)
            {
                static Transform Default(Transform? t) => t switch
                {
                    TranslateTransform => new TranslateTransform(),
                    ScaleTransform => new ScaleTransform(),
                    RotateTransform => new RotateTransform(),
                    SkewTransform => new SkewTransform(),
                    _ => new MatrixTransform()
                };

                s ??= Default(e);
                e ??= Default(s);

                if (s.GetType() != e.GetType())
                    return e; // fallback to end transform

                return s switch
                {
                    TranslateTransform st when e is TranslateTransform et =>
                        new TranslateTransform { X = Lerp(st.X, et.X, t), Y = Lerp(st.Y, et.Y, t) },

                    ScaleTransform st when e is ScaleTransform et =>
                        new ScaleTransform { ScaleX = Lerp(st.ScaleX, et.ScaleX, t), ScaleY = Lerp(st.ScaleY, et.ScaleY, t) },

                    RotateTransform st when e is RotateTransform et =>
                        new RotateTransform { Angle = Lerp(st.Angle, et.Angle, t) },

                    SkewTransform st when e is SkewTransform et =>
                        new SkewTransform { AngleX = Lerp(st.AngleX, et.AngleX, t), AngleY = Lerp(st.AngleY, et.AngleY, t) },

                    MatrixTransform st when e is MatrixTransform et =>
                        new MatrixTransform { Matrix = LerpMatrix(st.Matrix, et.Matrix, t) },

                    _ => null
                };
            }

            private static Matrix LerpMatrix(Matrix m1, Matrix m2, double t)
            {
                return new Matrix(
                    Lerp(m1.M11, m2.M11, t),
                    Lerp(m1.M12, m2.M12, t),
                    Lerp(m1.M21, m2.M21, t),
                    Lerp(m1.M22, m2.M22, t),
                    Lerp(m1.OffsetX, m2.OffsetX, t),
                    Lerp(m1.OffsetY, m2.OffsetY, t)
                );
            }
        }
        
        //===================== PlaneProjection =====================
        
        public class ProjectionInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                var s = Normalize(start);
                var e = Normalize(end);

                if (steps <= 1) return [e];

                List<object?> result = new(steps);
                for (var i = 0; i < steps; i++)
                {
                    var t = (double)i / (steps - 1);

                    result.Add(new PlaneProjection
                    {
                        RotationX = Lerp(s.RotationX, e.RotationX, t),
                        RotationY = Lerp(s.RotationY, e.RotationY, t),
                        RotationZ = Lerp(s.RotationZ, e.RotationZ, t),

                        CenterOfRotationX = Lerp(s.CenterOfRotationX, e.CenterOfRotationX, t),
                        CenterOfRotationY = Lerp(s.CenterOfRotationY, e.CenterOfRotationY, t),
                        CenterOfRotationZ = Lerp(s.CenterOfRotationZ, e.CenterOfRotationZ, t),

                        GlobalOffsetX = Lerp(s.GlobalOffsetX, e.GlobalOffsetX, t),
                        GlobalOffsetY = Lerp(s.GlobalOffsetY, e.GlobalOffsetY, t),
                        GlobalOffsetZ = Lerp(s.GlobalOffsetZ, e.GlobalOffsetZ, t)
                    });
                }

                result[0] = s;
                result[^1] = e;
                return result;
            }

            private static PlaneProjection Normalize(object? obj)
            {
                if (obj is PlaneProjection p)
                    return p;

                // 默认初始状态：无旋转、无偏移
                return new PlaneProjection
                {
                    RotationX = 0,
                    RotationY = 0,
                    RotationZ = 0,
                    CenterOfRotationX = 0.5,
                    CenterOfRotationY = 0.5,
                    CenterOfRotationZ = 0,
                    GlobalOffsetX = 0,
                    GlobalOffsetY = 0,
                    GlobalOffsetZ = 0
                };
            }
        }
    }
}
