namespace VeloxDev.Adapters.NativeSamplers
{
    public class ShadowSampler : ISampler
    {
        private static readonly Brush TransparentBrush = new SolidColorBrush(Colors.Transparent);

        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            var s1 = start as Shadow;
            var s2 = end as Shadow ?? s1;
            var brush1 = s1?.Brush ?? TransparentBrush;
            var brush2 = s2?.Brush ?? TransparentBrush;

            var x1 = s1?.Offset.X ?? 0f; var y1 = s1?.Offset.Y ?? 0f;
            var x2 = s2?.Offset.X ?? 0f; var y2 = s2?.Offset.Y ?? 0f;
            var r1 = s1?.Radius ?? 0f; var r2 = s2?.Radius ?? 0f;
            var o1 = s1?.Opacity ?? 0f; var o2 = s2?.Opacity ?? 0f;

            // The radius has its own range and stops at zero — a negative radius is not a shadow. The offset is
            // unbounded, and opacity keeps its own [0,1] clamp below.
            var radius = new BoundedProgress(t, 0f, double.PositiveInfinity);
            radius.Add(r1, r2);

            // Zero per-frame allocation: reuse a scratch Shadow, recomputing its fields from the pristine start/end.
            if (working is not Shadow ws)
            {
                ws = new Shadow();
                working = ws;
            }
            ws.Offset = new Point(x1 + (x2 - x1) * (float)t, y1 + (y2 - y1) * (float)t);
            ws.Radius = (float)radius.At(r1, r2);
            ws.Opacity = Math.Max(0, Math.Min(1, o1 + (o2 - o1) * (float)t));
            ws.Brush = t >= 0.5 ? brush2 : brush1; // Simple transition handling.
            property.SetValue(target, ws);
        }
    }
}
