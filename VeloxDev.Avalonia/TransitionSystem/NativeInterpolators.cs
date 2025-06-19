using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.Collections.Generic;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Avalonia.TransitionSystem
{
    internal static class NativeInterpolators
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
        public class BrushInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                var endBrush = end as IBrush ?? Brushes.Transparent;
                var startBrush = AdaptStartBrush(start);

                var result = new List<object?>();

                if (steps <= 1)
                {
                    result.Add(endBrush);
                    return result;
                }

                // 确保精确的起始和结束值
                result.Add(startBrush);

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

            private static IBrush AdaptStartBrush(object? start)
            {
                if (start == null)
                {
                    return Brushes.Transparent;
                }

                return (IBrush)start;
            }

            private static IBrush InterpolateBrush(IBrush start, IBrush end, double t)
            {
                if (start is ISolidColorBrush startSolid && end is ISolidColorBrush endSolid)
                {
                    return InterpolateSolidColor(startSolid, endSolid, t);
                }
                else
                {
                    return CrossFadeBrushes(start, end, t);
                }
            }

            private static ISolidColorBrush InterpolateSolidColor(ISolidColorBrush start, ISolidColorBrush end, double t)
            {
                return new SolidColorBrush(
                    Color.FromArgb(
                        (byte)(start.Color.A + (end.Color.A - start.Color.A) * t),
                        (byte)(start.Color.R + (end.Color.R - start.Color.R) * t),
                        (byte)(start.Color.G + (end.Color.G - start.Color.G) * t),
                        (byte)(start.Color.B + (end.Color.B - start.Color.B) * t)))
                {
                    Opacity = start.Opacity + (end.Opacity - start.Opacity) * t
                };
            }

            private static IBrush CrossFadeBrushes(IBrush start, IBrush end, double t)
            {
                if (t <= 0.0) return start;
                if (t >= 1.0) return end;

                // 使用RenderTargetBitmap实现精确混合
                return CreateBlendedBrush(start, end, t);
            }

            private static IBrush CreateBlendedBrush(IBrush start, IBrush end, double t)
            {
                const int renderSize = 100; // 可根据需要调整

                var bmp = new RenderTargetBitmap(new PixelSize(renderSize, renderSize));
                using (var ctx = bmp.CreateDrawingContext())
                {
                    // 先绘制start画刷（带透明度）
                    using (ctx.PushOpacity(1 - t))
                    {
                        ctx.DrawRectangle(start, null, new Rect(0, 0, renderSize, renderSize));
                    }

                    // 再绘制end画刷（带透明度）
                    using (ctx.PushOpacity(t))
                    {
                        ctx.DrawRectangle(end, null, new Rect(0, 0, renderSize, renderSize));
                    }
                }
                return new ImageBrush(bmp);
            }
        }
        public class TransformInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                var startTransform = (start as Transform) ?? new MatrixTransform();
                var endTransform = (end as Transform) ?? new MatrixTransform();

                var result = new List<object?>();

                if (steps <= 1)
                {
                    result.Add(endTransform);
                    return result;
                }

                var startMatrix = startTransform.Value;
                var endMatrix = endTransform.Value;

                for (int i = 0; i < steps; i++)
                {
                    double t = (double)i / (steps - 1);

                    // 正确的Matrix构造函数参数顺序
                    var interpolated = new Matrix(
                        Lerp(startMatrix.M11, endMatrix.M11, t),  // M11
                        Lerp(startMatrix.M12, endMatrix.M12, t),  // M12
                        Lerp(startMatrix.M21, endMatrix.M21, t),  // M21
                        Lerp(startMatrix.M22, endMatrix.M22, t),  // M22
                        Lerp(startMatrix.M31, endMatrix.M31, t),  // M31 (OffsetX)
                        Lerp(startMatrix.M32, endMatrix.M32, t)   // M32 (OffsetY)
                    );

                    result.Add(new MatrixTransform(interpolated));
                }

                return result;
            }

            private static double Lerp(double a, double b, double t)
            {
                return a + (b - a) * t;
            }
        }
    }
}
