using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.Foundation;
using Windows.UI;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class BrushSampler : ISampleable, ISampler
    {
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var s = Normalize(start);
            var e = Normalize(end);
            (var alignedS, var alignedE) = AlignBrushTypes(s, e);
            property.SetValue(target, InterpolateAligned(alignedS, alignedE, t));
        }

        //----------- Normalize & Safe Align -----------

        private static Brush Normalize(object? obj) => obj switch
        {
            Brush b => b,
            Color c => new SolidColorBrush(c),
            _ => new SolidColorBrush(Colors.Transparent)
        };

        /// <summary>
        /// Tries to align the two brushes to the same type; when the types differ, converts them to logically equivalent forms.
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

            // Types cannot be aligned → keep as-is.
            return (s, e);
        }

        //----------- Linear gradient conversion -----------

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

        //----------- Radial gradient conversion -----------

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

        //----------- Actual interpolation logic -----------

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
                        // Degenerate to solid-color blending.
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
                // On error, return the last frame.
                return e;
            }
        }

        //----------- Linear gradient interpolation -----------

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

        //----------- Radial gradient interpolation -----------

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

        //----------- Math helpers -----------

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
