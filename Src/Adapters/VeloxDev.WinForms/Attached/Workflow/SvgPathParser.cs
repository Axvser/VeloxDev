using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// The smallest SVG path reader the slot glyphs need: <c>M/m</c>, <c>A/a</c> and <c>Z</c>.
/// </summary>
/// <remarks>
/// Slot art is a path rather than a bitmap so every adapter draws the same artboard. Only the commands that art uses
/// are understood; an unrecognised one stops the parse and returns what it has rather than throwing.
/// </remarks>
internal static class SvgPathParser
{
    // 只覆盖 VeloxDev 插槽图形用到的那一小撮命令（M/m 移动、A/a 椭圆弧、Z 闭合）。
    // 弧段用 GraphicsPath.AddArc 画。
    public static GraphicsPath BuildPath(string data)
    {
        var path = new GraphicsPath();
        var tokens = Tokenize(data);
        var i = 0;
        var current = new PointF();
        var start = new PointF();
        var isOpen = false;

        while (i < tokens.Count)
        {
            var command = tokens[i].ToUpperInvariant();
            var relative = tokens[i] == command.ToLowerInvariant();
            i++;

            switch (command)
            {
                case "M":
                    // 相对 moveto：第一对相对当前点。
                    while (i < tokens.Count && IsNumber(tokens[i]))
                    {
                        var p = ReadPoint(tokens, ref i);
                        if (relative)
                        {
                            p = new PointF(current.X + p.X, current.Y + p.Y);
                        }

                        current = p;
                        start = p;
                        isOpen = true;
                        // moveto 之后成对出现的坐标是隐含的 lineto。
                        if (i < tokens.Count && IsLetter(tokens[i])) break;
                    }
                    break;

                case "A":
                    while (i < tokens.Count && IsNumber(tokens[i]))
                    {
                        var rx = float.Parse(tokens[i++], CultureInfo.InvariantCulture);
                        var ry = float.Parse(tokens[i++], CultureInfo.InvariantCulture);
                        var rotation = float.Parse(tokens[i++], CultureInfo.InvariantCulture);
                        var largeArc = int.Parse(tokens[i++], CultureInfo.InvariantCulture) != 0;
                        var sweep = int.Parse(tokens[i++], CultureInfo.InvariantCulture) != 0;
                        var end = ReadPoint(tokens, ref i);
                        if (relative)
                        {
                            end = new PointF(current.X + end.X, current.Y + end.Y);
                        }

                        AddArc(path, current, rx, ry, largeArc, sweep, end);
                        current = end;
                        if (i < tokens.Count && IsLetter(tokens[i])) break;
                    }
                    break;

                case "Z":
                    path.CloseFigure();
                    current = start;
                    isOpen = false;
                    break;

                default:
                    // 看不懂的命令：体面地停下，而不是抛。
                    return path;
            }
        }

        if (isOpen)
        {
            path.CloseFigure();
        }

        return path;
    }

    private static void AddArc(
        GraphicsPath path, PointF p1, float rx, float ry, bool largeArc, bool sweep, PointF p2)
    {
        if (rx <= 0 || ry <= 0 || p1 == p2)
        {
            return;
        }

        // VeloxDev 的图形只用圆（rx == ry）；rx == 0 / ry != 0 这类按较大半径夹取。
        var r = Math.Max(rx, ry);
        var dx = p2.X - p1.X;
        var dy = p2.Y - p1.Y;
        var d = (float)Math.Sqrt(dx * dx + dy * dy);

        if (d > 2 * r)
        {
            // 几何非法（弦比直径还长）：把半径放大。
            r = d / 2f;
        }

        // 弦中点与垂直单位向量。
        var mx = (p1.X + p2.X) / 2f;
        var my = (p1.Y + p2.Y) / 2f;
        var half = d / 2f;
        var h = (float)Math.Sqrt(Math.Max(0, r * r - half * half));
        var ux = -dy / d;
        var uy = dx / d;

        // 圆心选取：largeArc == sweep 取远端圆心，否则取近端。
        var sign = (largeArc == sweep) ? -1f : 1f;
        var cx = mx + sign * h * ux;
        var cy = my + sign * h * uy;

        var a1 = (float)Math.Atan2(p1.Y - cy, p1.X - cx);
        var a2 = (float)Math.Atan2(p2.Y - cy, p2.X - cx);
        var delta = a2 - a1;
        if (sweep && delta < 0) delta += 2f * (float)Math.PI;
        if (!sweep && delta > 0) delta -= 2f * (float)Math.PI;

        var startAngle = a1 * 180f / (float)Math.PI;
        var sweepAngle = delta * 180f / (float)Math.PI;
        var rect = new RectangleF(cx - r, cy - r, 2 * r, 2 * r);
        path.AddArc(rect, startAngle, sweepAngle);
    }

    private static PointF ReadPoint(List<string> tokens, ref int i)
    {
        var x = float.Parse(tokens[i++], CultureInfo.InvariantCulture);
        var y = float.Parse(tokens[i++], CultureInfo.InvariantCulture);
        return new PointF(x, y);
    }

    private static bool IsNumber(string token)
        => float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static bool IsLetter(string token)
        => token.Length == 1 && char.IsLetter(token[0]);

    private static List<string> Tokenize(string data)
    {
        var tokens = new List<string>();
        var current = "";

        foreach (var ch in data)
        {
            if (char.IsLetter(ch))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current);
                    current = "";
                }

                tokens.Add(ch.ToString());
            }
            else if (ch == ',' || ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n')
            {
                if (current.Length > 0)
                {
                    tokens.Add(current);
                    current = "";
                }
            }
            else if ((ch == '-' || ch == '+') && current.Length > 0)
            {
                // 正负号属于新数字，除非它是第一个字符。
                tokens.Add(current);
                current = ch.ToString();
            }
            else
            {
                current += ch;
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current);
        }

        return tokens;
    }
}
