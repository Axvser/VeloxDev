using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace VeloxDev.Adapters.NativeInterpolators
{
    /// <summary>Aligns with the Avalonia adapter's BrushInterpolator: SolidColorBrush lerps Color AND
    /// Opacity; non-solid or mixed brushes cross-fade by compositing the two brushes at intermediate
    /// opacities into a RenderTargetBitmap wrapped in an ImageBrush.</summary>
    public class BrushInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps, object? options = null)
        {
            var endBrush = end as Brush ?? new SolidColorBrush(Color.Transparent);
            var startBrush = AdaptStartBrush(start);

            if (steps <= 1)
            {
                // Single-frame (reset, etc.) returns the raw target value.
                return [end];
            }

            var result = new List<object?>(steps) { startBrush };
            if (steps > 2)
            {
                for (int i = 1; i < steps - 1; i++)
                {
                    double t = (double)i / (steps - 1);
                    result.Add(InterpolateBrush(startBrush, endBrush, t));
                }
            }

            result.Add(endBrush);
            return result;
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
                Color.FromArgb(
                    (byte)Math.Clamp(start.Color.A + (end.Color.A - start.Color.A) * t, 0, 255),
                    (byte)Math.Clamp(start.Color.R + (end.Color.R - start.Color.R) * t, 0, 255),
                    (byte)Math.Clamp(start.Color.G + (end.Color.G - start.Color.G) * t, 0, 255),
                    (byte)Math.Clamp(start.Color.B + (end.Color.B - start.Color.B) * t, 0, 255)))
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
