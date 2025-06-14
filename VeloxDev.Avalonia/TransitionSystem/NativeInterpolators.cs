using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
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
                var startBrush = AdaptStartBrush(start, endBrush);

                var result = new List<object?>();

                if (steps <= 1)
                {
                    result.Add(endBrush);
                    return result;
                }

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

            private static IBrush AdaptStartBrush(object? start, IBrush endBrush)
            {
                if (start == null || (start is IBrush b && IsFullyTransparent(b)))
                {
                    return CreateTransparentVersion(endBrush);
                }

                if (start.GetType() == endBrush.GetType())
                {
                    return (IBrush)start;
                }

                return start switch
                {
                    ISolidColorBrush solid => ConvertToTargetType(solid, endBrush),
                    _ => CreateTransparentVersion(endBrush)
                };
            }

            private static IBrush ConvertToTargetType(ISolidColorBrush solid, IBrush target)
            {
                return target switch
                {
                    ILinearGradientBrush lg => new LinearGradientBrush
                    {
                        GradientStops =
                    [
                        new GradientStop(solid.Color, 0),
                        new GradientStop(solid.Color, 1)
                    ],
                        StartPoint = lg.StartPoint,
                        EndPoint = lg.EndPoint,
                        Opacity = solid.Opacity,
                        SpreadMethod = lg.SpreadMethod
                    },
                    IConicGradientBrush cg => new ConicGradientBrush
                    {
                        GradientStops =
                    [
                        new GradientStop(solid.Color, 0),
                        new GradientStop(solid.Color, 1)
                    ],
                        Center = cg.Center,
                        Angle = cg.Angle,
                        Opacity = solid.Opacity,
                        SpreadMethod = cg.SpreadMethod
                    },
                    _ => solid,
                };
            }

            private static IBrush CreateTransparentVersion(IBrush brush)
            {
                switch (brush)
                {
                    case ISolidColorBrush:
                        return Brushes.Transparent;

                    case ILinearGradientBrush lg:
                        var transparentStops = new GradientStops();
                        foreach (var stop in lg.GradientStops)
                        {
                            transparentStops.Add(new GradientStop(
                                Color.FromArgb(0, stop.Color.R, stop.Color.G, stop.Color.B),
                                stop.Offset));
                        }
                        return new LinearGradientBrush
                        {
                            GradientStops = transparentStops,
                            StartPoint = lg.StartPoint,
                            EndPoint = lg.EndPoint,
                            SpreadMethod = lg.SpreadMethod,
                            Opacity = 0
                        };

                    case IConicGradientBrush cg:
                        var cgTransparentStops = new GradientStops();
                        foreach (var stop in cg.GradientStops)
                        {
                            cgTransparentStops.Add(new GradientStop(
                                Color.FromArgb(0, stop.Color.R, stop.Color.G, stop.Color.B),
                                stop.Offset));
                        }
                        return new ConicGradientBrush
                        {
                            GradientStops = cgTransparentStops,
                            Center = cg.Center,
                            Angle = cg.Angle,
                            SpreadMethod = cg.SpreadMethod,
                            Opacity = 0
                        };

                    default:
                        return Brushes.Transparent;
                }
            }

            private static IBrush InterpolateBrush(IBrush start, IBrush end, double t)
            {
                if (start.GetType() == end.GetType())
                {
                    return start switch
                    {
                        ISolidColorBrush s when end is ISolidColorBrush e => InterpolateSolid(s, e, t),
                        ILinearGradientBrush lg when end is ILinearGradientBrush lgEnd => InterpolateLinearGradient(lg, lgEnd, t),
                        IConicGradientBrush cg when end is IConicGradientBrush cgEnd => InterpolateConicGradient(cg, cgEnd, t),
                        _ => t < 0.5 ? start : end
                    };
                }

                return CrossFadeBrushes(start, end, t);
            }

            private static ISolidColorBrush InterpolateSolid(ISolidColorBrush start, ISolidColorBrush end, double t)
            {
                return new SolidColorBrush(InterpolateColor(start.Color, end.Color, t))
                {
                    Opacity = InterpolateDouble(start.Opacity, end.Opacity, t)
                };
            }

            private static IBrush InterpolateLinearGradient(ILinearGradientBrush start, ILinearGradientBrush end, double t)
            {
                var result = new LinearGradientBrush
                {
                    StartPoint = InterpolatePoint(start.StartPoint, end.StartPoint, t),
                    EndPoint = InterpolatePoint(start.EndPoint, end.EndPoint, t),
                    SpreadMethod = t < 0.5 ? start.SpreadMethod : end.SpreadMethod,
                    Opacity = InterpolateDouble(start.Opacity, end.Opacity, t)
                };

                var stops = InterpolateGradientStops([.. start.GradientStops], [.. end.GradientStops], t);
                foreach (var stop in stops)
                {
                    result.GradientStops.Add(stop);
                }

                return result;
            }

            private static IBrush InterpolateConicGradient(IConicGradientBrush start, IConicGradientBrush end, double t)
            {
                var result = new ConicGradientBrush
                {
                    Center = InterpolatePoint(start.Center, end.Center, t),
                    Angle = InterpolateDouble(start.Angle, end.Angle, t),
                    SpreadMethod = t < 0.5 ? start.SpreadMethod : end.SpreadMethod,
                    Opacity = InterpolateDouble(start.Opacity, end.Opacity, t)
                };

                var stops = InterpolateGradientStops([.. start.GradientStops], [.. end.GradientStops], t);
                foreach (var stop in stops)
                {
                    result.GradientStops.Add(stop);
                }

                return result;
            }

            private static List<GradientStop> InterpolateGradientStops(
                IList<IGradientStop> startStops,
                IList<IGradientStop> endStops,
                double t)
            {
                var result = new List<GradientStop>();
                int maxCount = Math.Max(startStops.Count, endStops.Count);

                for (int i = 0; i < maxCount; i++)
                {
                    var startStop = i < startStops.Count ? startStops[i] : null;
                    var endStop = i < endStops.Count ? endStops[i] : null;

                    if (startStop != null && endStop != null)
                    {
                        result.Add(new GradientStop(
                            InterpolateColor(startStop.Color, endStop.Color, t),
                            InterpolateDouble(startStop.Offset, endStop.Offset, t)));
                    }
                    else if (startStop != null)
                    {
                        result.Add(new GradientStop(
                            InterpolateColor(startStop.Color, Colors.Transparent, t),
                            startStop.Offset));
                    }
                    else if (endStop != null)
                    {
                        result.Add(new GradientStop(
                            InterpolateColor(Colors.Transparent, endStop.Color, t),
                            endStop.Offset));
                    }
                }

                return result;
            }

            private static IBrush CrossFadeBrushes(IBrush start, IBrush end, double t)
            {
                if (t <= 0.0) return start;
                if (t >= 1.0) return end;

                // 简单但有效的混合方案
                return t < 0.5 ? start : end;
            }

            private static bool IsFullyTransparent(IBrush brush)
            {
                return brush is ISolidColorBrush s && s.Color.A == 0;
            }

            private static Color InterpolateColor(Color start, Color end, double t)
            {
                return Color.FromArgb(
                    (byte)(start.A + (end.A - start.A) * t),
                    (byte)(start.R + (end.R - start.R) * t),
                    (byte)(start.G + (end.G - start.G) * t),
                    (byte)(start.B + (end.B - start.B) * t));
            }

            private static double InterpolateDouble(double start, double end, double t)
            {
                return start + (end - start) * t;
            }

            private static RelativePoint InterpolatePoint(RelativePoint start, RelativePoint end, double t)
            {
                if (start.Unit != end.Unit) return t < 0.5 ? start : end;

                return new RelativePoint(
                    start.Point.X + (end.Point.X - start.Point.X) * t,
                    start.Point.Y + (end.Point.Y - start.Point.Y) * t,
                    start.Unit);
            }
        }
        public class TransformInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                throw new NotImplementedException();
            }
        }
    }
}
