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
                // Zero per-frame allocation: reuse a scratch brush, recomputing its color from the pristine start/end.
                if (st.Scratch is not SolidColorBrush wb)
                {
                    wb = new SolidColorBrush();
                    st.Scratch = wb;
                }
                wb.Color = LerpColor(ss.Color, se.Color, t);
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
                // The offsets are one value: they share a progress so the stops keep their spacing instead of
                // crossing, which would invert the gradient. It stops at [0,1].
                var offsets = new BoundedProgress(t, 0d, 1d);
                for (var i = 0; i < sl.GradientStops.Count; i++)
                    offsets.Add(sl.GradientStops[i].Offset, el.GradientStops[i].Offset);

                for (var i = 0; i < sl.GradientStops.Count; i++)
                {
                    wl.GradientStops[i].Color = LerpColor(sl.GradientStops[i].Color, el.GradientStops[i].Color, t);
                    wl.GradientStops[i].Offset = (float)Lerp(sl.GradientStops[i].Offset, el.GradientStops[i].Offset, offsets.Progress);
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
                    wr = new RadialGradientBrush { Center = sr.Center, Radius = sr.Radius };
                    for (var i = 0; i < sr.GradientStops.Count; i++)
                        wr.GradientStops.Add(new GradientStop());
                    st.Scratch = wr;
                }
                wr.Center = LerpPoint(sr.Center, er.Center, t);
                wr.Radius = (float)Lerp(sr.Radius, er.Radius, t);
                // Same shared offset progress as the linear case above.
                var offsets = new BoundedProgress(t, 0d, 1d);
                for (var i = 0; i < sr.GradientStops.Count; i++)
                    offsets.Add(sr.GradientStops[i].Offset, er.GradientStops[i].Offset);

                for (var i = 0; i < sr.GradientStops.Count; i++)
                {
                    wr.GradientStops[i].Color = LerpColor(sr.GradientStops[i].Color, er.GradientStops[i].Color, t);
                    wr.GradientStops[i].Offset = (float)Lerp(sr.GradientStops[i].Offset, er.GradientStops[i].Offset, offsets.Progress);
                }
                property.SetValue(target, wr);
                return;
            }

            // Mixed types / different stop counts / other brushes → blend to a representative color in a scratch
            // solid brush — zero per-frame framework object allocation.
            var c1 = ExtractRepresentativeColor(s);
            var c2 = ExtractRepresentativeColor(e);
            if (st.Scratch is not SolidColorBrush wb2)
            {
                wb2 = new SolidColorBrush();
                st.Scratch = wb2;
            }
            wb2.Color = LerpColor(c1, c2, t);
            property.SetValue(target, wb2);
        }

        private sealed class NormalizedState
        {
            public Brush Start = null!;
            public Brush End = null!;
            public object? Scratch;
        }

        #region Math helper methods

        private static Brush Normalize(object? obj)
        {
            if (obj is Color c)
                return new SolidColorBrush(c);

            if (obj is Brush b)
                return b;

            return new SolidColorBrush(Colors.Transparent);
        }

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

        private static Color LerpColor(Color start, Color end, double t)
        {
            // R/G/B share one progress so an overshoot cannot shift the hue; alpha is its own range. MAUI's
            // channels are floats, so the range is [0,1].
            var rgb = new BoundedProgress(t, 0f, 1f);
            rgb.Add(start.Red, end.Red);
            rgb.Add(start.Green, end.Green);
            rgb.Add(start.Blue, end.Blue);

            return Color.FromRgba(
                ClampToUnit(rgb.At(start.Red, end.Red)),
                ClampToUnit(rgb.At(start.Green, end.Green)),
                ClampToUnit(rgb.At(start.Blue, end.Blue)),
                ClampToUnit(Lerp(start.Alpha, end.Alpha, t)));
        }

        private static Color ExtractRepresentativeColor(Brush brush)
        {
            if (brush is SolidColorBrush sb)
                return sb.Color;

            if (brush is GradientBrush gb && gb.GradientStops.Count > 0)
                return gb.GradientStops[0].Color;

            return Colors.Transparent;
        }

        #endregion
    }
}
