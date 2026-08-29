using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace VeloxDev.Adapters.NativeSamplers
{
    /// <summary>Aligns with the Avalonia adapter's BrushInterpolator: SolidColorBrush lerps Color AND
    /// Opacity; non-solid or mixed brushes cross-fade by compositing the two brushes at intermediate
    /// opacities into a RenderTargetBitmap wrapped in an ImageBrush. Middle frames mutate a live,
    /// unfrozen SolidColorBrush in place (no per-frame allocation); anything else falls back to the
    /// compute path.</summary>
    public class BrushSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            if (start is SolidColorBrush startBrush && end is SolidColorBrush endBrush && !startBrush.IsFrozen)
            {
                // 原地修改现有实例，不 new
                startBrush.Color = InterpolateColor(startBrush.Color, endBrush.Color, t);
                startBrush.Opacity = startBrush.Opacity + (endBrush.Opacity - startBrush.Opacity) * t;
                return;
            }

            // 冻结/非纯色 → 计算路径（分配）
            var startBr = AdaptStartBrush(start);
            var endBr = end as Brush ?? new SolidColorBrush(Color.Transparent);
            property.SetValue(target, InterpolateBrush(startBr, endBr, t));
        }

        private static Color InterpolateColor(Color c1, Color c2, double t) => Color.FromArgb(
            (byte)Math.Clamp(c1.A + (c2.A - c1.A) * t, 0, 255),
            (byte)Math.Clamp(c1.R + (c2.R - c1.R) * t, 0, 255),
            (byte)Math.Clamp(c1.G + (c2.G - c1.G) * t, 0, 255),
            (byte)Math.Clamp(c1.B + (c2.B - c1.B) * t, 0, 255));

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
                Opacity = start.Opacity + (end.Opacity - start.Opacity) * t,
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
