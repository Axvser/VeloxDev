// VeloxDev customization: Customize node content, but keep PART_* names synchronized
// with WorkflowSlotLayoutBehavior (SlotNames / SlotEnumeratorNames).
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
// `Size` collides between System.Drawing and VeloxDev.WorkflowSystem; the node
// model's Size property is accessed through the interface, so a drawing alias
// keeps `new Size(w, h)` unambiguous in generated code.
using Size = System.Drawing.Size;

namespace TemplateNamespace;

/// <summary>
/// A self-drawn workflow node card. The node's fixed input slot renders as a bare
/// port glyph at the card's left edge, and every output (single and enumerated)
/// renders inside the card as a labeled row — all hosted in
/// <c>PART_DynamicOutputs</c>, which <see cref="WorkflowSlotLayoutBehavior"/>
/// measures for link anchoring. No slot hangs off the card edge.
/// </summary>
public sealed class TemplateClass : UserControl
{
    /// <summary>Inside-card host for the bare input port and the labeled output rows.</summary>
    public Panel PART_DynamicOutputs => _dynamicOutputs;

    private readonly DynamicOutputsPanel _dynamicOutputs;
    private IWorkflowNodeViewModel? _node;
    private INotifyPropertyChanged? _notifier;
    private readonly Color _background = ParseColor("TemplateNodeBackground");
    private readonly Color _foreground = ParseColor("TemplateNodeForeground");
    private readonly Color _border = ParseColor("TemplateNodeBorderBrush");
    private readonly float _borderThickness = float.Parse("TemplateNodeBorderThickness", CultureInfo.InvariantCulture);
    private readonly float _cornerRadius = float.Parse("TemplateNodeCornerRadius", CultureInfo.InvariantCulture);
    // The card paints itself fully opaque (see OnPaintBackground). The configured
    // background color is often translucent, and a translucent fill can never erase
    // the double-buffer on repaint — stale pixels (old row labels after a
    // SetSelector rebuild, or a card that previously covered a region) bleed
    // through as faint ghosts and transparent children composite over garbage.
    // _opaqueBackground is the same color with its alpha forced to 255.
    private readonly Color _opaqueBackground;
    // The host surface behind the card is opaque #1E1E1E (the tree template's
    // default surface background); the rounded corners erase to this so they
    // visually match the surface (the canvas between cards is transparent, so this
    // card is the only opaque thing over it).
    private readonly Color _cardBackdrop = ParseColor("#1E1E1E");
    private string _title = "";

    public TemplateClass()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        BackColor = _background;
        _opaqueBackground = Color.FromArgb(255, _background.R, _background.G, _background.B);

        // Header row: title + drag surface (whole card acts as the drag handle).
        // Transparent so the card's own opaque background paints through: the title
        // composites onto the solid card instead of a translucent header panel that
        // would ghost stale pixels on repaint.
        var header = new TransparentPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
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

        // The input port and every output row render inside the card
        // (PART_DynamicOutputs). Keeping the glyphs fully inside the card (rather
        // than half-off the edges like the full demo's overlay slot buttons) avoids
        // disconnected glyphs floating over the grid.
        _dynamicOutputs = new DynamicOutputsPanel
        {
            Dock = DockStyle.Fill,
            Name = "PART_DynamicOutputs",
        };

        // Host the dynamic-outputs panel directly on the card. A transparent
        // intermediate panel would break the WinForms transparent-composite walk
        // (it stops at a plain panel and paints Color.Transparent, a no-op), so
        // rebuilt rows/labels would never erase stale pixels — the ghost text under
        // SetSelector-switched labels and the invisible input port both came from
        // that. Here the walk terminates at this card's real painted background.
        Controls.Add(PART_DynamicOutputs);
        Controls.Add(header);

        // Node drag: the whole card is the drag handle.
        WorkflowNodeDragBehavior.SetIsEnabled(this, true);
        WorkflowNodeDragBehavior.SetCoordinateHostType(this, typeof(Panel));

        // Slot layout: measure every SlotView under PART_DynamicOutputs.
        WorkflowSlotLayoutBehavior.SetIsEnabled(this, true);
        WorkflowSlotLayoutBehavior.SetSlotEnumeratorNames(this, "PART_DynamicOutputs");
        WorkflowSlotLayoutBehavior.SetCoordinateHostType(this, typeof(Panel));
    }

    /// <summary>
    /// Host for the node's bare input port and its labeled output rows. The input
    /// port (a <see cref="SlotView"/> added by <see cref="RebuildSlots"/>) is
    /// positioned at the card's left edge, vertically centered; the output rows are
    /// stacked vertically and centered as a group. The input view is added last so
    /// it paints above the row labels.
    /// </summary>
    private sealed class DynamicOutputsPanel : Panel
    {
        private SlotView? _inputView;

        public DynamicOutputsPanel()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public void SetInputView(SlotView? view)
        {
            _inputView = view;
            // Keep the stored view's actual z-order in sync with _inputView.
            // WinForms Controls.Add appends to the BACK of the z-order (index 0
            // is frontmost), so an input view added after the rows paints UNDER
            // them and gets erased by their transparent-backcolor repaint walk.
            // Reparent + move to front here so the port always paints above the
            // output rows regardless of add order.
            if (view is not null && view.Parent == this)
            {
                view.BringToFront();
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);

            // Bare input port: left edge, vertically centered — mirrors the other
            // frameworks' PART_InputSlot (a standalone glyph with no text label).
            if (_inputView is not null && _inputView.Visible)
            {
                _inputView.SetBounds(
                    4,
                    (Height - _inputView.Height) / 2,
                    _inputView.Width, _inputView.Height);
            }

            // Labeled output rows: stacked vertically, centered as a group.
            var visible = new List<Control>();
            foreach (Control child in Controls)
            {
                if (!ReferenceEquals(child, _inputView) && child.Visible) visible.Add(child);
            }
            if (visible.Count == 0) return;

            var total = 0;
            foreach (var child in visible) total += child.Height;
            var y = Math.Max(0, (Height - total) / 2);
            // Never let the row group slide up into the input-port gutter on the
            // left — the port is vertically centered at x=4, so a row that reaches
            // it would sit directly over the glyph.
            if (_inputView is { Visible: true }) y = Math.Max(y, _inputView.Height);
            foreach (var child in visible)
            {
                child.SetBounds(0, y, Width, child.Height);
                y += child.Height;
            }
        }
    }

    /// <summary>
    /// One inside-card labeled output row: a right-aligned name label plus the slot
    /// glyph on the right. All glyphs stay fully inside the card.
    /// </summary>
    private sealed class DynamicSlotRow : Panel
    {
        public Label Label { get; }
        public SlotView Slot { get; }

        public DynamicSlotRow(string name, IWorkflowSlotViewModel slot, Color foreground)
        {
            Height = 26;
            Margin = Padding.Empty;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;

            Label = new Label
            {
                Text = name,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = foreground,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = new Padding(0, 0, 8, 0),
                Font = new Font(Font.FontFamily, 8.5f, FontStyle.Regular),
            };
            Slot = new SlotView { ViewModel = slot, Width = 18, Height = 18, Margin = Padding.Empty };

            Controls.Add(Label);
            Controls.Add(Slot);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            // WinForms fires a synchronous layout pass during construction: setting
            // Height in the ctor happens before Label/Slot are assigned, so skip
            // that pass — it re-runs as soon as the row is added to PART_DynamicOutputs.
            if (Label is null || Slot is null) return;
            var slotWidth = Slot.Width;
            Slot.SetBounds(
                Width - slotWidth - 4,
                (Height - Slot.Height) / 2,
                slotWidth, Slot.Height);
            Label.SetBounds(0, 0, Width - slotWidth - 10, Height);
        }
    }

    /// <summary>
    /// Panel variant that declares <see cref="ControlStyles.SupportsTransparentBackColor"/>
    /// so it can host content that composites this card's opaque background (the header
    /// title). A plain panel cannot set a transparent BackColor.
    /// </summary>
    private sealed class TransparentPanel : Panel
    {
        public TransparentPanel()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }
    }

    /// <summary>
    /// Raised when the bound node's anchor or size changes (e.g. during a drag),
    /// after this view has repositioned itself. The tree surface subscribes to keep
    /// the minimap in sync, which otherwise only repaints on pan.
    /// </summary>
    public event Action? AnchorChanged;

    /// <summary>Gets or sets the workflow node bound to this view.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IWorkflowNodeViewModel? ViewModel
    {
        get => _node;
        set
        {
            if (ReferenceEquals(_node, value)) return;

            UnsubscribeNode();

            _node = value;
            Tag = value;

            if (value is INotifyPropertyChanged n)
            {
                _notifier = n;
                n.PropertyChanged += OnNodeChanged;
            }

            if (_node?.Slots is INotifyCollectionChanged slots)
            {
                slots.CollectionChanged += OnSlotsCollectionChanged;
            }

            RebuildSlots();
            SyncTitle();
            ApplyPosition();
            Invalidate();
        }
    }

    private void UnsubscribeNode()
    {
        if (_notifier is not null)
        {
            _notifier.PropertyChanged -= OnNodeChanged;
            _notifier = null;
        }

        if (_node?.Slots is INotifyCollectionChanged slots)
        {
            slots.CollectionChanged -= OnSlotsCollectionChanged;
        }
    }

    private void OnSlotsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new NotifyCollectionChangedEventHandler(OnSlotsCollectionChanged), sender, e);
            return;
        }

        // Slot set changed (e.g. a SlotEnumerator switched its selector type): rebuild
        // the dynamic output rows so names/glyphs match the current slot collection.
        RebuildSlots();
        ApplyPosition();
        Invalidate();
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
            AnchorChanged?.Invoke();
        }

        Invalidate();
    }

    /// <summary>
    /// Positions this card at the node anchor plus the surface pan offset, inside
    /// the coordinate host (the viewport-sized canvas). The canvas applies the
    /// pan itself, so a negative world anchor is offset back into the viewport and
    /// the card never falls off the canvas into an invisible region.
    /// </summary>
    internal void ApplyPosition()
    {
        if (_node is null || Parent is null) return;

        var pan = GetCanvasPanOffset();
        Location = new Point(
            (int)Math.Round(_node.Anchor.Horizontal) + pan.X,
            (int)Math.Round(_node.Anchor.Vertical) + pan.Y);
        Size = new Size(
            (int)Math.Round(_node.Size.Width),
            (int)Math.Round(_node.Size.Height));
    }

    /// <summary>
    /// Reads the <c>PanOffset</c> the tree surface pushes into the canvas, so this
    /// card can translate its world anchor into canvas-local coordinates. Found via
    /// reflection because the surface is a private nested control in the tree view;
    /// returns (0,0) when no such property exists (safe for a plain canvas host).
    /// </summary>
    private Point GetCanvasPanOffset()
    {
        for (var p = Parent; p is not null; p = p.Parent)
        {
            var property = p.GetType().GetProperty("PanOffset");
            if (property?.CanRead == true && property.PropertyType == typeof(Point))
            {
                return (Point)property.GetValue(p)!;
            }
        }

        return Point.Empty;
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

    /// <summary>
    /// Rebuilds the card's slot visuals from <see cref="IWorkflowNodeViewModel.Slots"/>.
    /// The node's fixed input slot renders as a bare port glyph at the card's left
    /// edge (mirroring the other frameworks' <c>PART_InputSlot</c>, which binds the
    /// node's <c>InputSlot</c> property directly). Every other slot — single and
    /// multiple/enumerated outputs — renders as a labeled row in
    /// <c>PART_DynamicOutputs</c>, so no glyph hangs off the card edge. The input is
    /// identified from the node's own <c>InputSlot</c> property rather than the slot
    /// channel, so it never renders as a mislabeled output row even when the channel
    /// is written asynchronously after binding. Runs whenever the node is bound and
    /// whenever its <c>Slots</c> collection changes (e.g. a SlotEnumerator switching
    /// its selector type).
    /// </summary>
    private void RebuildSlots()
    {
        if (IsDisposed) return;

        DisposeChildren(PART_DynamicOutputs);
        _dynamicOutputs.SetInputView(null);

        var rows = new List<DynamicSlotRow>();
        var inputSlot = ResolveInputSlot();
        SlotView? inputView = null;

        if (_node is not null)
        {
            var outputIndex = 0;
            foreach (var slot in _node.Slots)
            {
                if (inputSlot is not null && ReferenceEquals(slot, inputSlot))
                {
                    continue;
                }

                var hasSource = (slot.Channel & (SlotChannel.OneSource | SlotChannel.MultipleSources)) != 0;
                var hasTarget = (slot.Channel & (SlotChannel.OneTarget | SlotChannel.MultipleTargets)) != 0;

                if (hasSource && !hasTarget)
                {
                    // No dedicated InputSlot property on the model, but a pure input
                    // slot: treat it as the bare input port instead of a bogus row.
                    inputSlot ??= slot;
                    continue;
                }

                if (!hasTarget)
                {
                    // Unconfigured slot (no source or target capacity): skip the ghost.
                    continue;
                }

                rows.Add(new DynamicSlotRow(ResolveSlotLabel(slot, outputIndex), slot, _foreground));
                outputIndex++;
            }

            if (inputSlot is not null)
            {
                inputView = new SlotView
                {
                    ViewModel = inputSlot,
                    Width = 18,
                    Height = 18,
                    Margin = Padding.Empty,
                };
            }
        }

        PART_DynamicOutputs.SuspendLayout();
        foreach (var row in rows)
        {
            PART_DynamicOutputs.Controls.Add(row);
        }
        if (inputView is not null)
        {
            PART_DynamicOutputs.Controls.Add(inputView);
        }
        // SetInputView brings the stored view to the front of the z-order, so the
        // bare input port paints above the output rows. (A bare Controls.Add here
        // would append to the back of the z-order, putting the port UNDER the
        // rows and invisible — the rows' transparent-backcolor repaint walk erases
        // it back to the card color.)
        _dynamicOutputs.SetInputView(inputView);
        PART_DynamicOutputs.ResumeLayout();
        PART_DynamicOutputs.Invalidate();

        // The slot views may be newly created (slots can arrive after binding via
        // CollectionChanged) or re-parented; ask the layout behavior to re-sync
        // their anchors so links track the rebuilt rows.
        WorkflowSlotLayoutBehavior.Refresh(this);
    }

    /// <summary>
    /// Finds the node's fixed input slot — the model property whose value is a
    /// concrete <see cref="IWorkflowSlotViewModel"/> (a Slot property, not a
    /// <see cref="SlotEnumerator{T}"/>). This mirrors the other frameworks' node
    /// templates, which bind <c>PART_InputSlot</c> straight to the model's
    /// <c>InputSlot</c> property. It is preferred over channel-based detection
    /// because the input's channel can still be the generated default at render
    /// time: setting it runs through an asynchronous command.
    /// </summary>
    private IWorkflowSlotViewModel? ResolveInputSlot()
    {
        if (_node is null) return null;

        IWorkflowSlotViewModel? fallback = null;
        foreach (var property in _node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            IWorkflowSlotViewModel? slot;
            try
            {
                slot = property.GetValue(_node) as IWorkflowSlotViewModel;
            }
            catch
            {
                // Some generated properties throw until initialized; skip them.
                continue;
            }

            if (slot is null) continue;
            if (string.Equals(property.Name, "InputSlot", StringComparison.OrdinalIgnoreCase))
            {
                return slot;
            }

            // Only fall back to a fixed slot that is clearly the input (source-capable
            // but not target-capable); an output-only property must not become the port.
            var hasSource = (slot.Channel & (SlotChannel.OneSource | SlotChannel.MultipleSources)) != 0;
            var hasTarget = (slot.Channel & (SlotChannel.OneTarget | SlotChannel.MultipleTargets)) != 0;
            if (hasSource && !hasTarget && fallback is null)
            {
                fallback = slot;
            }
        }

        return fallback;
    }

    /// <summary>Disposes and removes every child of <paramref name="parent"/>.</summary>
    private static void DisposeChildren(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            child.Dispose();
        }
        parent.Controls.Clear();
    }

    /// <summary>
    /// Resolves the display label for an output slot. Enumerated slots carry their
    /// name on the <see cref="ConditionalSlot{T}.Name"/> of the owning
    /// <see cref="SlotEnumerator{T}"/> item, so reflect over the node's enumerator
    /// properties to find the slot identity; fall back to a <c>Name</c>/<c>Title</c>
    /// property, then a positional <c>Output N</c> label.
    /// </summary>
    private string ResolveSlotLabel(IWorkflowSlotViewModel slot, int index)
    {
        if (ReadReflectedName(slot) is { Length: > 0 } name)
        {
            return name;
        }

        var fallback = slot.GetType().GetProperty("Name") ?? slot.GetType().GetProperty("Title");
        if (fallback?.GetValue(slot)?.ToString() is { Length: > 0 } text)
        {
            return text;
        }

        return $"Output {index + 1}";
    }

    /// <summary>
    /// Searches the node view-model for <see cref="SlotEnumerator{T}"/> properties and
    /// returns the item label for <paramref name="slot"/> if one references it.
    /// </summary>
    private string? ReadReflectedName(IWorkflowSlotViewModel slot)
    {
        if (_node is null) return null;

        foreach (var property in _node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var value = property.GetValue(_node);
            if (value is null) continue;

            var enumeratorType = value.GetType();
            if (!enumeratorType.IsGenericType
                || enumeratorType.GetGenericTypeDefinition() != typeof(SlotEnumerator<>))
            {
                continue;
            }

            if (FindEnumeratorLabel(enumeratorType, value, slot) is { } label)
            {
                return label;
            }
        }

        return null;
    }

    private static string? FindEnumeratorLabel(Type enumeratorType, object enumerator, IWorkflowSlotViewModel target)
    {
        var itemsProperty = enumeratorType.GetProperty("Items");
        if (itemsProperty?.GetValue(enumerator) is not System.Collections.IEnumerable items)
        {
            return null;
        }

        foreach (var item in items)
        {
            var slotProperty = item.GetType().GetProperty("Slot");
            if (slotProperty?.GetValue(item) is not IWorkflowSlotViewModel slot
                || !ReferenceEquals(slot, target))
            {
                continue;
            }

            return item.GetType().GetProperty("Name")?.GetValue(item)?.ToString();
        }

        return null;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Draw the rounded chrome here so the card paints as a single surface.
        // The fill MUST be opaque: WinForms only alpha-composites a control's
        // background when SupportsTransparentBackColor is set, and this card
        // deliberately does NOT set it (the style would push the transparent-
        // composite walk from the children PAST this card, so row labels would
        // erase to the dark surface instead of the card). Without it, a translucent
        // fill blends over the previous double-buffer contents, so stale pixels —
        // old row labels after a SetSelector rebuild, or a card that previously
        // covered a region — bleed through as faint ghosts, and transparent
        // children (the input port glyph, the row labels) composite over garbage.
        // Erase the WHOLE card rect with an opaque backdrop first, then fill the
        // rounded body with the opaque card color, then stroke the border.
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(0, 0, Width, Height);
        using var path = RoundRect(bounds, _cornerRadius);
        using var backdrop = new SolidBrush(_cardBackdrop);
        using var brush = new SolidBrush(_opaqueBackground);
        using var pen = new Pen(_border, _borderThickness);
        g.FillRectangle(backdrop, bounds);
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
        if (disposing)
        {
            UnsubscribeNode();
        }

        base.Dispose(disposing);
    }
}
