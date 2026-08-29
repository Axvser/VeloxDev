using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class BrushSampler : ISampleable, ISampler
    {
        private const int RenderSize = 100;

        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            if (start is SolidColorBrush startBrush && end is SolidColorBrush endBrush && !startBrush.IsFrozen)
            {
                // 原地修改现有实例，不 new
                startBrush.Color = InterpolateColor(startBrush.Color, endBrush.Color, t);
                return;
            }

            // 冻结/非纯色 → 计算路径（分配）
            Brush startBr = start as Brush ?? Brushes.Transparent;
            Brush endBr = end as Brush ?? Brushes.Transparent;
            if (startBr is SolidColorBrush sc && endBr is SolidColorBrush ec)
                property.SetValue(target, InterpolateSolidColorBrush(sc, ec, t));
            else
                property.SetValue(target, CreateBlendedBrush(startBr, endBr, t));
        }

        private static Color InterpolateColor(Color c1, Color c2, double t) => Color.FromArgb(
            (byte)(c1.A + (c2.A - c1.A) * t),
            (byte)(c1.R + (c2.R - c1.R) * t),
            (byte)(c1.G + (c2.G - c1.G) * t),
            (byte)(c1.B + (c2.B - c1.B) * t));

        private static Brush InterpolateSolidColorBrush(SolidColorBrush start, SolidColorBrush end, double t)
        {
            return new SolidColorBrush(InterpolateColor(start.Color, end.Color, t));
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
