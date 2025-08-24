using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using VeloxDev.Core.Interfaces.TransitionSystem;
using Windows.Foundation;
using Windows.UI;

namespace VeloxDev.WinUI.PlatformAdapters
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

                for (int i = 0; i < steps; i++)
                {
                    double t = (double)(i + 1) / steps;
                    result.Add(d1 + t * delta);
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
                if (steps <= 0)
                    return [];

                Brush startBrush = start as Brush ?? new SolidColorBrush(Colors.Transparent);
                Brush endBrush = end as Brush ?? new SolidColorBrush(Colors.Transparent);

                if (steps == 1)
                    return [endBrush];

                var result = new List<object?>(steps);

                // 检查两个画刷是否都是纯色
                bool bothSolid = startBrush is SolidColorBrush && endBrush is SolidColorBrush;

                if (bothSolid)
                {
                    // 纯色画刷 - 使用 RGBA 插值
                    var startColor = ((SolidColorBrush)startBrush).Color;
                    var endColor = ((SolidColorBrush)endBrush).Color;

                    for (int i = 0; i < steps; i++)
                    {
                        double t = (double)i / (steps - 1);

                        byte a = (byte)(startColor.A + (endColor.A - startColor.A) * t);
                        byte r = (byte)(startColor.R + (endColor.R - startColor.R) * t);
                        byte g = (byte)(startColor.G + (endColor.G - startColor.G) * t);
                        byte b = (byte)(startColor.B + (endColor.B - startColor.B) * t);

                        result.Add(new SolidColorBrush(Color.FromArgb(a, r, g, b)));
                    }
                }
                else
                {
                    // 非纯色画刷 - 使用透明度混合
                    for (int i = 0; i < steps; i++)
                    {
                        double t = (double)i / (steps - 1);
                        result.Add(CreateBlendedBrush(startBrush, endBrush, t));
                    }
                }

                // 确保第一帧和最后一帧完全匹配输入
                result[0] = startBrush;
                result[steps - 1] = endBrush;

                return result;
            }

            // 透明度混合方法
            private static ImageBrush CreateBlendedBrush(Brush start, Brush end, double t)
            {
                // 创建容器面板
                var container = new Grid
                {
                    Width = 100,
                    Height = 100
                };

                // 添加起始画刷层
                var startLayer = new Rectangle
                {
                    Fill = start,
                    Opacity = 1 - t
                };
                container.Children.Add(startLayer);

                // 添加结束画刷层
                var endLayer = new Rectangle
                {
                    Fill = end,
                    Opacity = t
                };
                container.Children.Add(endLayer);

                // 创建渲染目标
                var renderTarget = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();

                // 返回图像画刷
                return new ImageBrush
                {
                    ImageSource = renderTarget,
                    Stretch = Stretch.Fill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
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

                for (int i = 0; i < steps; i++)
                {
                    double t = (double)(i + 1) / steps;
                    result.Add(new Thickness(
                        thickness1.Left + t * (thickness2.Left - thickness1.Left),
                        thickness1.Top + t * (thickness2.Top - thickness1.Top),
                        thickness1.Right + t * (thickness2.Right - thickness1.Right),
                        thickness1.Bottom + t * (thickness2.Bottom - thickness1.Bottom)
                    ));
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

                for (int i = 0; i < steps; i++)
                {
                    double t = (double)(i + 1) / steps;
                    result.Add(new CornerRadius(
                        radius1.TopLeft + t * (radius2.TopLeft - radius1.TopLeft),
                        radius1.TopRight + t * (radius2.TopRight - radius1.TopRight),
                        radius1.BottomRight + t * (radius2.BottomRight - radius1.BottomRight),
                        radius1.BottomLeft + t * (radius2.BottomLeft - radius1.BottomLeft)
                    ));
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
                var transform1 = start as Transform ?? new MatrixTransform();
                var transform2 = end as Transform ?? transform1;

                if (steps == 1)
                {
                    return [transform2];
                }

                // 获取矩阵值
                Matrix matrix1 = GetMatrix(transform1);
                Matrix matrix2 = GetMatrix(transform2);

                List<object?> result = new(steps);

                for (int i = 0; i < steps; i++)
                {
                    double t = (double)(i + 1) / steps;

                    // 插值每个分量
                    double m11 = matrix1.M11 + t * (matrix2.M11 - matrix1.M11);
                    double m12 = matrix1.M12 + t * (matrix2.M12 - matrix1.M12);
                    double m21 = matrix1.M21 + t * (matrix2.M21 - matrix1.M21);
                    double m22 = matrix1.M22 + t * (matrix2.M22 - matrix1.M22);
                    double offsetX = matrix1.OffsetX + t * (matrix2.OffsetX - matrix1.OffsetX);
                    double offsetY = matrix1.OffsetY + t * (matrix2.OffsetY - matrix1.OffsetY);

                    // 创建新的 MatrixTransform
                    var matrix = new Matrix(m11, m12, m21, m22, offsetX, offsetY);
                    result.Add(new MatrixTransform { Matrix = matrix });
                }

                result[0] = start;
                result[steps - 1] = end;

                return result;
            }

            // 从任意 Transform 类型提取矩阵值
            private static Matrix GetMatrix(Transform transform)
            {
                return transform switch
                {
                    MatrixTransform matrixTransform => matrixTransform.Matrix,
                    RotateTransform rotateTransform => new Matrix(
                        Math.Cos(rotateTransform.Angle * Math.PI / 180),
                        Math.Sin(rotateTransform.Angle * Math.PI / 180),
                        -Math.Sin(rotateTransform.Angle * Math.PI / 180),
                        Math.Cos(rotateTransform.Angle * Math.PI / 180),
                        rotateTransform.CenterX,
                        rotateTransform.CenterY
                    ),
                    ScaleTransform scaleTransform => new Matrix(
                        scaleTransform.ScaleX,
                        0,
                        0,
                        scaleTransform.ScaleY,
                        scaleTransform.CenterX,
                        scaleTransform.CenterY
                    ),
                    SkewTransform skewTransform => new Matrix(
                        1,
                        Math.Tan(skewTransform.AngleY * Math.PI / 180),
                        Math.Tan(skewTransform.AngleX * Math.PI / 180),
                        1,
                        skewTransform.CenterX,
                        skewTransform.CenterY
                    ),
                    TranslateTransform translateTransform => new Matrix(
                        1, 0, 0, 1,
                        translateTransform.X,
                        translateTransform.Y
                    ),
                    TransformGroup group => CombineMatrices(group),
                    _ => Matrix.Identity
                };
            }

            // 合并 TransformGroup 中的所有变换
            private static Matrix CombineMatrices(TransformGroup group)
            {
                var matrix = Matrix.Identity;
                foreach (var transform in group.Children)
                {
                    var m = GetMatrix(transform);
                    matrix = MultiplyMatrices(matrix, m);
                }
                return matrix;
            }

            // 矩阵乘法
            private static Matrix MultiplyMatrices(Matrix a, Matrix b)
            {
                return new Matrix(
                    a.M11 * b.M11 + a.M12 * b.M21,
                    a.M11 * b.M12 + a.M12 * b.M22,
                    a.M21 * b.M11 + a.M22 * b.M21,
                    a.M21 * b.M12 + a.M22 * b.M22,
                    a.OffsetX * b.M11 + a.OffsetY * b.M21 + b.OffsetX,
                    a.OffsetX * b.M12 + a.OffsetY * b.M22 + b.OffsetY
                );
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

                for (int i = 0; i < steps; i++)
                {
                    double t = (double)(i + 1) / steps;
                    result.Add(new Point(
                        point1.X + t * (point2.X - point1.X),
                        point1.Y + t * (point2.Y - point1.Y)
                    ));
                }

                result[0] = start;
                result[steps - 1] = end;

                return result;
            }
        }
    }
}