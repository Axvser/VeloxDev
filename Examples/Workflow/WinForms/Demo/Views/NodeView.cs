// VeloxDev customization: Customize node content, but keep PART_* names synchronized
// with WorkflowSlotLayoutBehavior (SlotNames / SlotEnumeratorNames).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
// `Size` collides between System.Drawing and VeloxDev.WorkflowSystem; the node
// model's Size property is accessed through the interface, so a drawing alias
// keeps `new Size(w, h)` unambiguous in generated code.
using Size = System.Drawing.Size;

namespace Demo.Views;

/// <summary>
/// A self-drawn workflow node card. Hosts a single input slot (<c>PART_InputSlot</c>)
/// and an output slot enumerator (<c>PART_OutputSlots</c>) that
/// <see cref="WorkflowSlotLayoutBehavior"/> measures for link anchoring.
/// </summary>
public sealed class NodeView : UserControl
{
    /// <summary>Input slot host (slot views added by the consumer or selector).</summary>
    public Panel PART_InputSlot { get; }

    /// <summary>Output slot enumerator host (one slot view per output).</summary>
    public FlowLayoutPanel PART_OutputSlots { get; }

    private IWorkflowNodeViewModel? _node;
    private INotifyPropertyChanged? _notifier;
    private readonly Color _background = ParseColor("#DDFFFFFF");
    private readonly Color _foreground = ParseColor("#DD1E1E1E");
    private readonly Color _border = ParseColor("#331E1E1E");
    private readonly float _borderThickness = float.Parse("1", CultureInfo.InvariantCulture);
    private readonly float _cornerRadius = float.Parse("6", CultureInfo.InvariantCulture);
    private string _title = "";

    public NodeView()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        BackColor = _background;

        // Header row: title + drag surface (whole card acts as the drag handle).
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = _background,
            Name = "PART_Header",
        };
        header.Paint += (_, e) =>
        {
            var g = e.Graphics;
            using var brush = new SolidBrush(_foreground);
            var font = new Font(Font.FontFamily, 10f, FontStyle.Bold);
            var rect = new RectangleF(12, 0, Math.Max(0, header.Width - 24), header.Height);
            var format = new StringFormat { LineAlignment = StringAlignment.Center };
            g.DrawString(_title, font, brush, rect, format);
        };

        // Body: hosts content + slot hosts (kept transparent so the chrome shows through).
        PART_InputSlot = new Panel
        {
            Dock = DockStyle.Left,
            Width = 24,
            BackColor = Color.Transparent,
            Name = "PART_InputSlot",
        };
        PART_OutputSlots = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Name = "PART_OutputSlots",
        };

        var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        body.Controls.Add(PART_OutputSlots);
        body.Controls.Add(PART_InputSlot);

        Controls.Add(body);
        Controls.Add(header);

        // Node drag: the whole card is the drag handle.
        WorkflowNodeDragBehavior.SetIsEnabled(this, true);
        WorkflowNodeDragBehavior.SetCoordinateHostType(this, typeof(Panel));

        // Slot layout: measure PART_InputSlot and each PART_OutputSlots child.
        WorkflowSlotLayoutBehavior.SetIsEnabled(this, true);
        WorkflowSlotLayoutBehavior.SetSlotNames(this, "PART_InputSlot");
        WorkflowSlotLayoutBehavior.SetSlotEnumeratorNames(this, "PART_OutputSlots");
        WorkflowSlotLayoutBehavior.SetCoordinateHostType(this, typeof(Panel));
    }

    /// <summary>Gets or sets the workflow node bound to this view.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IWorkflowNodeViewModel? ViewModel
    {
        get => _node;
        set
        {
            if (ReferenceEquals(_node, value)) return;

            if (_notifier is not null)
            {
                _notifier.PropertyChanged -= OnNodeChanged;
                _notifier = null;
            }

            _node = value;
            Tag = value;

            if (value is INotifyPropertyChanged n)
            {
                _notifier = n;
                n.PropertyChanged += OnNodeChanged;
            }

            SyncTitle();
            ApplyPosition();
            Invalidate();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyPosition();
    }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        ApplyPosition();
    }

    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new PropertyChangedEventHandler(OnNodeChanged), sender, e);
            return;
        }

        if (e.PropertyName is "Name" or "Title" or null or "")
        {
            SyncTitle();
        }

        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor)
            or nameof(IWorkflowNodeViewModel.Size)
            or null or "")
        {
            ApplyPosition();
        }

        Invalidate();
    }

    /// <summary>
    /// Positions this card at the raw node anchor inside its coordinate host.
    /// Mirrors the WPF template's <c>Canvas.Left/Top/Width/Height</c> bindings:
    /// the host (tree surface) applies the canvas content offset itself, so the
    /// anchor coordinates are used as-is.
    /// </summary>
    private void ApplyPosition()
    {
        if (_node is null || Parent is null) return;

        Location = new Point(
            (int)Math.Round(_node.Anchor.Horizontal),
            (int)Math.Round(_node.Anchor.Vertical));
        Size = new Size(
            (int)Math.Round(_node.Size.Width),
            (int)Math.Round(_node.Size.Height));
    }

    /// <summary>
    /// Reads the display title from the node. <see cref="IWorkflowNodeViewModel"/> does
    /// not expose a name, so look up a <c>Name</c> or <c>Title</c> property reflectively
    /// (works with any node view-model, including the built-in VeloxDev samples).
    /// </summary>
    private void SyncTitle()
    {
        if (_node is null)
        {
            _title = "";
            return;
        }

        var property = _node.GetType().GetProperty("Name") ?? _node.GetType().GetProperty("Title");
        _title = property?.GetValue(_node)?.ToString() ?? "";
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Draw the rounded chrome here so the card paints as a single surface.
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(0, 0, Width, Height);
        using var path = RoundRect(bounds, _cornerRadius);
        using var brush = new SolidBrush(_background);
        using var pen = new Pen(_border, _borderThickness);
        g.FillPath(brush, path);
        g.DrawPath(pen, path);
    }

    private static GraphicsPath RoundRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var r = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f);
        var d = 2 * r;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
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
        if (disposing && _notifier is not null)
        {
            _notifier.PropertyChanged -= OnNodeChanged;
            _notifier = null;
        }

        base.Dispose(disposing);
    }
}
