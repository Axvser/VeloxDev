using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace VeloxDev.Adapters.NativeSamplers
{
    /// <summary>Aligns with the Avalonia adapter's brush sampler, as far as this framework allows:
    /// SolidColorBrush lerps Color AND Opacity; two alike linear gradients interpolate stop by stop, so every frame
    /// is still a gradient; anything else falls back to blending one representative colour from each end, because
    /// this framework has no brush that can composite two layers (see the note on that method). Middle frames always
    /// allocate a fresh brush — start/end are never mutated (shared with the snapshot).</summary>
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

            // Non-solid / null → allocate a fresh brush (start/end are never mutated).
            var startBr = AdaptStartBrush(start);
            var endBr = end as Brush ?? new SolidColorBrush(Color.Transparent);
            property.SetValue(target, InterpolateBrush(startBr, endBr, t));
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

        private static Brush AdaptStartBrush(object? start)
            => start is Brush brush ? brush : new SolidColorBrush(Color.Transparent);

        private static Brush InterpolateBrush(Brush start, Brush end, double t)
        {
            if (start is SolidColorBrush startSolid && end is SolidColorBrush endSolid)
            {
                return InterpolateSolidColor(startSolid, endSolid, t);
            }

            // 两个渐变之间：逐色标插值，产出的**仍然是一条渐变**。这是唯一能让每一帧都还是渐变的做法 ——
            // 把两端各压成一个代表色再插值，会让动画从第一帧起就变成一块平的纯色。
            if (start is LinearGradientBrush startGradient
                && end is LinearGradientBrush endGradient
                && InterpolateGradient(startGradient, endGradient, t) is { } gradient)
            {
                return gradient;
            }

            return BlendToRepresentativeColor(start, end, t);
        }

        /// <summary>
        /// 两条形状相同的线性渐变之间逐色标插值；形状不同（色标数不一样、或不是线性渐变）时返回 null，
        /// 由调用方退回到代表性颜色。
        /// </summary>
        private static LinearGradientBrush? InterpolateGradient(LinearGradientBrush start, LinearGradientBrush end, double t)
        {
            var count = start.GradientStops.Count;
            if (count == 0 || count != end.GradientStops.Count) return null;

            var stops = new GradientStopCollection(count);
            for (var i = 0; i < count; i++)
            {
                var from = start.GradientStops[i];
                var to = end.GradientStops[i];
                stops.Add(new GradientStop(InterpolateColor(from.Color, to.Color, t), Lerp(from.Offset, to.Offset, t)));
            }

            return new LinearGradientBrush(
                stops,
                LerpPoint(start.StartPoint, end.StartPoint, t),
                LerpPoint(start.EndPoint, end.EndPoint, t));
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        private static Point LerpPoint(Point a, Point b, double t) => new(Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t));

        private static SolidColorBrush InterpolateSolidColor(SolidColorBrush start, SolidColorBrush end, double t)
        {
            return new SolidColorBrush(
                InterpolateColor(start.Color, end.Color, t))
            {
                Opacity = Math.Max(0, Math.Min(1, start.Opacity + (end.Opacity - start.Opacity) * t)),
            };
        }

        /// <summary>
        /// 两种画刷无法逐色标插值时的退路：取两端的代表性颜色，混成一个纯色。
        /// </summary>
        /// <remarks>
        /// <b>这里试过两条"真正交叉淡出"的路，在本框架上都不成立</b>，所以最终与 Avalonia / WinUI 的同一个采样器
        /// 取了同样的做法：
        /// <list type="bullet">
        /// <item>渲染进 <c>RenderTargetBitmap</c> 再包成 <c>ImageBrush</c> —— 那条路的绘制上下文是个桩，只认
        /// <c>SolidColorBrush</c>（渐变什么都不画），<c>PushOpacity</c> 与 <c>DrawImage</c> 都是占位实现，
        /// 位图全空。</item>
        /// <item>画成 <c>DrawingBrush</c> 交给屏幕渲染器合成 —— 整块不画（实测中间帧那一格 0/9800 像素有内容）。</item>
        /// </list>
        /// 两条路都会让中间帧看上去"没有颜色"：过冲条那一格整块消失，而这正是最坏的失败 —— 看不见的东西
        /// 无法与"没动"区分。纯色一定画得出来，代价是失去两层叠加的观感。
        /// </remarks>
        private static SolidColorBrush BlendToRepresentativeColor(Brush start, Brush end, double t)
        {
            return new SolidColorBrush(InterpolateColor(RepresentativeColor(start), RepresentativeColor(end), t))
            {
                // 淡出的系数是一个比例，所以它在两端饱和而不是被交给一个越界的值 —— 缓动时间可以越界。
                Opacity = Math.Max(0d, Math.Min(1d, start.Opacity + t * (end.Opacity - start.Opacity))),
            };
        }

        /// <summary>一种画刷的代表性颜色：纯色取它自己，渐变取最后一个色标，其余视为透明。</summary>
        private static Color RepresentativeColor(Brush brush) => brush switch
        {
            SolidColorBrush solid => solid.Color,
            GradientBrush gradient when gradient.GradientStops.Count > 0 => gradient.GradientStops[^1].Color,
            _ => Color.Transparent,
        };
    }
}
