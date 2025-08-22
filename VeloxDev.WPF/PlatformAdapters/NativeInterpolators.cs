using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.WPF.PlatformAdapters
{
    public static class NativeInterpolators
    {
        public class DoubleInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                var d1 = (double)(start ?? 0);
                var d2 = (double)(end ?? d1);
                if (steps == 1)
                {
                    return [d2];
                }

                List<object?> result = new(steps);

                var delta = d2 - d1;

                for (var i = 0; i < steps; i++)
                {
                    var t = (double)(i + 1) / steps;
                    result.Add(d1 + t * delta);
                }
                result[0] = start;
                result[steps - 1] = end;

                return result;
            }
        }
        public class BrushInterpolator : IValueInterpolator
        {
            private const int RenderSize = 100;

            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                if (steps <= 0)
                    return [];

                Brush startBrush = start as Brush ?? Brushes.Transparent;
                Brush endBrush = end as Brush ?? Brushes.Transparent;

                if (steps == 1)
                    return [endBrush];

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
                    // 绘制start画刷(带透明度)
                    drawingContext.PushOpacity(1 - t);
                    drawingContext.DrawRectangle(start, null, new Rect(0, 0, RenderSize, RenderSize));
                    drawingContext.Pop();

                    // 绘制end画刷(带透明度)
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
        public class ThicknessInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                var thickness1 = (Thickness)(start ?? new Thickness(0));
                var thickness2 = (Thickness)(end ?? thickness1);
                if (steps == 1)
                {
                    return [thickness2];
                }

                List<object?> result = new(steps);

                for (var i = 0; i < steps; i++)
                {
                    var t = (double)(i + 1) / steps;
                    var left = thickness1.Left + t * (thickness2.Left - thickness1.Left);
                    var top = thickness1.Top + t * (thickness2.Top - thickness1.Top);
                    var right = thickness1.Right + t * (thickness2.Right - thickness1.Right);
                    var bottom = thickness1.Bottom + t * (thickness2.Bottom - thickness1.Bottom);
                    result.Add(new Thickness(left, top, right, bottom));
                }
                result[0] = start;
                result[steps - 1] = end;

                return result;
            }
        }
        public class CornerRadiusInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                var radius1 = (CornerRadius)(start ?? new CornerRadius(0));
                var radius2 = (CornerRadius)(end ?? radius1);
                if (steps == 1)
                {
                    return [radius2];
                }

                List<object?> result = new(steps);

                for (var i = 0; i < steps; i++)
                {
                    var t = (double)(i + 1) / steps;
                    var topLeft = radius1.TopLeft + t * (radius2.TopLeft - radius1.TopLeft);
                    var topRight = radius1.TopRight + t * (radius2.TopRight - radius1.TopRight);
                    var bottomLeft = radius1.BottomLeft + t * (radius2.BottomLeft - radius1.BottomLeft);
                    var bottomRight = radius1.BottomRight + t * (radius2.BottomRight - radius1.BottomRight);
                    result.Add(new CornerRadius(topLeft, topRight, bottomRight, bottomLeft));
                }
                result[0] = start;
                result[steps - 1] = end;

                return result;
            }
        }
        public class TransformInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                var matrix1 = (Transform)(start ?? Transform.Identity);
                var matrix2 = (Transform)(end ?? matrix1);
                if (steps == 1)
                {
                    return [matrix2];
                }

                List<object?> result = new(steps);

                for (int i = 0; i < steps; i++)
                {
                    var t = (double)(i + 1) / steps;

                    double m11 = matrix1.Value.M11 + t * (matrix2.Value.M11 - matrix1.Value.M11);
                    double m12 = matrix1.Value.M12 + t * (matrix2.Value.M12 - matrix1.Value.M12);
                    double m21 = matrix1.Value.M21 + t * (matrix2.Value.M21 - matrix1.Value.M21);
                    double m22 = matrix1.Value.M22 + t * (matrix2.Value.M22 - matrix1.Value.M22);
                    double offsetX = matrix1.Value.OffsetX + t * (matrix2.Value.OffsetX - matrix1.Value.OffsetX);
                    double offsetY = matrix1.Value.OffsetY + t * (matrix2.Value.OffsetY - matrix1.Value.OffsetY);

                    var interpolatedMatrixStr = $"{m11},{m12},{m21},{m22},{offsetX},{offsetY}";
                    var transform = Transform.Parse(interpolatedMatrixStr);
                    result.Add(transform);
                }
                result[0] = start;
                result[steps - 1] = end;

                return result;
            }
        }
        public class PointInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                var point1 = (Point)(start ?? new Point(0, 0));
                var point2 = (Point)(end ?? point1);
                if (steps == 1)
                {
                    return [point2];
                }

                List<object?> result = new(steps);

                for (var i = 0; i < steps; i++)
                {
                    var t = (double)(i + 1) / steps;
                    var x = point1.X + t * (point2.X - point1.X);
                    var y = point1.Y + t * (point2.Y - point1.Y);
                    result.Add(new Point(x, y));
                }
                result[0] = start;
                result[steps - 1] = end;

                return result;
            }
        }
    }
}
