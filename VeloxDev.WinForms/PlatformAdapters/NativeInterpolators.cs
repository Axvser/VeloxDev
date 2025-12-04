using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.WinForms.PlatformAdapters
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

        public class IntInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                var i1 = (int)(start ?? 0);
                var i2 = (int)(end ?? i1);
                if (steps == 1)
                {
                    return [i2];
                }

                List<object?> result = new(steps);
                var delta = i2 - i1;

                for (var i = 0; i < steps; i++)
                {
                    var t = (double)(i + 1) / steps;
                    result.Add(i1 + (int)(t * delta));
                }
                result[0] = start;
                result[steps - 1] = end;

                return result;
            }
        }

        public class FloatInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                var f1 = (float)(start ?? 0f);
                var f2 = (float)(end ?? f1);
                if (steps == 1)
                {
                    return [f2];
                }

                List<object?> result = new(steps);
                var delta = f2 - f1;

                for (var i = 0; i < steps; i++)
                {
                    var t = (float)(i + 1) / steps;
                    result.Add(f1 + t * delta);
                }
                result[0] = start;
                result[steps - 1] = end;

                return result;
            }
        }

        public class ColorInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                var color1 = (Color)(start ?? Color.Empty);
                var color2 = (Color)(end ?? color1);

                if (color1.IsEmpty) color1 = Color.Transparent;
                if (color2.IsEmpty) color2 = Color.Transparent;

                if (steps == 1)
                {
                    return [color2];
                }

                List<object?> result = new(steps);

                for (var i = 0; i < steps; i++)
                {
                    var t = (double)(i + 1) / steps;
                    var a = (byte)(color1.A + t * (color2.A - color1.A));
                    var r = (byte)(color1.R + t * (color2.R - color1.R));
                    var g = (byte)(color1.G + t * (color2.G - color1.G));
                    var b = (byte)(color1.B + t * (color2.B - color1.B));
                    result.Add(Color.FromArgb(a, r, g, b));
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
                    var x = point1.X + (int)(t * (point2.X - point1.X));
                    var y = point1.Y + (int)(t * (point2.Y - point1.Y));
                    result.Add(new Point(x, y));
                }
                result[0] = start;
                result[steps - 1] = end;

                return result;
            }
        }

        public class PointFInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                var point1 = (PointF)(start ?? new PointF(0, 0));
                var point2 = (PointF)(end ?? point1);
                if (steps == 1)
                {
                    return [point2];
                }

                List<object?> result = new(steps);

                for (var i = 0; i < steps; i++)
                {
                    var t = (float)(i + 1) / steps;
                    var x = point1.X + t * (point2.X - point1.X);
                    var y = point1.Y + t * (point2.Y - point1.Y);
                    result.Add(new PointF(x, y));
                }
                result[0] = start;
                result[steps - 1] = end;

                return result;
            }
        }

        public class SizeInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                var size1 = (Size)(start ?? new Size(0, 0));
                var size2 = (Size)(end ?? size1);
                if (steps == 1)
                {
                    return [size2];
                }

                List<object?> result = new(steps);

                for (var i = 0; i < steps; i++)
                {
                    var t = (double)(i + 1) / steps;
                    var width = size1.Width + (int)(t * (size2.Width - size1.Width));
                    var height = size1.Height + (int)(t * (size2.Height - size1.Height));
                    result.Add(new Size(width, height));
                }
                result[0] = start;
                result[steps - 1] = end;

                return result;
            }
        }

        public class SizeFInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                var size1 = (SizeF)(start ?? new SizeF(0, 0));
                var size2 = (SizeF)(end ?? size1);
                if (steps == 1)
                {
                    return [size2];
                }

                List<object?> result = new(steps);

                for (var i = 0; i < steps; i++)
                {
                    var t = (float)(i + 1) / steps;
                    var width = size1.Width + t * (size2.Width - size1.Width);
                    var height = size1.Height + t * (size2.Height - size1.Height);
                    result.Add(new SizeF(width, height));
                }
                result[0] = start;
                result[steps - 1] = end;

                return result;
            }
        }

        public class RectangleInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                var rect1 = (Rectangle)(start ?? new Rectangle(0, 0, 0, 0));
                var rect2 = (Rectangle)(end ?? rect1);
                if (steps == 1)
                {
                    return [rect2];
                }

                List<object?> result = new(steps);

                for (var i = 0; i < steps; i++)
                {
                    var t = (double)(i + 1) / steps;
                    var x = rect1.X + (int)(t * (rect2.X - rect1.X));
                    var y = rect1.Y + (int)(t * (rect2.Y - rect1.Y));
                    var width = rect1.Width + (int)(t * (rect2.Width - rect1.Width));
                    var height = rect1.Height + (int)(t * (rect2.Height - rect1.Height));
                    result.Add(new Rectangle(x, y, width, height));
                }
                result[0] = start;
                result[steps - 1] = end;

                return result;
            }
        }

        public class RectangleFInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                var rect1 = (RectangleF)(start ?? new RectangleF(0, 0, 0, 0));
                var rect2 = (RectangleF)(end ?? rect1);
                if (steps == 1)
                {
                    return [rect2];
                }

                List<object?> result = new(steps);

                for (var i = 0; i < steps; i++)
                {
                    var t = (float)(i + 1) / steps;
                    var x = rect1.X + t * (rect2.X - rect1.X);
                    var y = rect1.Y + t * (rect2.Y - rect1.Y);
                    var width = rect1.Width + t * (rect2.Width - rect1.Width);
                    var height = rect1.Height + t * (rect2.Height - rect1.Height);
                    result.Add(new RectangleF(x, y, width, height));
                }
                result[0] = start;
                result[steps - 1] = end;

                return result;
            }
        }

        public class PaddingInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                var padding1 = (Padding)(start ?? new Padding(0));
                var padding2 = (Padding)(end ?? padding1);
                if (steps == 1)
                {
                    return [padding2];
                }

                List<object?> result = new(steps);

                for (var i = 0; i < steps; i++)
                {
                    var t = (double)(i + 1) / steps;
                    var left = padding1.Left + (int)(t * (padding2.Left - padding1.Left));
                    var top = padding1.Top + (int)(t * (padding2.Top - padding1.Top));
                    var right = padding1.Right + (int)(t * (padding2.Right - padding1.Right));
                    var bottom = padding1.Bottom + (int)(t * (padding2.Bottom - padding1.Bottom));
                    result.Add(new Padding(left, top, right, bottom));
                }
                result[0] = start;
                result[steps - 1] = end;

                return result;
            }
        }

        public class FontInterpolator : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
            {
                var font1 = (Font)(start ?? SystemFonts.DefaultFont);
                var font2 = (Font)(end ?? font1);

                if (steps == 1)
                {
                    return [font2];
                }

                List<object?> result = new(steps);

                // 字体插值主要处理大小，保持字体族和样式不变（以目标字体为准）
                var fontFamily = font2.FontFamily;
                var fontStyle = font2.Style;

                for (var i = 0; i < steps; i++)
                {
                    var t = (float)(i + 1) / steps;
                    var size = font1.SizeInPoints + t * (font2.SizeInPoints - font1.SizeInPoints);

                    // 确保最小字体大小为1
                    size = Math.Max(1, size);

                    result.Add(new Font(fontFamily, size, fontStyle));
                }
                result[0] = start;
                result[steps - 1] = end;

                return result;
            }
        }
    }
}