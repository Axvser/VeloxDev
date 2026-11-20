using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.RegularExpressions;

namespace VeloxDev.WinForms.Xaml.Controls.Shapes;

using VeloxDev.WinForms.Xaml.Controls;

// 只覆盖 <Path Data="..."> 常用的 SVG 子集；看不懂的字符就地停下，返回已经画出的部分。
internal static class PathGeometry
{
    private static readonly Regex TokenPattern = new(
        @"[a-zA-Z]|[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static GraphicsPath Parse(string? data)
    {
        var path = new GraphicsPath();
        if (string.IsNullOrWhiteSpace(data))
        {
            return path;
        }

        var tokens = TokenPattern.Matches(data).Cast<Match>().Select(static match => match.Value).ToArray();
        var index = 0;
        var command = ' ';
        var current = new PointF();
        var start = new PointF();
        PointF? lastControl = null;
        var lastCommand = ' ';
        var started = false;

        while (index < tokens.Length)
        {
            if (IsCommand(tokens[index]))
            {
                command = tokens[index][0];
                index++;
                if (command is 'Z' or 'z')
                {
                    if (started)
                    {
                        path.CloseFigure();
                        current = start;
                    }

                    lastControl = null;
                    lastCommand = command;
                    continue;
                }
            }
            else if (command == ' ')
            {
                break;
            }

            var relative = char.IsLower(command);
            var upper = char.ToUpperInvariant(command);
            try
            {
                switch (upper)
                {
                    case 'M':
                    {
                        var point = ReadPoint(tokens, ref index, relative, current);
                        if (!started)
                        {
                            path.StartFigure();
                            started = true;
                        }
                        else
                        {
                            path.AddLine(current, point);
                        }

                        current = point;
                        start = point;
                        // Moveto 之后的隐式坐标对是 lineto。
                        command = relative ? 'l' : 'L';
                        lastControl = null;
                        lastCommand = upper;
                        break;
                    }
                    case 'L':
                    {
                        var point = ReadPoint(tokens, ref index, relative, current);
                        path.AddLine(current, point);
                        current = point;
                        lastControl = null;
                        lastCommand = upper;
                        break;
                    }
                    case 'H':
                    {
                        var x = ReadNumber(tokens, ref index);
                        var point = new PointF(relative ? current.X + x : x, current.Y);
                        path.AddLine(current, point);
                        current = point;
                        lastControl = null;
                        lastCommand = upper;
                        break;
                    }
                    case 'V':
                    {
                        var y = ReadNumber(tokens, ref index);
                        var point = new PointF(current.X, relative ? current.Y + y : y);
                        path.AddLine(current, point);
                        current = point;
                        lastControl = null;
                        lastCommand = upper;
                        break;
                    }
                    case 'C':
                    {
                        var c1 = ReadPoint(tokens, ref index, relative, current);
                        var c2 = ReadPoint(tokens, ref index, relative, current);
                        var end = ReadPoint(tokens, ref index, relative, current);
                        path.AddBezier(current, c1, c2, end);
                        current = end;
                        lastControl = c2;
                        lastCommand = upper;
                        break;
                    }
                    case 'S':
                    {
                        var c1 = lastCommand is 'C' or 'S' && lastControl is { } previous
                            ? Reflect(current, previous)
                            : current;
                        var c2 = ReadPoint(tokens, ref index, relative, current);
                        var end = ReadPoint(tokens, ref index, relative, current);
                        path.AddBezier(current, c1, c2, end);
                        current = end;
                        lastControl = c2;
                        lastCommand = upper;
                        break;
                    }
                    case 'Q':
                    {
                        var control = ReadPoint(tokens, ref index, relative, current);
                        var end = ReadPoint(tokens, ref index, relative, current);
                        AddQuadratic(path, current, control, end);
                        current = end;
                        lastControl = control;
                        lastCommand = upper;
                        break;
                    }
                    case 'T':
                    {
                        var control = lastCommand is 'Q' or 'T' && lastControl is { } previous
                            ? Reflect(current, previous)
                            : current;
                        var end = ReadPoint(tokens, ref index, relative, current);
                        AddQuadratic(path, current, control, end);
                        current = end;
                        lastControl = control;
                        lastCommand = upper;
                        break;
                    }
                    case 'A':
                    {
                        var rx = ReadNumber(tokens, ref index);
                        var ry = ReadNumber(tokens, ref index);
                        _ = ReadNumber(tokens, ref index);
                        var largeArc = ReadNumber(tokens, ref index) != 0;
                        var sweep = ReadNumber(tokens, ref index) != 0;
                        var end = ReadPoint(tokens, ref index, relative, current);
                        AddArc(path, current, rx, ry, largeArc, sweep, end);
                        current = end;
                        lastControl = null;
                        lastCommand = upper;
                        break;
                    }
                    default:
                        return path;
                }
            }
            catch (IndexOutOfRangeException)
            {
                return path;
            }
        }

        return path;
    }

    private static void AddQuadratic(GraphicsPath path, PointF start, PointF control, PointF end)
    {
        // GDI+ 没有二次贝塞尔；按 2/3 控制点规则升阶成三次。
        var c1 = new PointF(
            start.X + ((control.X - start.X) * 2f / 3f),
            start.Y + ((control.Y - start.Y) * 2f / 3f));
        var c2 = new PointF(
            end.X + ((control.X - end.X) * 2f / 3f),
            end.Y + ((control.Y - end.Y) * 2f / 3f));
        path.AddBezier(start, c1, c2, end);
    }

    private static void AddArc(GraphicsPath path, PointF start, double rx, double ry, bool largeArc, bool sweep, PointF end)
    {
        var radius = Math.Max(Math.Abs(rx), Math.Abs(ry));
        if (radius <= 0d || start == end)
        {
            path.AddLine(start, end);
            return;
        }

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        if (distance <= 0d)
        {
            return;
        }

        radius = Math.Max(radius, distance / 2d);
        var midX = (start.X + end.X) / 2d;
        var midY = (start.Y + end.Y) / 2d;
        var offset = Math.Sqrt(Math.Max(0d, (radius * radius) - ((distance / 2d) * (distance / 2d))));
        var ux = -dy / distance;
        var uy = dx / distance;
        var sign = largeArc == sweep ? -1d : 1d;
        var centerX = midX + (sign * offset * ux);
        var centerY = midY + (sign * offset * uy);

        var startAngle = Math.Atan2(start.Y - centerY, start.X - centerX) * 180d / Math.PI;
        var endAngle = Math.Atan2(end.Y - centerY, end.X - centerX) * 180d / Math.PI;
        var sweepAngle = endAngle - startAngle;
        if (sweep && sweepAngle < 0d)
        {
            sweepAngle += 360d;
        }
        else if (!sweep && sweepAngle > 0d)
        {
            sweepAngle -= 360d;
        }

        if (largeArc && Math.Abs(sweepAngle) < 180d)
        {
            sweepAngle += sweepAngle < 0d ? -360d : 360d;
        }
        else if (!largeArc && Math.Abs(sweepAngle) > 180d)
        {
            sweepAngle += sweepAngle < 0d ? 360d : -360d;
        }

        var rect = new RectangleF(
            (float)(centerX - radius),
            (float)(centerY - radius),
            (float)(radius * 2d),
            (float)(radius * 2d));
        path.AddArc(rect, (float)startAngle, (float)sweepAngle);
    }

    private static PointF Reflect(PointF current, PointF control)
        => new((2f * current.X) - control.X, (2f * current.Y) - control.Y);

    private static PointF ReadPoint(string[] tokens, ref int index, bool relative, PointF current)
    {
        var x = ReadNumber(tokens, ref index);
        var y = ReadNumber(tokens, ref index);
        return relative ? new PointF(current.X + x, current.Y + y) : new PointF(x, y);
    }

    private static float ReadNumber(string[] tokens, ref int index)
    {
        if (index >= tokens.Length)
        {
            throw new IndexOutOfRangeException();
        }

        return float.Parse(tokens[index++], CultureInfo.InvariantCulture);
    }

    private static bool IsCommand(string token) => token.Length == 1 && char.IsLetter(token[0]);
}
