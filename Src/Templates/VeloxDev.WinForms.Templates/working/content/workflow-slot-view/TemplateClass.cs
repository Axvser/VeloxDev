// VeloxDev customization: Customize the slot icon, color, and border here.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
// `Size` collides between System.Drawing and VeloxDev.WorkflowSystem; a drawing
// alias keeps `new Size(20, 20)` unambiguous in generated code.
using Size = System.Drawing.Size;

namespace TemplateNamespace;

/// <summary>
/// A workflow slot that participates in drag-to-connect. The glyph is parsed from
/// an SVG path (<c>TemplateSlotPath</c>) so the icon stays in sync with the XAML
/// adapters' slot views.
/// </summary>
public sealed class TemplateClass : Control
{
    private IWorkflowSlotViewModel? _slot;
    private INotifyPropertyChanged? _notifier;
    private GraphicsPath? _iconPath;
    private readonly Color _standbyColor = ParseColor("TemplateSlotColor");
    private readonly Color _borderColor = ParseColor("TemplateSlotBorderColor");

    public TemplateClass()
    {
        Size = new Size(20, 20);
        Margin = Padding.Empty;
        Cursor = Cursors.Hand;
        TabStop = false;
        // TemplateSlotBackground is a translucent color (#01000000); the control
        // must declare SupportsTransparentBackColor before assigning it, otherwise
        // WinForms throws (transparent background key not supported).
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = ParseColor("TemplateSlotBackground");
        WorkflowSlotConnectionBehavior.SetIsEnabled(this, true);
    }

    // No OnPaintBackground override: with a translucent BackColor the default
    // paints the nearest opaque ancestor's background through this control, so the
    // glyph floats over the node card (or the grid where the slot is half-off the
    // edge). Clearing to Parent.BackColor here instead painted BLACK when the host
    // panel is itself transparent (Clear(Color.Transparent) == black on GDI),
    // producing the jarring dark box around each slot.

    /// <summary>Gets or sets the workflow slot bound to this view.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IWorkflowSlotViewModel? ViewModel
    {
        get => _slot;
        set
        {
            if (ReferenceEquals(_slot, value)) return;

            if (_notifier is not null)
            {
                _notifier.PropertyChanged -= OnSlotChanged;
                _notifier = null;
            }

            _slot = value;
            Tag = value;
            Visible = value is not null;

            if (value is INotifyPropertyChanged n)
            {
                _notifier = n;
                n.PropertyChanged += OnSlotChanged;
            }

            Invalidate();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_slot is null && Tag is IWorkflowSlotViewModel tagged)
        {
            ViewModel = tagged;
        }
    }

    /// <summary>Design-time viewBox size of the slot glyph's SVG artboard.</summary>
    private const float ArtboardSize = 1024f;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        _iconPath ??= SvgPathParser.BuildPath("TemplateSlotPath");

        // Scale the artboard (1024²) into the control, mirroring a WPF Viewbox.
        var saved = g.Save();
        g.ScaleTransform(Width / ArtboardSize, Height / ArtboardSize);

        using var fill = new SolidBrush(StandbyColor());
        using var pen = new Pen(_borderColor, 1.5f);
        g.FillPath(fill, _iconPath);
        g.DrawPath(pen, _iconPath);

        g.Restore(saved);
    }

    private Color StandbyColor()
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

    private void OnSlotChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new PropertyChangedEventHandler(OnSlotChanged), sender, e);
            return;
        }

        Invalidate();
    }

    private static Color ParseColor(string hex)
    {
        var value = hex.Trim();
        if (value.StartsWith("#", StringComparison.Ordinal))
        {
            var digits = value.Substring(1);
            if (digits.Length == 8)
            {
                return Color.FromArgb(
                    byte.Parse(digits.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }

            if (digits.Length == 6)
            {
                return Color.FromArgb(
                    byte.Parse(digits.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
        }

        return Color.FromName(value);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_notifier is not null)
            {
                _notifier.PropertyChanged -= OnSlotChanged;
                _notifier = null;
            }

            _iconPath?.Dispose();
            _iconPath = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Minimal SVG path parser for the command subset used by the VeloxDev slot
    /// glyph (<c>M/m</c> moveto, <c>A/a</c> elliptical arc, <c>Z</c> close). Arc
    /// segments are drawn with <see cref="GraphicsPath.AddArc"/>.
    /// </summary>
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
                var relative = tokens[i] == command.ToLowerInvariant() ? true : false;
                i++;

                switch (command)
                {
                    case "M":
                        // Relative moveto: first pair is relative to current.
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
                            // Following pairs in a moveto command are implicit lineto.
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
                        // Unsupported command: stop gracefully rather than throw.
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

            // Only circular arcs are needed by the VeloxDev glyph (rx == ry); treat
            // rx == 0 / ry != 0 by clamping to the larger radius.
            var r = Math.Max(rx, ry);
            var dx = p2.X - p1.X;
            var dy = p2.Y - p1.Y;
            var d = (float)Math.Sqrt(dx * dx + dy * dy);

            if (d > 2 * r)
            {
                // Invalid geometry (chord longer than diameter): scale radius up.
                r = d / 2f;
            }

            // Chord midpoint and perpendicular unit vector.
            var mx = (p1.X + p2.X) / 2f;
            var my = (p1.Y + p2.Y) / 2f;
            var half = d / 2f;
            var h = (float)Math.Sqrt(Math.Max(0, r * r - half * half));
            var ux = -dy / d;
            var uy = dx / d;

            // Center selection: largeArc == sweep picks the far center, else near.
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
                    // Sign belongs to a new number unless it's the first character.
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
