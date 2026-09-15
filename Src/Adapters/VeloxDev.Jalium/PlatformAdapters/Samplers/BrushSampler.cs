using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace VeloxDev.Adapters.NativeSamplers
{
    /// <summary>Aligns with the Avalonia adapter's brush sampler, as far as this framework allows:
    /// SolidColorBrush lerps Color AND Opacity; two alike linear gradients interpolate stop by stop, so every frame
    /// is still a gradient; anything else falls back to blending one representative colour from each end, because
    /// this framework has no brush that can composite two layers (see the note on that method). Every branch reuses
    /// a scratch brush, recomputed from the pristine endpoints — start/end are never mutated (shared with the
    /// snapshot).</summary>
    public class BrushSampler : ISampler
    {
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

            var startBr = AdaptStartBrush(start);
            var endBr = end as Brush ?? new SolidColorBrush(Color.Transparent);

            // 一个端点是 null、另一个是纯色 —— 那也还是纯色。
            if (startBr is SolidColorBrush startSolid && endBr is SolidColorBrush endSolid)
            {
                if (working is not SolidColorBrush ws) working = ws = new SolidColorBrush();
                ws.Color = InterpolateColor(startSolid.Color, endSolid.Color, t);
                ws.Opacity = Clamp01(startSolid.Opacity + (endSolid.Opacity - startSolid.Opacity) * t);
                property.SetValue(target, ws);
                return;
            }

            // 两条形状相同的线性渐变：整条便签复用，逐色标就地改写。GradientStop 从 Animatable 派生、带 Changed 事件，
            // 所以就地改写和纯色那条一样会触发重绘。
            if (startBr is LinearGradientBrush startGradient
                && endBr is LinearGradientBrush endGradient
                && startGradient.GradientStops.Count > 0
                && startGradient.GradientStops.Count == endGradient.GradientStops.Count)
            {
                if (working is not LinearGradientBrush wg || wg.GradientStops.Count != startGradient.GradientStops.Count)
                {
                    wg = new LinearGradientBrush();
                    for (var i = 0; i < startGradient.GradientStops.Count; i++) wg.GradientStops.Add(new GradientStop());
                    working = wg;
                }

                wg.StartPoint = LerpPoint(startGradient.StartPoint, endGradient.StartPoint, t);
                wg.EndPoint = LerpPoint(startGradient.EndPoint, endGradient.EndPoint, t);
                for (var i = 0; i < startGradient.GradientStops.Count; i++)
                {
                    wg.GradientStops[i].Color = InterpolateColor(
                        startGradient.GradientStops[i].Color, endGradient.GradientStops[i].Color, t);
                    wg.GradientStops[i].Offset = Lerp(
                        startGradient.GradientStops[i].Offset, endGradient.GradientStops[i].Offset, t);
                }

                property.SetValue(target, wg);
                return;
            }

            // 两端不可比：混成一个代表性纯色。
            //
            // 这里试过两条"真正交叉淡出"的路，在本框架上都不成立：渲染进 RenderTargetBitmap 再包成 ImageBrush ——
            // 那条路的绘制上下文是个桩，只认 SolidColorBrush（渐变什么都不画），PushOpacity 与 DrawImage 都是
            // 占位实现，位图全空；画成 DrawingBrush 交给屏幕渲染器合成 —— 整块不画（实测中间帧那一格 0/9800
            // 像素有内容）。两条路都让中间帧看上去"没有颜色"，而过冲条那一格整块消失是最坏的失败 —— 看不见的
            // 东西无法与"没动"区分。纯色一定画得出来，代价是失去两层叠加的观感。
            if (working is not SolidColorBrush wr) working = wr = new SolidColorBrush();
            wr.Color = InterpolateColor(RepresentativeColor(startBr), RepresentativeColor(endBr), t);
            // 淡出的系数是一个比例，所以它在两端饱和而不是被交给一个越界的值 —— 缓动时间可以越界。
            wr.Opacity = Clamp01(startBr.Opacity + t * (endBr.Opacity - startBr.Opacity));
            property.SetValue(target, wr);
        }

        private static double Clamp01(double value) => Math.Max(0d, Math.Min(1d, value));

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

        private static Brush AdaptStartBrush(object? start)
            => start is Brush brush ? brush : new SolidColorBrush(Color.Transparent);

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        private static Point LerpPoint(Point a, Point b, double t) => new(Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t));

        /// <summary>一种画刷的代表性颜色：纯色取它自己，渐变取最后一个色标，其余视为透明。</summary>
        private static Color RepresentativeColor(Brush brush) => brush switch
        {
            SolidColorBrush solid => solid.Color,
            GradientBrush gradient when gradient.GradientStops.Count > 0 => gradient.GradientStops[^1].Color,
            _ => Color.Transparent,
        };
    }
}
