﻿using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Avalonia.PlatformAdapters
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
                const int renderSize = 100;
                var bmp = new RenderTargetBitmap(new PixelSize(renderSize, renderSize));

                using (var ctx = bmp.CreateDrawingContext())
                {
                    // 绘制底层画刷
                    using (ctx.PushOpacity(1 - t))
                        ctx.DrawRectangle(start, null, new Rect(0, 0, renderSize, renderSize));

                    // 绘制上层画刷
                    using (ctx.PushOpacity(t))
                        ctx.DrawRectangle(end, null, new Rect(0, 0, renderSize, renderSize));
                }

                return new ImageBrush(bmp)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };
            }
        }

        public class TransformInterpolator : IValueInterpolator
        {
            private static readonly Transform Identity = new TransformGroup();

            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                // 1. 统一预处理
                var startTransform = NormalizeInput(start);
                var endTransform = NormalizeInput(end);

                if (steps <= 1) return [endTransform];

                // 2. 解析有效变换
                var startTransforms = ParseTransforms(startTransform);
                var endTransforms = ParseTransforms(endTransform);

                // 3. 创建匹配对
                var transformPairs = CreateTransformPairs(startTransforms, endTransforms);

                // 4. 生成插值序列
                var result = new List<object?>(steps);
                for (int i = 0; i < steps; i++)
                {
                    double t = (double)i / (steps - 1);
                    result.Add(InterpolateTransformPairs(transformPairs, t));
                }

                // 5. 确保首尾精确匹配
                result[0] = startTransform;
                result[steps - 1] = endTransform;
                return result;
            }

            private static Transform NormalizeInput(object? input)
            {
                if (input == null || (input is Transform transform && transform == Identity))
                    return new TransformGroup();
                return (Transform)input;
            }

            private static List<Transform> ParseTransforms(Transform transform)
            {
                var transforms = new List<Transform>();

                if (transform is TransformGroup group && group.Children.Count > 0)
                {
                    var lastOfType = new Dictionary<Type, Transform>();
                    foreach (var child in group.Children)
                    {
                        if (child != Identity)
                            lastOfType[child.GetType()] = child;
                    }
                    transforms.AddRange(lastOfType.Values);
                }
                else if (transform != null && transform != Identity)
                {
                    transforms.Add(transform);
                }

                return transforms;
            }

            private static List<(Transform? start, Transform? end)> CreateTransformPairs(
                List<Transform> startTransforms, List<Transform> endTransforms)
            {
                var allTypes = startTransforms.Select(t => t.GetType())
                                 .Union(endTransforms.Select(t => t.GetType()))
                                 .Distinct()
                                 .ToList();

                var pairs = new List<(Transform?, Transform?)>();
                foreach (var type in allTypes)
                {
                    var start = startTransforms.LastOrDefault(t => t.GetType() == type);
                    var end = endTransforms.LastOrDefault(t => t.GetType() == type);
                    pairs.Add((start, end));
                }
                return pairs;
            }

            private static Transform InterpolateTransformPairs(
                List<(Transform? start, Transform? end)> pairs, double t)
            {
                var interpolatedTransforms = new List<Transform>();
                foreach (var (start, end) in pairs)
                {
                    var interpolated = InterpolateSingleTransformPair(start, end, t);
                    if (interpolated != null)
                        interpolatedTransforms.Add(interpolated);
                }

                return interpolatedTransforms.Count switch
                {
                    0 => new TransformGroup(),
                    1 => interpolatedTransforms[0],
                    _ => new TransformGroup { Children = [.. interpolatedTransforms] }
                };
            }

            private static Transform? InterpolateSingleTransformPair(Transform? start, Transform? end, double t)
            {
                static Transform GetDefaultTransform(Transform? transform) => transform switch
                {
                    TranslateTransform _ => new TranslateTransform(0, 0),
                    RotateTransform _ => new RotateTransform(0, 0, 0),
                    ScaleTransform _ => new ScaleTransform(1, 1),
                    SkewTransform _ => new SkewTransform(0, 0),
                    _ => new MatrixTransform(Matrix.Identity)
                };

                start ??= GetDefaultTransform(end);
                end ??= GetDefaultTransform(start);

                if (start.GetType() != end.GetType())
                {
                    return new MatrixTransform(
                        LerpMatrix(start.Value, end.Value, t));
                }

                return start switch
                {
                    TranslateTransform st when end is TranslateTransform et =>
                        new TranslateTransform(
                            Lerp(st.X, et.X, t),
                            Lerp(st.Y, et.Y, t)),

                    RotateTransform st when end is RotateTransform et =>
                        new RotateTransform(
                            LerpAngle(st.Angle, et.Angle, t),
                            Lerp(st.CenterX, et.CenterX, t),
                            Lerp(st.CenterY, et.CenterY, t)),

                    ScaleTransform st when end is ScaleTransform et =>
                        new ScaleTransform(
                            Lerp(st.ScaleX, et.ScaleX, t),
                            Lerp(st.ScaleY, et.ScaleY, t)),

                    SkewTransform st when end is SkewTransform et =>
                        new SkewTransform(
                            Lerp(st.AngleX, et.AngleX, t),
                            Lerp(st.AngleY, et.AngleY, t)),

                    MatrixTransform st when end is MatrixTransform et =>
                        new MatrixTransform(
                            LerpMatrix(st.Matrix, et.Matrix, t)),

                    _ => null
                };
            }

            private static double Lerp(double a, double b, double t) => a + t * (b - a);
            private static double LerpAngle(double a, double b, double t)
            {
                double delta = ((b - a + 180) % 360 + 360) % 360 - 180;
                return a + t * delta;
            }
            private static Matrix LerpMatrix(Matrix m1, Matrix m2, double t)
            {
                return new Matrix(
                    Lerp(m1.M11, m2.M11, t),
                    Lerp(m1.M12, m2.M12, t),
                    Lerp(m1.M21, m2.M21, t),
                    Lerp(m1.M22, m2.M22, t),
                    Lerp(m1.M31, m2.M31, t),
                    Lerp(m1.M32, m2.M32, t));
            }
        }
    }
}
