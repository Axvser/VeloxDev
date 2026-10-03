using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// A workflow slot that participates in drag-to-connect: an SVG glyph tinted by the slot's state.
/// </summary>
/// <remarks>
/// <para>
/// Derive from it to restyle it — set <see cref="SlotPath"/> to your own glyph and the colours to your palette.
/// The binding, the paint, the state tinting and the SVG parsing are the same in every host.
/// </para>
/// <para>
/// The glyph is a path rather than a bitmap so it stays in sync with the XAML adapters' slot views, which draw the
/// same artboard.
/// </para>
/// </remarks>
public class WorkflowSlotView : Control
{
    private IWorkflowSlotViewModel? _slot;
    private readonly ModelChangeRelay _relay;
    private GraphicsPath? _iconPath;
    private string _slotPath = string.Empty;
    private float _pathViewBox = 1024f;
    private Color _slotBackground = Color.FromArgb(0x01, 0x00, 0x00, 0x00);
    private Color _standbyColor = Color.FromArgb(0xDD, 0x1E, 0x1E, 0x1E);
    private Color _borderColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

    /// <summary>Creates the slot glyph.</summary>
    public WorkflowSlotView()
    {
        _relay = new ModelChangeRelay(this, _ => Invalidate());

        // 全名限定：本文件的命名空间嵌在 VeloxDev.WorkflowSystem 里，裸写 `Size` 会解析成模型的那个 Size。
        Size = new System.Drawing.Size(20, 20);
        Margin = Padding.Empty;
        Cursor = Cursors.Hand;
        TabStop = false;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        // alpha 强制 255：共享的 slotBackground 默认是 #01000000（近乎透明），而 WinForms 的
        // Control.BackColor 在 A != 0xFF 且未声明 SupportsTransparentBackColor 时会抛。这个底色只作为
        // 「没有父控件」时的兜底（OnPaintBackground 会擦成父控件的底色），所以不透明就够。
        BackColor = Opaque(_slotBackground);
        WorkflowSlotConnectionBehavior.SetIsEnabled(this, true);
    }

    /// <summary>Gets or sets the workflow slot bound to this view.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IWorkflowSlotViewModel? ViewModel
    {
        get => _slot;
        set
        {
            if (ReferenceEquals(_slot, value)) return;

            _slot = value;
            Tag = value;
            Visible = value is not null;
            _relay.Set(value as INotifyPropertyChanged);

            Invalidate();
        }
    }

    /// <summary>The glyph, as an SVG path in the <see cref="PathViewBox"/> artboard.</summary>
    /// <remarks>Changing it re-parses on the next paint.</remarks>
    public string SlotPath
    {
        get => _slotPath;
        set
        {
            if (string.Equals(_slotPath, value, StringComparison.Ordinal)) return;
            _slotPath = value ?? string.Empty;
            DisposeIconPath();
            Invalidate();
        }
    }

    /// <summary>The artboard the <see cref="SlotPath"/> coordinates are expressed in — its SVG viewBox size.</summary>
    public float PathViewBox
    {
        get => _pathViewBox;
        set
        {
            if (_pathViewBox == value || value <= 0) return;
            _pathViewBox = value;
            Invalidate();
        }
    }

    /// <summary>Background used when the control has no parent to erase to.</summary>
    public Color SlotBackground
    {
        get => _slotBackground;
        set
        {
            if (_slotBackground == value) return;
            _slotBackground = value;
            BackColor = Opaque(value);
            Invalidate();
        }
    }

    /// <summary>The glyph's fill while the slot is idle.</summary>
    public Color StandbyColor
    {
        get => _standbyColor;
        set
        {
            if (_standbyColor == value) return;
            _standbyColor = value;
            Invalidate();
        }
    }

    /// <summary>The glyph's outline.</summary>
    public Color BorderColor
    {
        get => _borderColor;
        set
        {
            if (_borderColor == value) return;
            _borderColor = value;
            Invalidate();
        }
    }

    /// <summary>Parses a <c>#RRGGBB</c>, <c>#AARRGGBB</c> or named colour.</summary>
    /// <param name="hex">The colour text.</param>
    /// <returns>The colour.</returns>
    protected static Color ParseColor(string hex) => WorkflowSurfaceColors.Parse(hex);

    // 不透明底色：WinForms 没有可靠的透明合成，所以插槽擦成父控件（不透明）的底色，而不是声明
    // SupportsTransparentBackColor 去走那条半透明合成链。擦成 Parent.BackColor 是安全的 —— 每个宿主面板都不
    // 透明，Clear 永远看不到 Color.Transparent（GDI 会把它画成黑）。
    private static Color Opaque(Color color) => Color.FromArgb(255, color);

    /// <inheritdoc />
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_slot is null && Tag is IWorkflowSlotViewModel tagged)
        {
            ViewModel = tagged;
        }
    }

    /// <inheritdoc />
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (e is null) throw new ArgumentNullException(nameof(e));

        e.Graphics.Clear(Parent?.BackColor ?? Opaque(_slotBackground));
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        if (e is null) throw new ArgumentNullException(nameof(e));

        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_iconPath is null)
        {
            if (_slotPath.Length == 0) return;
            _iconPath = SvgPathParser.BuildPath(_slotPath);
        }

        // 把画板缩放进控件，等价于一个 WPF Viewbox。
        var saved = g.Save();
        g.ScaleTransform(Width / _pathViewBox, Height / _pathViewBox);

        using var fill = new SolidBrush(ResolveGlyphColor());
        using var pen = new Pen(_borderColor, 1.5f);
        g.FillPath(fill, _iconPath);
        g.DrawPath(pen, _iconPath);

        g.Restore(saved);
    }

    // 三个状态色是语义色（发送 / 接收 / 双向），不是调色板，所以不开放配置。
    private Color ResolveGlyphColor()
    {
        if (_slot is null) return _standbyColor;
        return _slot.State switch
        {
            var s when s.HasFlag(SlotState.Sender) && s.HasFlag(SlotState.Receiver) => Color.Violet,
            var s when s.HasFlag(SlotState.Sender) => Color.Tomato,
            var s when s.HasFlag(SlotState.Receiver) => Color.Lime,
            _ => _standbyColor,
        };
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _relay.Clear();
            DisposeIconPath();
        }

        base.Dispose(disposing);
    }

    private void DisposeIconPath()
    {
        _iconPath?.Dispose();
        _iconPath = null;
    }

    // 只覆盖 VeloxDev 插槽图形用到的那一小撮命令（M/m 移动、A/a 椭圆弧、Z 闭合）的最小 SVG 路径解析器。
    // 弧段用 GraphicsPath.AddArc 画。
    private static class SvgPathParser
    {
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
}
