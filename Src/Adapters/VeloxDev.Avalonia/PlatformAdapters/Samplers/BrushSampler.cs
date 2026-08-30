using Avalonia;
using Avalonia.Media;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class BrushSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var endBrush = end as IBrush ?? Brushes.Transparent;
            var startBrush = AdaptStartBrush(start);

            if (startBrush is ISolidColorBrush ss && endBrush is ISolidColorBrush se)
            {
                // Zero per-frame allocation: reuse a scratch brush, recomputing its color/opacity from the pristine start/end.
                if (working is not SolidColorBrush wb)
                {
                    wb = new SolidColorBrush();
                    working = wb;
                }
                wb.Color = LerpColor(ss.Color, se.Color, t);
                wb.Opacity = ss.Opacity + (se.Opacity - ss.Opacity) * t;
                property.SetValue(target, wb);
                return;
            }

            if (startBrush is LinearGradientBrush sl && endBrush is LinearGradientBrush el
                && sl.GradientStops.Count == el.GradientStops.Count)
            {
                // Zero per-frame allocation: reuse a scratch linear gradient, recomputing its stops from the pristine start/end.
                if (working is not LinearGradientBrush wl || wl.GradientStops.Count != sl.GradientStops.Count)
                {
                    wl = new LinearGradientBrush { StartPoint = sl.StartPoint, EndPoint = sl.EndPoint };
                    for (var i = 0; i < sl.GradientStops.Count; i++)
                        wl.GradientStops.Add(new GradientStop());
                    working = wl;
                }
                wl.StartPoint = LerpRelativePoint(sl.StartPoint, el.StartPoint, t);
                wl.EndPoint = LerpRelativePoint(sl.EndPoint, el.EndPoint, t);
                for (var i = 0; i < sl.GradientStops.Count; i++)
                {
                    wl.GradientStops[i].Color = LerpColor(sl.GradientStops[i].Color, el.GradientStops[i].Color, t);
                    wl.GradientStops[i].Offset = sl.GradientStops[i].Offset + (el.GradientStops[i].Offset - sl.GradientStops[i].Offset) * t;
                }
                property.SetValue(target, wl);
                return;
            }

            if (startBrush is RadialGradientBrush sr && endBrush is RadialGradientBrush er
                && sr.GradientStops.Count == er.GradientStops.Count)
            {
                // Zero per-frame allocation: reuse a scratch radial gradient, recomputing its stops from the pristine start/end.
                if (working is not RadialGradientBrush wr || wr.GradientStops.Count != sr.GradientStops.Count)
                {
                    wr = new RadialGradientBrush { Center = sr.Center, Radius = sr.Radius };
                    for (var i = 0; i < sr.GradientStops.Count; i++)
                        wr.GradientStops.Add(new GradientStop());
                    working = wr;
                }
                wr.Center = LerpRelativePoint(sr.Center, er.Center, t);
                wr.Radius = sr.Radius + (er.Radius - sr.Radius) * t;
                for (var i = 0; i < sr.GradientStops.Count; i++)
                {
                    wr.GradientStops[i].Color = LerpColor(sr.GradientStops[i].Color, er.GradientStops[i].Color, t);
                    wr.GradientStops[i].Offset = sr.GradientStops[i].Offset + (er.GradientStops[i].Offset - sr.GradientStops[i].Offset) * t;
                }
                property.SetValue(target, wr);
                return;
            }

            // Unhandled brush kinds (image, conic, mixed types, different stop counts) → blend to a representative
            // color in a scratch solid brush — zero per-frame allocation (no RenderTargetBitmap).
            var c1 = ExtractRepresentativeColor(startBrush);
            var c2 = ExtractRepresentativeColor(endBrush);
            if (working is not SolidColorBrush wb2)
            {
                wb2 = new SolidColorBrush();
                working = wb2;
            }
            wb2.Color = LerpColor(c1, c2, t);
            wb2.Opacity = startBrush.Opacity + (endBrush.Opacity - startBrush.Opacity) * t;
            property.SetValue(target, wb2);
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        private static RelativePoint LerpRelativePoint(RelativePoint a, RelativePoint b, double t)
        {
            return new RelativePoint(
                new Point(Lerp(a.Point.X, b.Point.X, t), Lerp(a.Point.Y, b.Point.Y, t)),
                a.Unit);
        }

        private static IBrush AdaptStartBrush(object? start)
        {
            if (start == null)
            {
                return Brushes.Transparent;
            }

            return (IBrush)start;
        }

        private static Color LerpColor(Color c1, Color c2, double t) => Color.FromArgb(
            (byte)(c1.A + (c2.A - c1.A) * t),
            (byte)(c1.R + (c2.R - c1.R) * t),
            (byte)(c1.G + (c2.G - c1.G) * t),
            (byte)(c1.B + (c2.B - c1.B) * t));

        private static Color ExtractRepresentativeColor(IBrush brush)
        {
            if (brush is ISolidColorBrush sb)
            {
                return sb.Color;
            }

            if (brush is IGradientBrush gb && gb.GradientStops.Count > 0)
            {
                return gb.GradientStops[0].Color;
            }

            return Colors.Transparent;
        }
    }
}
