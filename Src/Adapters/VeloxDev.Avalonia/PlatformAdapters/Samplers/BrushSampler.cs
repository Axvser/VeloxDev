using Avalonia;
using Avalonia.Media;
using System;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class BrushSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

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
                wb.Opacity = Math.Max(0, Math.Min(1, ss.Opacity + (se.Opacity - ss.Opacity) * t));
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
                // The offsets are one value: they share a progress so the stops keep their spacing instead of
                // crossing, which would invert the gradient. It stops at [0,1] — an offset outside that range is
                // not a valid gradient stop.
                var offsets = new BoundedProgress(t, 0d, 1d);
                for (var i = 0; i < sl.GradientStops.Count; i++)
                    offsets.Add(sl.GradientStops[i].Offset, el.GradientStops[i].Offset);

                for (var i = 0; i < sl.GradientStops.Count; i++)
                {
                    wl.GradientStops[i].Color = LerpColor(sl.GradientStops[i].Color, el.GradientStops[i].Color, t);
                    wl.GradientStops[i].Offset = sl.GradientStops[i].Offset + (el.GradientStops[i].Offset - sl.GradientStops[i].Offset) * offsets.Progress;
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
                    wr = new RadialGradientBrush { Center = sr.Center, RadiusX = sr.RadiusX, RadiusY = sr.RadiusY };
                    for (var i = 0; i < sr.GradientStops.Count; i++)
                        wr.GradientStops.Add(new GradientStop());
                    working = wr;
                }
                wr.Center = LerpRelativePoint(sr.Center, er.Center, t);
                // 半径按标量插值，写回时沿用起始笔刷的单位
                wr.RadiusX = new RelativeScalar(sr.RadiusX.Scalar + (er.RadiusX.Scalar - sr.RadiusX.Scalar) * t, sr.RadiusX.Unit);
                wr.RadiusY = new RelativeScalar(sr.RadiusY.Scalar + (er.RadiusY.Scalar - sr.RadiusY.Scalar) * t, sr.RadiusY.Unit);
                // Same shared offset progress as the linear case above.
                var offsets = new BoundedProgress(t, 0d, 1d);
                for (var i = 0; i < sr.GradientStops.Count; i++)
                    offsets.Add(sr.GradientStops[i].Offset, er.GradientStops[i].Offset);

                for (var i = 0; i < sr.GradientStops.Count; i++)
                {
                    wr.GradientStops[i].Color = LerpColor(sr.GradientStops[i].Color, er.GradientStops[i].Color, t);
                    wr.GradientStops[i].Offset = sr.GradientStops[i].Offset + (er.GradientStops[i].Offset - sr.GradientStops[i].Offset) * offsets.Progress;
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
            wb2.Opacity = Math.Max(0, Math.Min(1, startBrush.Opacity + (endBrush.Opacity - startBrush.Opacity) * t));
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

        private static Color LerpColor(Color c1, Color c2, double t)
        {
            // R/G/B share one progress so an overshoot cannot shift the hue; alpha is its own range.
            var rgb = new BoundedProgress(t, 0d, 255d);
            rgb.Add(c1.R, c2.R);
            rgb.Add(c1.G, c2.G);
            rgb.Add(c1.B, c2.B);

            return Color.FromArgb(
                Channel(c1.A + (c2.A - c1.A) * t),
                Channel(rgb.At(c1.R, c2.R)),
                Channel(rgb.At(c1.G, c2.G)),
                Channel(rgb.At(c1.B, c2.B)));
        }

        /// <summary>Saturates instead of wrapping — a bare byte cast turns 300 into 44.</summary>
        private static byte Channel(double value)
        {
            if (value <= 0d) return 0;
            if (value >= 255d) return 255;
            return (byte)value;
        }

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
