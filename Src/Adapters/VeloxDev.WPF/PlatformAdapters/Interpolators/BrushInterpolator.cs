using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VeloxDev.Adapters.NativeInterpolators
{
    public class BrushInterpolator : IValueInterpolator
    {
        private const int RenderSize = 100;

        public List<object?> Interpolate(object? start, object? end, int steps, object? options = null)
        {
            if (steps <= 0)
                return [];

            Brush startBrush = start as Brush ?? Brushes.Transparent;
            Brush endBrush = end as Brush ?? Brushes.Transparent;

            // Single-frame (reset, etc.) returns the raw target value: write null when end is null rather than Brushes.Transparent.
            // Otherwise OpacityMask would be set to a transparent brush, making the whole element invisible.
            if (steps == 1)
                return [end];

            var result = new List<object?>(steps);

            if (startBrush is SolidColorBrush startColor && endBrush is SolidColorBrush endColor)
            {
                for (int i = 0; i < steps; i++)
                {
                    double t = (double)i / (steps - 1);
                    result.Add(InterpolateSolidColorBrush(startColor, endColor, t));
                }
            }
            else
            {
                for (int i = 0; i < steps; i++)
                {
                    double t = (double)i / (steps - 1);
                    result.Add(CreateBlendedBrush(startBrush, endBrush, t));
                }
            }

            result[0] = startBrush;
            result[steps - 1] = endBrush;

            return result;
        }

        private static Brush InterpolateSolidColorBrush(SolidColorBrush start, SolidColorBrush end, double t)
        {
            Color startColor = start.Color;
            Color endColor = end.Color;

            return new SolidColorBrush(Color.FromArgb(
                (byte)(startColor.A + (endColor.A - startColor.A) * t),
                (byte)(startColor.R + (endColor.R - startColor.R) * t),
                (byte)(startColor.G + (endColor.G - startColor.G) * t),
                (byte)(startColor.B + (endColor.B - startColor.B) * t)
            ));
        }

        private static Brush CreateBlendedBrush(Brush start, Brush end, double t)
        {
            var renderTarget = new RenderTargetBitmap(
                RenderSize, RenderSize,
                96, 96,
                PixelFormats.Pbgra32);

            var drawingVisual = new DrawingVisual();
            using (var drawingContext = drawingVisual.RenderOpen())
            {
                // Draw the start brush with semi-transparency.
                drawingContext.PushOpacity(1 - t);
                drawingContext.DrawRectangle(start, null, new Rect(0, 0, RenderSize, RenderSize));
                drawingContext.Pop();

                // Draw the end brush with semi-transparency.
                drawingContext.PushOpacity(t);
                drawingContext.DrawRectangle(end, null, new Rect(0, 0, RenderSize, RenderSize));
                drawingContext.Pop();
            }

            renderTarget.Render(drawingVisual);

            return new ImageBrush(renderTarget)
            {
                Stretch = Stretch.Fill,
                TileMode = TileMode.None
            };
        }
    }
}
