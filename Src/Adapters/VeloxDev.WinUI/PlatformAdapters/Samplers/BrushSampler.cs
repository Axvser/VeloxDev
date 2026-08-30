using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.Foundation;
using Windows.UI;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class BrushSampler : ISampler
    {
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            // Normalize the start/end once per animation (a Color/null input must not allocate a brush per frame).
            if (working is not NormalizedState st)
            {
                st = new NormalizedState { Start = Normalize(start), End = Normalize(end) };
                working = st;
            }
            var s = st.Start;
            var e = st.End;

            if (s is SolidColorBrush ss && e is SolidColorBrush se)
            {
                // Zero per-frame allocation: reuse a scratch brush, recomputing its color/opacity from the pristine start/end.
                if (st.Scratch is not SolidColorBrush wb)
                {
                    wb = new SolidColorBrush();
                    st.Scratch = wb;
                }
                wb.Color = LerpColorPremultiplied(ss.Color, se.Color, t);
                wb.Opacity = Lerp(ss.Opacity, se.Opacity, t);
                property.SetValue(target, wb);
                return;
            }

            if (s is LinearGradientBrush sl && e is LinearGradientBrush el
                && sl.GradientStops.Count == el.GradientStops.Count)
            {
                // Zero per-frame allocation: reuse a scratch linear gradient, recomputing its stops from the pristine start/end.
                if (st.Scratch is not LinearGradientBrush wl || wl.GradientStops.Count != sl.GradientStops.Count)
                {
                    wl = new LinearGradientBrush { StartPoint = sl.StartPoint, EndPoint = sl.EndPoint };
                    for (var i = 0; i < sl.GradientStops.Count; i++)
                        wl.GradientStops.Add(new GradientStop());
                    st.Scratch = wl;
                }
                wl.StartPoint = LerpPoint(sl.StartPoint, el.StartPoint, t);
                wl.EndPoint = LerpPoint(sl.EndPoint, el.EndPoint, t);
                for (var i = 0; i < sl.GradientStops.Count; i++)
                {
                    wl.GradientStops[i].Color = LerpColorPremultiplied(sl.GradientStops[i].Color, el.GradientStops[i].Color, t);
                    wl.GradientStops[i].Offset = Lerp(sl.GradientStops[i].Offset, el.GradientStops[i].Offset, t);
                }
                property.SetValue(target, wl);
                return;
            }

            if (s is RadialGradientBrush sr && e is RadialGradientBrush er
                && sr.GradientStops.Count == er.GradientStops.Count)
            {
                // Zero per-frame allocation: reuse a scratch radial gradient, recomputing its stops from the pristine start/end.
                if (st.Scratch is not RadialGradientBrush wr || wr.GradientStops.Count != sr.GradientStops.Count)
                {
                    wr = new RadialGradientBrush { Center = sr.Center, RadiusX = sr.RadiusX, RadiusY = sr.RadiusY };
                    for (var i = 0; i < sr.GradientStops.Count; i++)
                        wr.GradientStops.Add(new GradientStop());
                    st.Scratch = wr;
                }
                wr.Center = LerpPoint(sr.Center, er.Center, t);
                wr.RadiusX = Lerp(sr.RadiusX, er.RadiusX, t);
                wr.RadiusY = Lerp(sr.RadiusY, er.RadiusY, t);
                for (var i = 0; i < sr.GradientStops.Count; i++)
                {
                    wr.GradientStops[i].Color = LerpColorPremultiplied(sr.GradientStops[i].Color, er.GradientStops[i].Color, t);
                    wr.GradientStops[i].Offset = Lerp(sr.GradientStops[i].Offset, er.GradientStops[i].Offset, t);
                }
                property.SetValue(target, wr);
                return;
            }

            // Mixed types / different stop counts / other brushes → blend to a representative color in a scratch
            // solid brush — zero per-frame WinRT object allocation.
            var c1 = ExtractRepresentativeColor(s);
            var c2 = ExtractRepresentativeColor(e);
            if (st.Scratch is not SolidColorBrush wb2)
            {
                wb2 = new SolidColorBrush();
                st.Scratch = wb2;
            }
            wb2.Color = LerpColorPremultiplied(c1, c2, t);
            wb2.Opacity = Lerp(s.Opacity, e.Opacity, t);
            property.SetValue(target, wb2);
        }

        private sealed class NormalizedState
        {
            public Brush Start = null!;
            public Brush End = null!;
            public object? Scratch;
        }

        //----------- Normalize -----------

        private static Brush Normalize(object? obj) => obj switch
        {
            Brush b => b,
            Color c => new SolidColorBrush(c),
            _ => new SolidColorBrush(Colors.Transparent)
        };

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
