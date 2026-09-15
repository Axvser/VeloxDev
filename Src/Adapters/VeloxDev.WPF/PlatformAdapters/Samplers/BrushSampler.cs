using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class BrushSampler : ISampler
    {
        private const int RenderSize = 100;

        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            if (start is SolidColorBrush sb && end is SolidColorBrush eb)
            {
                // Zero per-frame allocation: reuse a scratch brush, recomputing from the pristine start/end each frame.
                if (working is not SolidColorBrush wb)
                {
                    wb = new SolidColorBrush(sb.Color) { Opacity = sb.Opacity };
                    working = wb;
                }
                wb.Color = InterpolateColor(sb.Color, eb.Color, t);
                wb.Opacity = Math.Max(0, Math.Min(1, sb.Opacity + t * (eb.Opacity - sb.Opacity)));
                property.SetValue(target, wb);
                return;
            }

            // 一个端点是 null、另一个是纯色 —— 那也还是纯色，同样复用便签（start/end 本身从不被改写）。
            var startBr = start as Brush ?? Brushes.Transparent;
            var endBr = end as Brush ?? Brushes.Transparent;
            if (startBr is SolidColorBrush sc && endBr is SolidColorBrush ec)
            {
                if (working is not SolidColorBrush ws)
                {
                    ws = new SolidColorBrush(sc.Color) { Opacity = sc.Opacity };
                    working = ws;
                }
                ws.Color = InterpolateColor(sc.Color, ec.Color, t);
                ws.Opacity = Math.Max(0, Math.Min(1, sc.Opacity + t * (ec.Opacity - sc.Opacity)));
                property.SetValue(target, ws);
                return;
            }

            // 两端不可比 —— 只有这一路是真的交叉淡出。
            if (working is not BlendScratch scratch)
            {
                scratch = new BlendScratch();
                working = scratch;
            }

            // 每帧新出的只有这层壳：位图与 DrawingVisual 都在便签里复用，而把画刷也复用会改掉失效语义。
            property.SetValue(target, new ImageBrush(scratch.Redraw(startBr, endBr, t))
            {
                Stretch = Stretch.Fill,
                TileMode = TileMode.None,
            });
        }

        private static Color InterpolateColor(Color c1, Color c2, double t)
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

        /// <summary>The two endpoints' cross-fade, drawn again into a bitmap that outlives the frame.</summary>
        /// <remarks>
        /// The bitmap and its <see cref="DrawingVisual"/> are the expensive part — 100×100 Pbgra32 is 40 KB of managed
        /// bytes over a native surface of the same size, and this used to run once per frame per animated property.
        /// <c>Render</c> accumulates rather than replaces, so every frame clears first.
        /// </remarks>
        private sealed class BlendScratch
        {
            private readonly RenderTargetBitmap _target =
                new(RenderSize, RenderSize, 96, 96, PixelFormats.Pbgra32);

            private readonly DrawingVisual _visual = new();

            public RenderTargetBitmap Redraw(Brush start, Brush end, double t)
            {
                // The cross-fade factor is a fraction, so it cannot express an overshoot: it saturates at either end
                // instead of being handed a value outside [0,1], which is what the eased time can now be.
                var blend = t <= 0d ? 0d : (t >= 1d ? 1d : t);

                using (var context = _visual.RenderOpen())
                {
                    context.PushOpacity(1 - blend);
                    context.DrawRectangle(start, null, new Rect(0, 0, RenderSize, RenderSize));
                    context.Pop();

                    context.PushOpacity(blend);
                    context.DrawRectangle(end, null, new Rect(0, 0, RenderSize, RenderSize));
                    context.Pop();
                }

                _target.Clear();
                _target.Render(_visual);
                return _target;
            }
        }
    }
}
