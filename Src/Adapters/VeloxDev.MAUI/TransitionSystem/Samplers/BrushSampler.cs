namespace VeloxDev.Adapters.NativeSamplers
{
    public class BrushSampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            // 每段动画只归一化一次 start/end（Color/null 输入不该每帧分配画刷）。
            if (working is not NormalizedState st)
            {
                st = new NormalizedState { Start = Normalize(start), End = Normalize(end) };
                working = st;
            }
            var s = st.Start;
            var e = st.End;

            if (s is SolidColorBrush ss && e is SolidColorBrush se)
            {
                // 每帧零分配：复用同一个 scratch 画刷，每帧从原始 start/end 重算颜色。
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
                // 每帧零分配：复用同一个 scratch 线性渐变，每帧从原始 start/end 重算色标。
                if (st.Scratch is not LinearGradientBrush wl || wl.GradientStops.Count != sl.GradientStops.Count)
                {
                    wl = new LinearGradientBrush { StartPoint = sl.StartPoint, EndPoint = sl.EndPoint };
                    for (var i = 0; i < sl.GradientStops.Count; i++)
                        wl.GradientStops.Add(new GradientStop());
                    st.Scratch = wl;
                }
                wl.StartPoint = LerpPoint(sl.StartPoint, el.StartPoint, t);
                wl.EndPoint = LerpPoint(sl.EndPoint, el.EndPoint, t);
                // 偏移是同一个值：共用进度，色标保持间距而不会交叉（交叉会反转渐变）；到 [0,1] 为止。
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
                // 每帧零分配：复用同一个 scratch 径向渐变，每帧从原始 start/end 重算色标。
                if (st.Scratch is not RadialGradientBrush wr || wr.GradientStops.Count != sr.GradientStops.Count)
                {
                    wr = new RadialGradientBrush { Center = sr.Center, Radius = sr.Radius };
                    for (var i = 0; i < sr.GradientStops.Count; i++)
                        wr.GradientStops.Add(new GradientStop());
                    st.Scratch = wr;
                }
                wr.Center = LerpPoint(sr.Center, er.Center, t);
                wr.Radius = (float)Lerp(sr.Radius, er.Radius, t);
                // 与上面线性同样的共用偏移进度。
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

            // 异型/色标数不同/其它画刷 → 混成代表性纯色写进 scratch 画刷，每帧零框架对象分配。
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
            // R/G/B 共用同一进度，越界时不会偏色；alpha 走自己的范围。MAUI 通道是 float，范围 [0,1]。
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
