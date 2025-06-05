using System.Reflection;
using System.Windows;
using System.Windows.Media;
using VeloxDev.WPF.FrameworkSupport;
using VeloxDev.WPF.StructuralDesign.Animator;
using VeloxDev.WPF.Tools.SolidColor;

namespace VeloxDev.WPF.TransitionSystem.Basic
{
    public static class LinearInterpolation
    {
        public static List<List<Tuple<PropertyInfo, List<object?>>>> ComputingFrames(Type type, State state, object target, int framecount)
        {
            List<List<Tuple<PropertyInfo, List<object?>>>> result = new(7);
            result.Add(DoubleComputing(type, state, target, framecount));
            result.Add(BrushComputing(type, state, target, framecount));
            result.Add(TransformComputing(type, state, target, framecount));
            result.Add(PointComputing(type, state, target, framecount));
            result.Add(CornerRadiusComputing(type, state, target, framecount));
            result.Add(ThicknessComputing(type, state, target, framecount));
            result.Add(ILinearInterpolationComputing(type, state, target, framecount));
            return result;
        }

        public static List<object?> DoubleComputing(object? start, object? end, int steps)
        {
            var d1 = (double)(start ?? 0);
            var d2 = (double)(end ?? d1);

            List<object?> result = new(steps);

            var delta = d2 - d1;

            if (steps == 0)
            {
                result.Add(end);
                return result;
            }

            for (var i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                result.Add(d1 + t * delta);
            }
            if (steps > 1) result[0] = start;
            result[steps - 1] = end;

            return result;
        }
        public static List<object?> TransformComputing(object? start, object? end, int steps)
        {
            var matrix1 = (Transform)(start ?? Transform.Identity);
            var matrix2 = (Transform)(end ?? matrix1);

            List<object?> result = new(steps);

            if (steps == 0)
            {
                result.Add(end);
                return result;
            }

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
            if (steps > 1) result[0] = start;
            result[steps - 1] = end;

            return result;
        }
        public static List<object?> PointComputing(object? start, object? end, int steps)
        {
            var point1 = (Point)(start ?? new Point(0, 0));
            var point2 = (Point)(end ?? point1);

            List<object?> result = new(steps);

            if (steps == 0)
            {
                result.Add(end);
                return result;
            }

            for (var i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                var x = point1.X + t * (point2.X - point1.X);
                var y = point1.Y + t * (point2.Y - point1.Y);
                result.Add(new Point(x, y));
            }
            if (steps > 1) result[0] = start;
            result[steps - 1] = end;

            return result;
        }
        public static List<object?> ThicknessComputing(object? start, object? end, int steps)
        {
            var thickness1 = (Thickness)(start ?? new Thickness(0));
            var thickness2 = (Thickness)(end ?? thickness1);

            List<object?> result = new(steps);

            if (steps == 0)
            {
                result.Add(end);
                return result;
            }

            for (var i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                var left = thickness1.Left + t * (thickness2.Left - thickness1.Left);
                var top = thickness1.Top + t * (thickness2.Top - thickness1.Top);
                var right = thickness1.Right + t * (thickness2.Right - thickness1.Right);
                var bottom = thickness1.Bottom + t * (thickness2.Bottom - thickness1.Bottom);
                result.Add(new Thickness(left, top, right, bottom));
            }
            if (steps > 1) result[0] = start;
            result[steps - 1] = end;

            return result;
        }
        public static List<object?> CornerRadiusComputing(object? start, object? end, int steps)
        {
            var radius1 = (CornerRadius)(start ?? new CornerRadius(0));
            var radius2 = (CornerRadius)(end ?? radius1);

            List<object?> result = new(steps);

            if (steps == 0)
            {
                result.Add(end);
                return result;
            }

            for (var i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                var topLeft = radius1.TopLeft + t * (radius2.TopLeft - radius1.TopLeft);
                var topRight = radius1.TopRight + t * (radius2.TopRight - radius1.TopRight);
                var bottomLeft = radius1.BottomLeft + t * (radius2.BottomLeft - radius1.BottomLeft);
                var bottomRight = radius1.BottomRight + t * (radius2.BottomRight - radius1.BottomRight);
                result.Add(new CornerRadius(topLeft, topRight, bottomRight, bottomLeft));
            }
            if (steps > 1) result[0] = start;
            result[steps - 1] = end;

            return result;
        }
        public static List<object?> BrushComputing(object? start, object? end, int steps)
        {
            object startBrush = start ?? new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            object endBrush = end ?? new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            return (startBrush.GetType().Name, endBrush.GetType().Name) switch
            {
                (nameof(SolidColorBrush), nameof(SolidColorBrush)) => InterpolateSolidColorBrush((SolidColorBrush)startBrush, (SolidColorBrush)endBrush, steps),
                _ => InterpolateBrushOpacity(startBrush as Brush ?? Brushes.Transparent, endBrush as Brush ?? Brushes.Transparent, steps, endBrush)
            };
        }

        public static List<Tuple<PropertyInfo, List<object?>>> DoubleComputing(Type type, State state, object TransitionApplied, int FrameCount)
        {
            List<Tuple<PropertyInfo, List<object?>>> allFrames = new(FrameCount);
            if (TransitionScheduler.SplitedPropertyInfos.TryGetValue(type, out var infodictionary))
            {
                foreach (var propertyname in state.Values.Keys)
                {
                    if (infodictionary.Item1.TryGetValue(propertyname, out var propertyinfo))
                    {
                        var currentValue = propertyinfo.GetValue(TransitionApplied);
                        var newValue = state.Values[propertyname];
                        if (!currentValue?.Equals(newValue) ?? true)
                        {
                            allFrames.Add(Tuple.Create(propertyinfo, state.Calculators.TryGetValue(propertyname, out var calculator) ? calculator.Invoke(currentValue, newValue, FrameCount) : LinearInterpolation.DoubleComputing(currentValue, newValue, FrameCount)));
                        }
                    }
                }
            }
            return allFrames;
        }
        public static List<Tuple<PropertyInfo, List<object?>>> BrushComputing(Type type, State state, object TransitionApplied, int FrameCount)
        {
            List<Tuple<PropertyInfo, List<object?>>> allFrames = new(FrameCount);
            if (TransitionScheduler.SplitedPropertyInfos.TryGetValue(type, out var infodictionary))
            {
                foreach (var propertyname in state.Values.Keys)
                {
                    if (infodictionary.Item2.TryGetValue(propertyname, out var propertyinfo))
                    {
                        var currentValue = propertyinfo.GetValue(TransitionApplied);
                        var newValue = state.Values[propertyname];
                        if (!currentValue?.Equals(newValue) ?? true)
                        {
                            allFrames.Add(Tuple.Create(propertyinfo, state.Calculators.TryGetValue(propertyname, out var calculator) ? calculator.Invoke(currentValue, newValue, FrameCount) : LinearInterpolation.BrushComputing(currentValue, newValue, FrameCount)));
                        }
                    }
                }
            }
            return allFrames;
        }
        public static List<Tuple<PropertyInfo, List<object?>>> TransformComputing(Type type, State state, object TransitionApplied, int FrameCount)
        {
            List<Tuple<PropertyInfo, List<object?>>> allFrames = new(FrameCount);
            if (TransitionScheduler.SplitedPropertyInfos.TryGetValue(type, out var infodictionary))
            {
                foreach (var propertyname in state.Values.Keys)
                {
                    if (infodictionary.Item3.TryGetValue(propertyname, out var propertyinfo))
                    {
                        var currentValue = propertyinfo.GetValue(TransitionApplied);
                        var newValue = state.Values[propertyname];
                        if (!currentValue?.Equals(newValue) ?? true)
                        {
                            allFrames.Add(Tuple.Create(propertyinfo, state.Calculators.TryGetValue(propertyname, out var calculator) ? calculator.Invoke(currentValue, newValue, FrameCount) : LinearInterpolation.TransformComputing(currentValue, newValue, FrameCount)));
                        }
                    }
                }
            }
            return allFrames;
        }
        public static List<Tuple<PropertyInfo, List<object?>>> PointComputing(Type type, State state, object TransitionApplied, int FrameCount)
        {
            List<Tuple<PropertyInfo, List<object?>>> allFrames = new(FrameCount);
            if (TransitionScheduler.SplitedPropertyInfos.TryGetValue(type, out var infodictionary))
            {
                foreach (var propertyname in state.Values.Keys)
                {
                    if (infodictionary.Item4.TryGetValue(propertyname, out var propertyinfo))
                    {
                        var currentValue = propertyinfo.GetValue(TransitionApplied);
                        var newValue = state.Values[propertyname];
                        if (!currentValue?.Equals(newValue) ?? true)
                        {
                            allFrames.Add(Tuple.Create(propertyinfo, state.Calculators.TryGetValue(propertyname, out var calculator) ? calculator.Invoke(currentValue, newValue, FrameCount) : LinearInterpolation.PointComputing(currentValue, newValue, FrameCount)));
                        }
                    }
                }
            }
            return allFrames;
        }
        public static List<Tuple<PropertyInfo, List<object?>>> CornerRadiusComputing(Type type, State state, object TransitionApplied, int FrameCount)
        {
            List<Tuple<PropertyInfo, List<object?>>> allFrames = new(FrameCount);
            if (TransitionScheduler.SplitedPropertyInfos.TryGetValue(type, out var infodictionary))
            {
                foreach (var propertyname in state.Values.Keys)
                {
                    if (infodictionary.Item5.TryGetValue(propertyname, out var propertyinfo))
                    {
                        var currentValue = propertyinfo.GetValue(TransitionApplied);
                        var newValue = state.Values[propertyname];
                        if (!currentValue?.Equals(newValue) ?? true)
                        {
                            allFrames.Add(Tuple.Create(propertyinfo, state.Calculators.TryGetValue(propertyname, out var calculator) ? calculator.Invoke(currentValue, newValue, FrameCount) : LinearInterpolation.CornerRadiusComputing(currentValue, newValue, FrameCount)));
                        }
                    }
                }
            }
            return allFrames;
        }
        public static List<Tuple<PropertyInfo, List<object?>>> ThicknessComputing(Type type, State state, object TransitionApplied, int FrameCount)
        {
            List<Tuple<PropertyInfo, List<object?>>> allFrames = new(FrameCount);
            if (TransitionScheduler.SplitedPropertyInfos.TryGetValue(type, out var infodictionary))
            {
                foreach (var propertyname in state.Values.Keys)
                {
                    if (infodictionary.Item6.TryGetValue(propertyname, out var propertyinfo))
                    {
                        var currentValue = propertyinfo.GetValue(TransitionApplied);
                        var newValue = state.Values[propertyname];
                        if (!currentValue?.Equals(newValue) ?? true)
                        {
                            allFrames.Add(Tuple.Create(propertyinfo, state.Calculators.TryGetValue(propertyname, out var calculator) ? calculator.Invoke(currentValue, newValue, FrameCount) : LinearInterpolation.ThicknessComputing(currentValue, newValue, FrameCount)));
                        }
                    }
                }
            }
            return allFrames;
        }
        public static List<Tuple<PropertyInfo, List<object?>>> ILinearInterpolationComputing(Type type, State state, object TransitionApplied, int FrameCount)
        {
            List<Tuple<PropertyInfo, List<object?>>> allFrames = new(FrameCount);
            if (TransitionScheduler.SplitedPropertyInfos.TryGetValue(type, out var infodictionary))
            {
                foreach (var propertyname in state.Values.Keys)
                {
                    if (infodictionary.Item7.TryGetValue(propertyname, out var propertyinfo))
                    {
                        var currentValue = propertyinfo.GetValue(TransitionApplied);
                        var newValue = state.Values[propertyname];
                        if (!currentValue?.Equals(newValue) ?? true)
                        {
                            var interpolator = (currentValue as IInterpolable) ?? (newValue as IInterpolable);
                            if (interpolator != null)
                            {
                                allFrames.Add(Tuple.Create(
                                    propertyinfo,
                                    state.Calculators.TryGetValue(propertyname, out var calculator)
                                        ? calculator.Invoke(currentValue, newValue, FrameCount)
                                        : interpolator.Interpolate(currentValue, newValue, FrameCount)
                                ));
                            }
                        }
                    }
                }
            }
            return allFrames;
        }

        private static List<object?> InterpolateSolidColorBrush(SolidColorBrush start, SolidColorBrush end, int steps)
        {
            var rgb1 = RGB.FromBrush(start);
            var rgb2 = RGB.FromBrush(end);
            return [.. rgb1.Interpolate(rgb1, rgb2, steps).Select(rgb => (object?)(((rgb as RGB) ?? RGB.Empty)).Brush)];
        }
        private static List<object?> InterpolateBrushOpacity(Brush start, Brush end, int steps, object oriEnd)
        {
            var result = new List<object?>(steps);
            if (steps <= 0) return result;

            // 计算实际透明度
            double startAlpha = GetEffectiveOpacity(start);
            double endAlpha = GetEffectiveOpacity(end);

            int halfSteps = (int)Math.Ceiling(steps / 2.0);

            for (int i = 0; i < steps; i++)
            {
                // 每帧独立克隆
                var frameStart = start.CloneCurrentValue() ?? Brushes.Transparent;
                var frameEnd = end.CloneCurrentValue() ?? Brushes.Transparent;

                if (i < halfSteps)
                {
                    // 阶段1：双画刷统一向0.5过渡
                    double t = (double)i / halfSteps;
                    SetCompositeOpacity(frameStart, Lerp(startAlpha, 0.5, t));
                    SetCompositeOpacity(frameEnd, Lerp(0, 0.5, t));
                }
                else
                {
                    // 阶段2：start→透明，end→目标值
                    double t = (double)(i - halfSteps) / (steps - halfSteps);
                    SetCompositeOpacity(frameStart, Lerp(0.5, 0, t));
                    SetCompositeOpacity(frameEnd, Lerp(0.5, endAlpha, t));
                }

                // 使用DrawingBrush实现完美混合
                result.Add(new DrawingBrush(new DrawingGroup
                {
                    Children =
                    {
                        new GeometryDrawing(frameStart, null, new RectangleGeometry(new Rect(0, 0, 1, 1))),
                        new GeometryDrawing(frameEnd, null, new RectangleGeometry(new Rect(0, 0, 1, 1)))
                    }
                }));
            }

            return ApplyEdgeCases(result, steps, start, oriEnd);
        }
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
        private static double GetEffectiveOpacity(Brush brush)
        {
            if (brush == null) return 0;

            double opacity = brush.Opacity;
            if (brush is SolidColorBrush scb)
            {
                opacity *= scb.Color.A / 255.0;
            }
            return XMath.Clamp(opacity, 0, 1);
        }
        private static void SetCompositeOpacity(Brush brush, double targetOpacity)
        {
            if (brush == null) return;
#if NET
            targetOpacity = Math.Clamp(targetOpacity, 0, 1);
#elif NETFRAMEWORK
            targetOpacity = targetOpacity.Clamp(0, 1);
#endif

            if (brush is SolidColorBrush scb)
            {
                var color = scb.Color;
                scb.Color = Color.FromArgb(
                    (byte)(targetOpacity * 255),
                    color.R,
                    color.G,
                    color.B
                );
                brush.Opacity = 1;
            }
            else
            {
                brush.Opacity = targetOpacity;
            }
        }
        private static List<object?> ApplyEdgeCases(List<object?> result, int steps, object start, object end)
        {
            if (steps == 0) return [end];
            if (steps > 1)
            {
                result[0] = start;
                result[result.Count - 1] = end;
            }
            return result;
        }
    }
}
