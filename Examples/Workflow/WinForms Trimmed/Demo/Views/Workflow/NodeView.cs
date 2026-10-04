// VeloxDev customization: Customize node content, but keep PART_* names synchronized
// with WorkflowSlotLayoutBehavior (SlotNames / SlotEnumeratorNames). The adapter's WorkflowNodeAttachment owns the
// binding, the placement, the zoom collapse and the reflective title/slot lookups; this control draws the card.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>
/// A self-drawn workflow node card. The node's fixed input slot renders as a bare
/// port glyph at the card's left edge, and every output (single and enumerated)
/// renders inside the card as a labeled row — all hosted in
/// <c>PART_DynamicOutputs</c>, which <see cref="WorkflowSlotLayoutBehavior"/>
/// measures for link anchoring. No slot hangs off the card edge.
/// </summary>
public sealed class NodeView : UserControl
{
    /// <summary>Inside-card host for the bare input port and the labeled output rows.</summary>
    public Panel PART_DynamicOutputs => _dynamicOutputs;

    private readonly DynamicOutputsPanel _dynamicOutputs;
    private readonly Color _background = WorkflowNodeAttachment.ParseColor("#DDFFFFFF");
    private readonly Color _foreground = WorkflowNodeAttachment.ParseColor("#DD1E1E1E");
    private readonly Color _border = WorkflowNodeAttachment.ParseColor("#331E1E1E");
    private readonly float _borderThickness = float.Parse("1", CultureInfo.InvariantCulture);
    private readonly float _cornerRadius = float.Parse("6", CultureInfo.InvariantCulture);
    // The card paints itself fully opaque (see OnPaintBackground). The configured
    // background color is often translucent, and a translucent fill can never erase
    // the double-buffer on repaint — stale pixels (old row labels after a
    // SetSelector rebuild, or a card that previously covered a region) bleed
    // through as faint ghosts and transparent children composite over garbage.
    // _opaqueBackground is the same color with its alpha forced to 255.
    private readonly Color _opaqueBackground;
    // The host surface behind the card is opaque #1E1E1E; the rounded corners erase
    // to this so they visually match the surface (the canvas between cards is
    // transparent, so this card is the only opaque thing over it).
    private readonly Color _cardBackdrop = WorkflowNodeAttachment.ParseColor("#1E1E1E");
    // The header row (title + drag surface) — a field so zoom scaling can resize it.
    private readonly DoubleBufferedPanel _header;
    private readonly WorkflowNodeAttachment card;

    /// <summary>Gets the attachment, for a card that wants the title, the collapse or the model events.</summary>
    public WorkflowNodeAttachment Attachment => card;

    /// <summary>Gets or sets the node this card shows — the pool and the surface both bind through it.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public IWorkflowNodeViewModel? ViewModel
    {
        get => card.Node;
        set => card.Node = value;
    }

    public NodeView()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        _opaqueBackground = Color.FromArgb(255, _background.R, _background.G, _background.B);
        // Opaque background — the configured color is translucent (#DDFFFFFF), and a
        // translucent BackColor throws in .NET 10 (Control.set_BackColor requires
        // A == 0xFF without SupportsTransparentBackColor).
        BackColor = _opaqueBackground;

        // One call attaches the rest: binding, placement, zoom collapse, the model events and the reflective
        // title/slot lookups. Pointers and the card's own drawing stay yours.
        card = WorkflowNodeAttachment.Attach(this);
        card.SurfaceBackdrop = WorkflowNodeAttachment.ParseColor("#1E1E1E");
        card.Rebound += (_, _) => OnRebound();
        card.CollapseChanged += (_, e) => OnCollapse(e.Collapse);

        // Header row: title + drag surface (whole card acts as the drag handle).
        // Opaque double-buffered panel: without AllPaintingInWmPaint +
        // OptimizedDoubleBuffer the header erases its background and paints the text
        // in separate passes, which flickers while the node card moves during a drag.
        _header = new DoubleBufferedPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = _opaqueBackground,
            Name = "PART_Header",
        };

        // Subscribed after the header exists: the handler dereferences it, and a lambda written above the
        // assignment makes the compiler report the field as possibly null.
        card.TitleChanged += (_, _) => _header.Invalidate();
        _header.Paint += (_, e) =>
        {
            var g = e.Graphics;
            using var brush = new SolidBrush(_foreground);
            // Scale the title font and ellipsize so longer titles don't clip when the
            // card collapses on zoom.
            using var font = new Font(Font.FontFamily, Math.Max(5f, 10f * (float)card.Collapse), FontStyle.Bold);
            var rect = new RectangleF(12, 0, Math.Max(0, _header.Width - 24), _header.Height);
            using var format = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
            g.DrawString(card.Title, font, brush, rect, format);
        };

        // The input port and every output row render inside the card
        // (PART_DynamicOutputs). Keeping the glyphs fully inside the card (rather
        // than half-off the edges like the full demo's overlay slot buttons) avoids
        // disconnected glyphs floating over the grid.
        _dynamicOutputs = new DynamicOutputsPanel
        {
            Dock = DockStyle.Fill,
            BackColor = _opaqueBackground,
            Name = "PART_DynamicOutputs",
        };

        // Host the dynamic-outputs panel directly on the card with the card's own
        // opaque background: the transparent-composite walk that WinForms performs
        // for translucent children stops at a plain panel and paints a no-op, so
        // rebuilt rows/labels would never erase stale pixels. An opaque background
        // erases cleanly every repaint.
        Controls.Add(PART_DynamicOutputs);
        Controls.Add(_header);

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
    /// port (a <see cref="SlotView"/> added by <see cref="OnNodeRebound"/>) is
    /// positioned at the card's left edge, vertically centered; the output rows are
    /// stacked vertically and centered as a group. The input view is added last so
    /// it paints above the row labels.
    /// </summary>
    private sealed class DynamicOutputsPanel : Panel
    {
        private SlotView? _inputView;
        private double _collapse = 1d;

        /// <summary>Scales the row heights, slot glyphs and port by the zoom collapse
        /// factor so they re-flow to the collapsed card, then re-lays-out.</summary>
        public void SetCollapse(double k)
        {
            _collapse = k;
            SuspendLayout();
            foreach (Control child in Controls)
            {
                if (child is DynamicSlotRow row)
                {
                    row.ApplyScale(k);
                }
                else if (child is SlotView slot)
                {
                    var s = Math.Max(9, (int)Math.Round(18 * k));
                    slot.Width = s;
                    slot.Height = s;
                }
            }
            ResumeLayout(true);
            Invalidate();
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
                    Math.Max(2, (int)Math.Round(4 * _collapse)),
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

        public DynamicSlotRow(string name, IWorkflowSlotViewModel slot, Color foreground, Color background)
        {
            Height = 26;
            Margin = Padding.Empty;
            // Opaque row background (the card's opaque color) — no transparency.
            BackColor = background;

            Label = new Label
            {
                Text = name,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = foreground,
                BackColor = background,
                Margin = Padding.Empty,
                Padding = new Padding(0, 0, 8, 0),
                Font = new Font(Font.FontFamily, 8.5f, FontStyle.Regular),
            };
            Slot = new SlotView { ViewModel = slot, Width = 18, Height = 18, Margin = Padding.Empty };

            Controls.Add(Label);
            Controls.Add(Slot);
        }

        /// <summary>Scales the row's height, slot glyph and label font by the zoom
        /// collapse factor so the row re-flows to the collapsed card.</summary>
        public void ApplyScale(double k)
        {
            Height = Math.Max(12, (int)Math.Round(26 * k));
            var s = Math.Max(9, (int)Math.Round(18 * k));
            Slot.Width = s;
            Slot.Height = s;
            Label.Font = new Font(Font.FontFamily, Math.Max(4f, 8.5f * (float)k), FontStyle.Regular);
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
    /// Panel with full double buffering and an opaque background. The header title
    /// is painted in its <c>Paint</c> event; without AllPaintingInWmPaint +
    /// OptimizedDoubleBuffer, every repaint erases the background and draws the text
    /// in separate passes, which flickers visibly while the node card moves during
    /// a drag.
    /// </summary>
    private sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }
    }

    // 折叠因子变了：按它重排卡片内部的固定度量。
    private void OnCollapse(double collapse)
    {
        _header.Height = Math.Max(14, (int)Math.Round(36 * collapse));
        _dynamicOutputs.SetCollapse(collapse);
        // Re-measure the slot views at their new (scaled) bounds so link endpoints
        // keep pointing at the glyphs — PointToScreen reads the actual control bounds,
        // so this must run after the resize above settles. SetCollapse's
        // ResumeLayout(true) settles the panel synchronously, so SyncNow can write the
        // anchors back in this same turn — a deferred Refresh would drain only after
        // the surface's forced synchronous repaint, leaving links stale for one frame
        // during zoom collapse (both link endpoints drift).
        WorkflowSlotLayoutBehavior.SyncNow(this);
        Invalidate();
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
    /// is written asynchronously after binding.
    /// </summary>
    private void OnRebound()
    {
        if (IsDisposed) return;

        WorkflowNodeAttachment.DisposeChildren(PART_DynamicOutputs);
        _dynamicOutputs.SetInputView(null);

        var rows = new List<DynamicSlotRow>();
        var inputSlot = card.ResolveInputSlot();
        SlotView? inputView = null;
        var node = ViewModel;

        if (node is not null)
        {
            var outputIndex = 0;
            foreach (var slot in node.Slots)
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

                rows.Add(new DynamicSlotRow(card.ResolveSlotLabel(slot, outputIndex), slot, _foreground, _opaqueBackground));
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

    /// <inheritdoc />
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Draw the rounded chrome here so the card paints as a single surface.
        // The fill is fully opaque — no SupportsTransparentBackColor anywhere, so
        // WinForms never runs a transparent-composite walk through the card. The
        // opaque fill erases the double buffer cleanly every repaint: stale pixels
        // (old row labels after a SetSelector rebuild, or a card that previously
        // covered a region) cannot bleed through as ghosts. Erase the WHOLE card
        // rect with an opaque backdrop first, then fill the rounded body with the
        // opaque card color, then stroke the border.
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(0, 0, Width, Height);
        using var path = WorkflowSurfaceGraphics.RoundedRectangle(bounds, _cornerRadius);
        using var backdrop = new SolidBrush(_cardBackdrop);
        using var brush = new SolidBrush(_opaqueBackground);
        using var pen = new Pen(_border, _borderThickness);
        g.FillRectangle(backdrop, bounds);
        g.FillPath(brush, path);
        g.DrawPath(pen, path);
    }
}
