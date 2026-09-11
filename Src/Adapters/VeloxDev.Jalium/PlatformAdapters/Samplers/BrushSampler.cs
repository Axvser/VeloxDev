using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace VeloxDev.Adapters.NativeSamplers
{
    /// <summary>Aligns with the Avalonia adapter's BrushInterpolator: SolidColorBrush lerps Color AND
    /// Opacity; non-solid or mixed brushes cross-fade by compositing into a RenderTargetBitmap wrapped in an
    /// ImageBrush. Middle frames always allocate a fresh brush — start/end are never mutated (shared with the
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

            return CrossFadeBrushes(start, end, t);
        }

        private static SolidColorBrush InterpolateSolidColor(SolidColorBrush start, SolidColorBrush end, double t)
        {
            return new SolidColorBrush(
                InterpolateColor(start.Color, end.Color, t))
            {
                Opacity = Math.Max(0, Math.Min(1, start.Opacity + (end.Opacity - start.Opacity) * t)),
            };
        }

        private static Brush CrossFadeBrushes(Brush start, Brush end, double t)
        {
            if (t <= 0.0) return start;
            if (t >= 1.0) return end;

            const int size = 64;
            var grid = new Grid { Width = size, Height = size };
            grid.Children.Add(new Border { Background = start, Opacity = 1 - t });
            grid.Children.Add(new Border { Background = end, Opacity = t });

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormat.Bgra32);
            bitmap.Render(grid);
            return new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
        }
    }
}
