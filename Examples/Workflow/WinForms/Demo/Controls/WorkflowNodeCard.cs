using Demo.ViewModels;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;
// `Size` collides between System.Drawing and VeloxDev.WorkflowSystem; a drawing
// alias keeps `new Size(24, 24)` and `node.Size` unambiguous in the same file.
using Size = System.Drawing.Size;

namespace Demo.Controls;

/// <summary>
/// Card control for a single workflow node.
///
/// The look is the Avalonia demo's node design, and it is authored entirely at the node type's
/// <c>[DefaultSize]</c> (Controller 230×170, Timer 200×140, Python 280×260, Enum 280×380) with every colour and
/// metric taken from <see cref="Views.CardTheme"/> — the same one-place-per-token arrangement as that demo's
/// <c>CardTheme.axaml</c>. The canvas collapses a node by shrinking its bounds, so the interior is re-scaled
/// uniformly by <c>k = current / design</c> on every layout instead of being re-authored per zoom step.
///
/// WinForms has none of the controls the reference's cards are built from — a rounded, hairline-bordered box —
/// so the three shapes that need one are drawn by hand: <see cref="Views.FramePanel"/> for the fields, the
/// script editor and its header, the status capsule and the type accent bar; <see cref="Views.GhostButton"/>
/// for the action row. Everything else is a plain label, panel or table layout.
/// </summary>
internal sealed class WorkflowNodeCard : UserControl
{
    // ── Layout ──────────────────────────────────────────────────────────────────
    private readonly TableLayoutPanel _rootLayout;
    private readonly Panel _headerPanel;
    private readonly Panel _bodyPanel;
    private readonly Panel _footerPanel;
    private readonly Panel _headerDivider;
    private readonly Panel _footerDivider;

    // ── Uniform design-coordinate scaling ────────────────────────────────────────
    private float _designW;
    private float _designH;
    private double _k = 1d;
    private bool _layoutReady;
    private bool _applyingScale;

    // ── ViewModel subscription ────────────────────────────────────────────────────
    private IWorkflowNodeViewModel? _node;
    private INotifyPropertyChanged? _nodeNotifier;
    private bool _updatingFromVm;

    // ── Dynamic control references ────────────────────────────────────────────────
    private Views.FramePanel? _accentBar;
    private Label? _titleLabel;
    private Label? _orderBadge;
    private Label? _capsuleLabel;
    private Views.FramePanel? _capsule;
    private TextBox? _seedBox;
    private Views.GhostButton? _runButton;
    private ComboBox? _enumCombo;
    private ComboBox? _routerModeCombo;
    private TableLayoutPanel? _outputSlotsLayout;
    private readonly List<(Label label, Views.SlotView slot)> _dynamicSlotRows = [];
    private TableLayoutPanel? _inputSlotsLayout;
    private TextBox? _scriptBox;
    private Label? _descriptionLabel;
    private readonly List<Views.SlotView> _pythonSlotRows = [];
    private TextBox? _intervalBox;
    private Label? _tickLabel;

    // ── Main slot buttons (edge-anchored slot views; screen position computed by WorkflowCanvas) ────
    internal Views.SlotView? InputSlotButton { get; private set; }
    internal Views.SlotView? OutputSlotButton { get; private set; }

    /// <summary>
    /// Every port this card owns. Each one rides the edge of the container that holds it — the card itself or
    /// one of the port strips — so half of every glyph falls outside that container and WinForms, which clips a
    /// child window to its parent, can never paint it. The canvas draws that half; see
    /// <see cref="Views.SlotView"/>'s remarks.
    /// </summary>
    internal IEnumerable<Views.SlotView> Ports() => EnumeratePorts(this);

    private static IEnumerable<Views.SlotView> EnumeratePorts(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Views.SlotView slot) yield return slot;
            foreach (var nested in EnumeratePorts(child)) yield return nested;
        }
    }

    /// <summary>The bound node view model.</summary>
    internal IWorkflowNodeViewModel? ViewModel => _node;

    // ── Constructor ──────────────────────────────────────────────────────────────────
    internal WorkflowNodeCard()
    {
        DoubleBuffered = true;
        BackColor = Views.CardTheme.Ground;
        Padding = new Padding(1);
        Margin = Padding.Empty;
        WorkflowBehaviors.WorkflowNodeDragBehavior.SetIsEnabled(this, true);
        WorkflowBehaviors.WorkflowNodeDragBehavior.SetCoordinateHostType(this, typeof(WorkflowCanvas));
        WorkflowBehaviors.WorkflowSlotLayoutBehavior.SetIsEnabled(this, true);
        WorkflowBehaviors.WorkflowSlotLayoutBehavior.SetCoordinateHostType(this, typeof(WorkflowCanvas));

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        _rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(1),
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Views.CardTheme.Surface,
        };
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        _headerPanel = MakeSection();
        _bodyPanel = MakeSection();
        _footerPanel = MakeSection();
        _headerDivider = MakeDivider();
        _footerDivider = MakeDivider();

        Controls.Add(_rootLayout);
        _rootLayout.Controls.Add(_headerPanel, 0, 0);
        _rootLayout.Controls.Add(_headerDivider, 0, 1);
        _rootLayout.Controls.Add(_bodyPanel, 0, 2);
        _rootLayout.Controls.Add(_footerDivider, 0, 3);
        _rootLayout.Controls.Add(_footerPanel, 0, 4);
    }

    // ── Public binding API ──────────────────────────────────────────────────────────

    /// <summary>Binds the card to a new node view model.</summary>
    internal void Bind(IWorkflowNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (ReferenceEquals(_node, node)) { Refresh(node); return; }

        UnsubscribeVm();
        _node = node;
        Tag = node;
        SetDesignSize(node);
        // Build at design (k=1) and let the first layout after the canvas sizes the card re-scale to the
        // current collapsed factor. Guards against a card being bound while the workspace is already zoomed.
        _k = 1d;
        _layoutReady = false;

        if (node is INotifyPropertyChanged n)
        {
            _nodeNotifier = n;
            n.PropertyChanged += OnNodePropertyChanged;
        }

        BuildLayout(node);
        Refresh(node);
        _layoutReady = true;
    }

    /// <summary>Unbinds and resets the card to an empty state.</summary>
    internal void Unbind()
    {
        UnsubscribeVm();
        _node = null;
        Tag = null;
        ClearSlotButtons();
        _headerPanel.Controls.Clear();
        _bodyPanel.Controls.Clear();
        _footerPanel.Controls.Clear();
        _footerPanel.Visible = false;
        _footerDivider.Visible = false;
        _dynamicSlotRows.Clear();
        ResetRefs();
    }

    /// <summary>Refreshes all displayed values from the view model (without rebuilding the layout).</summary>
    internal void Refresh(IWorkflowNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _updatingFromVm = true;
        try
        {
            switch (node)
            {
                case ControllerViewModel c: ApplyController(c); break;
                case EnumSelectorNodeViewModel e: ApplyEnumSelector(e); break;
                case PythonScriptNodeViewModel p: ApplyPython(p); break;
                case TimerNodeViewModel t: ApplyTimer(t); break;
                default: ApplyFallback(); break;
            }
        }
        finally
        {
            _updatingFromVm = false;
        }

        RefreshVisual();
    }

    /// <summary>
    /// The reference card is one surface whose identity is carried by its type accent bar, so there is no
    /// per-state border or header tint to re-apply here — only the repaint the canvas asks for after a node
    /// property changed.
    /// </summary>
    internal void RefreshVisual() => Invalidate();

    // ── Drawing (rounded surface + type accent) ───────────────────────────────────────
    /// <summary>
    /// The card's own chrome: a single dark surface inside a hairline and an 8-unit corner, painted in
    /// <c>OnPaintBackground</c> so the section panels and any transparent child label sit on it.
    /// </summary>
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Views.CardTheme.Ground);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using var path = Views.CardTheme.RoundedPath(rect, F(CardRadius));
        using (var fill = new SolidBrush(Views.CardTheme.Surface))
        {
            g.FillPath(fill, path);
        }

        using var pen = new Pen(Views.CardTheme.Border, 1f);
        g.DrawPath(pen, path);
    }

    private static float CardRadius => Views.CardTheme.CardRadius;

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_layoutReady && !_applyingScale)
        {
            ApplyScaleToCurrent();
        }

        PositionOverlaySlotButtons();
    }

    /// <summary>
    /// Positions the edge-anchored ports so their centre lands exactly on the card's left/right edge — half of
    /// each port rides the card, which is what the design asks for. Their size is taken from the port's design
    /// size (32) times the current scale rather than from whatever size the port happens to have, so a round trip
    /// through a small zoom cannot drift.
    /// </summary>
    private void PositionOverlaySlotButtons()
    {
        if (InputSlotButton is not null)
        {
            SizeSlot(InputSlotButton);
            InputSlotButton.Location = new Point(
                -(InputSlotButton.Width / 2),
                (Height - InputSlotButton.Height) / 2);
        }

        if (OutputSlotButton is not null)
        {
            SizeSlot(OutputSlotButton);
            OutputSlotButton.Location = new Point(
                Width - (OutputSlotButton.Width / 2),
                (Height - OutputSlotButton.Height) / 2);
        }
    }

    /// <summary>Resizes one port from its design size at the current uniform scale.</summary>
    private void SizeSlot(Views.SlotView slot)
    {
        var s = Math.Max(9, (int)Math.Round(slot.DesignSize * K));
        slot.Size = new Size(s, s);
    }

    // ── Uniform scale application ─────────────────────────────────────────────────
    /// <summary>
    /// Records the card's design (scale-1) dimensions for the bound node type, taken from the type's
    /// <c>[DefaultSize]</c> attribute — the same value seven demos share, so a card can never disagree with the
    /// box the canvas gives it (a stale copy of this table is exactly how this card ended up 220×340 for a
    /// 230×170 node, and its buttons clipped).
    /// </summary>
    private void SetDesignSize(IWorkflowNodeViewModel node)
    {
        var attribute = node.GetType()
            .GetCustomAttribute<DefaultSizeAttribute>(inherit: false);

        _designW = attribute is { Width: > 0 } ? (float)attribute.Width : 220f;
        _designH = attribute is { Height: > 0 } ? (float)attribute.Height : 170f;
    }

    /// <summary>Recomputes the current uniform factor k = collapsed / design and re-scales the interior.</summary>
    private void ApplyScaleToCurrent()
    {
        if (_designW <= 0 || _designH <= 0) return;
        var w = Width;
        var h = Height;
        if (w <= 0 || h <= 0) return;

        // The node collapses uniformly (node.Size = [DefaultSize] × collapse); the smaller ratio is
        // used so the proportional interior always fits inside the host box on both axes.
        var k = Math.Min(w / (double)_designW, h / (double)_designH);
        if (k <= 0) return;
        ApplyScale(k);
    }

    /// <summary>Scales the existing interior metrics (fonts, row/column absolute styles, margins,
    /// paddings, minimums, in-card slot glyphs) by the ratio to the requested uniform factor k.</summary>
    private void ApplyScale(double k)
    {
        if (_rootLayout is null || _applyingScale) return;
        _applyingScale = true;
        try
        {
            var r = k / _k;
            _k = k;
            if (Math.Abs(r - 1d) < 0.001d) return;

            _rootLayout.SuspendLayout();
            try
            {
                ScaleControlTree(_rootLayout, r, k);
            }
            finally
            {
                // The card's hairline is one device pixel at every zoom, so its inset is not scaled.
                _rootLayout.Padding = new Padding(1);
                _rootLayout.ResumeLayout(true);
            }

            ResizeCapsule();
            foreach (var port in Ports()) SizeSlot(port);
        }
        finally
        {
            _applyingScale = false;
        }

        Invalidate();
    }

    private static void ScaleControlTree(Control c, double r, double k)
    {
        if (c.IsDisposed) return;

        if (c is TableLayoutPanel tlp)
        {
            foreach (ColumnStyle cs in tlp.ColumnStyles)
            {
                if (cs.SizeType == SizeType.Absolute) cs.Width = Math.Max(1f, cs.Width * (float)r);
            }

            foreach (RowStyle rs in tlp.RowStyles)
            {
                if (rs.SizeType == SizeType.Absolute) rs.Height = Math.Max(1f, rs.Height * (float)r);
            }
        }

        // Text-bearing controls scale with the card so rows/fonts stay proportional.
        if (c is Label or Button or TextBox or ComboBox)
        {
            var f = c.Font;
            if (f is not null)
            {
                c.Font = Views.CardTheme.Font(f.SizeInPoints * (float)r, f.Style);
            }
        }

        // Hand-drawn shapes carry their own metrics, which a Font change cannot reach.
        if (c is Views.FramePanel frame)
        {
            frame.Radius *= (float)r;
            frame.FrameWidth = Math.Max(0f, frame.FrameWidth * (float)r);
        }

        if (c is Views.GhostButton ghost)
        {
            ghost.Radius *= (float)r;
            ghost.FrameWidth = Math.Max(0f, ghost.FrameWidth * (float)r);
        }

        // In-card port glyphs scale from their DESIGN size (24 × k), so a round-trip to a tiny zoom cannot
        // drift via the minimum clamp. Edge-anchored ports live OUTSIDE _rootLayout and are sized by
        // PositionOverlaySlotButtons, which the canvas drives.
        if (c is Views.SlotView sv)
        {
            var s = Math.Max(9, (int)Math.Round(sv.DesignSize * k));
            sv.Size = new Size(s, s);
        }

        if (c.Padding != Padding.Empty)
            c.Padding = ScalePadding(c.Padding, r);
        if (c.Margin != Padding.Empty)
            c.Margin = ScalePadding(c.Margin, r);
        if (c.MinimumSize != Size.Empty)
            c.MinimumSize = ScaleSize(c.MinimumSize, r);
        if (c.MaximumSize != Size.Empty)
            c.MaximumSize = ScaleSize(c.MaximumSize, r);

        foreach (Control child in c.Controls)
        {
            ScaleControlTree(child, r, k);
        }
    }

    private static Padding ScalePadding(Padding p, double r)
        => new(
            Math.Max(0, (int)Math.Round(p.Left * r)),
            Math.Max(0, (int)Math.Round(p.Top * r)),
            Math.Max(0, (int)Math.Round(p.Right * r)),
            Math.Max(0, (int)Math.Round(p.Bottom * r)));

    private static Size ScaleSize(Size s, double r)
        => new(
            Math.Max(0, (int)Math.Round(s.Width * r)),
            Math.Max(0, (int)Math.Round(s.Height * r)));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UnsubscribeVm();
        }

        base.Dispose(disposing);
    }

    // ── Slot button management ──────────────────────────────────────────────────────────

    private Views.SlotView AddSlotButton(IWorkflowSlotViewModel? slot, int designSize)
    {
        var btn = new Views.SlotView { DesignSize = designSize };
        btn.ViewModel = slot;
        // 端口骑在卡边上，外溢的那一半由画布绘制（WinForms 会把子窗口裁到父窗口的客户区），
        // 所以端口的每一帧与每次状态变化都要让画布跟着重绘
        btn.ExternalInvalidate = () => { if (!IsDisposed) Parent?.Invalidate(); };
        SizeSlot(btn);
        Controls.Add(btn);
        btn.BringToFront();
        return btn;
    }

    private void ClearSlotButtons()
    {
        if (InputSlotButton is not null)
        {
            Controls.Remove(InputSlotButton);
            InputSlotButton.Dispose();
            InputSlotButton = null;
        }

        if (OutputSlotButton is not null)
        {
            Controls.Remove(OutputSlotButton);
            OutputSlotButton.Dispose();
            OutputSlotButton = null;
        }

        foreach (var (_, slot) in _dynamicSlotRows)
        {
            slot.Dispose();
        }

        foreach (var slot in _pythonSlotRows)
        {
            slot.Dispose();
        }
        _pythonSlotRows.Clear();
    }

    // ── Layout building ──────────────────────────────────────────────────────────────

    private void BuildLayout(IWorkflowNodeViewModel node)
    {
        ClearSlotButtons();
        ResetRefs();
        _dynamicSlotRows.Clear();
        _headerPanel.Controls.Clear();
        _bodyPanel.Controls.Clear();
        _footerPanel.Controls.Clear();
        // The sections are shared across the five layouts: a padding the previous type set would otherwise
        // survive into the next one (the Controller pads its body, and the port strips must not inherit it —
        // their ports ride the card's edge, and an inset strip takes those ports with it).
        _bodyPanel.Padding = Padding.Empty;
        _footerPanel.Padding = Padding.Empty;

        switch (node)
        {
            case ControllerViewModel: BuildController(); break;
            case EnumSelectorNodeViewModel: BuildEnumSelector(); break;
            case PythonScriptNodeViewModel: BuildPython(); break;
            case TimerNodeViewModel: BuildTimer(); break;
            default: BuildFallback(); break;
        }
    }

    private void SetRows(float headerH, float footerH, bool showFooter)
    {
        _rootLayout.SuspendLayout();
        _rootLayout.RowStyles.Clear();
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, F(headerH)));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, F(Views.CardTheme.DividerThickness)));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, F(Views.CardTheme.DividerThickness)));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, F(footerH)));
        _footerPanel.Visible = showFooter;
        _footerDivider.Visible = showFooter;
        _rootLayout.ResumeLayout();
    }

    /// <summary>
    /// The title row: a 2-unit type accent bar, the left-aligned title, and the card's right-hand readouts
    /// (the execution order and, where the node reports one, a neutral status capsule).
    /// </summary>
    private void BuildHeader(Color accent)
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Views.CardTheme.Surface,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Math.Max(1, S(Views.CardTheme.AccentWidth))));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        _accentBar = new Views.FramePanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Fill = accent,
            Frame = Color.Transparent,
            FrameWidth = 0f,
            Radius = F(Views.CardTheme.CardRadius),
            Corners = Views.CardCorners.TopLeft,
        };
        header.Controls.Add(_accentBar, 0, 0);

        _titleLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            BackColor = Color.Transparent,
            ForeColor = Views.CardTheme.Title,
            Font = Views.CardTheme.Font(Views.CardTheme.TitleSize, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(S(Views.CardTheme.BodyPaddingX), 0, S(6), 0),
        };
        header.Controls.Add(_titleLabel, 1, 0);

        var badges = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Views.CardTheme.Surface,
        };

        _capsuleLabel = new Label
        {
            AutoSize = true,
            BackColor = Views.CardTheme.Hover,
            ForeColor = Views.CardTheme.BadgeText,
            Font = Views.CardTheme.Font(Views.CardTheme.CapsuleSize),
            Margin = Padding.Empty,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _capsule = new Views.FramePanel
        {
            Size = new Size(S(40), S(16)),
            Fill = Views.CardTheme.Hover,
            Frame = Views.CardTheme.Border,
            Radius = F(Views.CardTheme.CapsuleRadius),
            Padding = new Padding(S(Views.CardTheme.CapsulePaddingX), S(Views.CardTheme.CapsulePaddingY),
                S(Views.CardTheme.CapsulePaddingX), S(Views.CardTheme.CapsulePaddingY)),
            Margin = new Padding(S(12), 0, S(12), 0),
            Visible = false,
        };
        _capsuleLabel.Location = new Point(S(Views.CardTheme.CapsulePaddingX), S(Views.CardTheme.CapsulePaddingY));
        _capsule.Controls.Add(_capsuleLabel);

        _orderBadge = new Label
        {
            AutoSize = true,
            BackColor = Color.Transparent,
            ForeColor = Views.CardTheme.ActionRun,
            Font = Views.CardTheme.Font(Views.CardTheme.ValueSize),
            Margin = new Padding(0, 0, S(12), 0),
            Visible = false,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        // RightToLeft flow puts the first child added at the far right (the capsule), then the order text.
        badges.Controls.Add(_capsule);
        badges.Controls.Add(_orderBadge);
        header.Controls.Add(badges, 2, 0);

        _headerPanel.Controls.Add(header);
    }

    // ── Layout: Controller ──────────────────────────────────────────────────────
    private void BuildController()
    {
        BuildHeader(Views.CardTheme.AccentController);
        SetRows(Views.CardTheme.HeaderHeight, Views.CardTheme.ControllerFooterHeight, showFooter: true);

        _bodyPanel.Padding = new Padding(S(Views.CardTheme.BodyPaddingX), S(Views.CardTheme.BodyPaddingY),
            S(Views.CardTheme.BodyPaddingX), S(Views.CardTheme.BodyPaddingY));

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Views.CardTheme.Surface,
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, F(28)));
        body.Controls.Add(MakeLabel("SEED", Views.CardTheme.LabelSize, FontStyle.Regular, Views.CardTheme.Label), 0, 0);

        _seedBox = MakeField();
        _seedBox.TextChanged += OnSeedTextChanged;
        body.Controls.Add(MakeFieldHost(_seedBox), 0, 1);
        _bodyPanel.Controls.Add(body);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(S(9), S(4), S(9), S(4)),
            BackColor = Views.CardTheme.Surface,
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        footer.Controls.Add(MakeGhostButton("Compile", nameof(ControllerViewModel.CompileCommand), Views.CardTheme.ActionCompile), 0, 0);
        _runButton = MakeGhostButton("Run", nameof(ControllerViewModel.RunCommand), Views.CardTheme.ActionRun);
        footer.Controls.Add(_runButton, 1, 0);
        footer.Controls.Add(MakeGhostButton("Stop", nameof(ControllerViewModel.StopCommand), Views.CardTheme.ActionStop), 0, 1);
        footer.Controls.Add(MakeGhostButton("Close", nameof(ControllerViewModel.CloseWorkflowCommand), Views.CardTheme.ActionClose), 1, 1);
        _footerPanel.Controls.Add(footer);

        OutputSlotButton = AddSlotButton(null, designSize: 32);
    }

    // ── Layout: EnumSelector ────────────────────────────────────────────────────
    private void BuildEnumSelector()
    {
        BuildHeader(Views.CardTheme.AccentEnum);
        SetRows(Views.CardTheme.HeaderHeight, 0F, showFooter: false);
        _capsuleLabel!.ForeColor = Views.CardTheme.AccentEnum;
        _capsuleLabel.Font = Views.CardTheme.Font(Views.CardTheme.CapsuleSize, FontStyle.Bold);

        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Views.CardTheme.Surface,
        };

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 6,
            Margin = Padding.Empty,
            Padding = new Padding(S(14), S(10), 0, S(10)),
            BackColor = Views.CardTheme.Surface,
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        // Labels size to their text; the two drop-downs get the same fixed field height the text fields have, so
        // the panel hosting them has a height to lay the combo out inside.
        for (var i = 0; i < 6; i++)
        {
            body.RowStyles.Add(i is 1 or 3
                ? new RowStyle(SizeType.Absolute, F(28))
                : new RowStyle(SizeType.AutoSize));
        }

        body.Controls.Add(MakeLabel("SELECTED METHOD", Views.CardTheme.LabelSize, FontStyle.Regular, Views.CardTheme.Label), 0, 0);
        _enumCombo = MakeComboBox();
        _enumCombo.SelectedIndexChanged += OnEnumValueChanged;
        body.Controls.Add(MakeComboField(_enumCombo), 0, 1);
        body.Controls.Add(MakeLabel("COMPILE MODE", Views.CardTheme.LabelSize, FontStyle.Regular, Views.CardTheme.Label, new Padding(0, S(4), 0, 0)), 0, 2);
        _routerModeCombo = MakeComboBox();
        _routerModeCombo.SelectedIndexChanged += OnRouterModeChanged;
        body.Controls.Add(MakeComboField(_routerModeCombo), 0, 3);
        body.Controls.Add(MakeLabel("OUTPUT SLOTS", Views.CardTheme.LabelSize, FontStyle.Regular, Views.CardTheme.Label, new Padding(0, S(6), 0, 0)), 0, 4);

        _outputSlotsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Views.CardTheme.Surface,
        };
        _outputSlotsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _outputSlotsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Math.Max(1, S(24))));
        body.Controls.Add(_outputSlotsLayout, 0, 5);

        scroll.Controls.Add(body);
        _bodyPanel.Controls.Add(scroll);

        InputSlotButton = AddSlotButton(null, designSize: 32);
    }

    // ── Layout: Python node ───────────────────────────────────────────────────────
    private void BuildPython()
    {
        BuildHeader(Views.CardTheme.AccentAgent);
        SetRows(Views.CardTheme.HeaderHeight, 0F, showFooter: false);
        _capsuleLabel!.ForeColor = Views.CardTheme.BadgeText;

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Views.CardTheme.Surface,
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));            // description
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, F(Views.CardTheme.DividerThickness)));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));       // editor row

        _descriptionLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            BackColor = Views.CardTheme.Surface,
            ForeColor = Views.CardTheme.Label,
            Font = Views.CardTheme.Font(Views.CardTheme.CapsuleSize),
            TextAlign = ContentAlignment.TopLeft,
            Padding = new Padding(S(12), S(5), S(12), S(5)),
            Margin = Padding.Empty,
        };
        body.Controls.Add(_descriptionLabel, 0, 0);
        body.Controls.Add(MakeDivider(), 0, 1);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Views.CardTheme.Surface,
        };
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, F(64)));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, F(64)));

        _inputSlotsLayout = MakePortStripLayout(inputPortsLeft: true);
        grid.Controls.Add(MakePortStrip(_inputSlotsLayout), 0, 0);

        grid.Controls.Add(BuildScriptEditor(), 1, 0);

        _outputSlotsLayout = MakePortStripLayout(inputPortsLeft: false);
        grid.Controls.Add(MakePortStrip(_outputSlotsLayout), 2, 0);

        body.Controls.Add(grid, 0, 2);
        _bodyPanel.Controls.Add(body);
    }

    /// <summary>The script editor: its own near-black surface inside a hairline, with a titled header strip.</summary>
    private Control BuildScriptEditor()
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(S(4), S(6), S(4), S(6)),
            Padding = Padding.Empty,
            BackColor = Views.CardTheme.Surface,
        };

        var frame = new Views.FramePanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Fill = Views.CardTheme.EditorSurface,
            Frame = Views.CardTheme.Divider,
            Radius = F(Views.CardTheme.FieldRadius),
        };

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = S(22),
            BackColor = Views.CardTheme.EditorChrome,
            Padding = new Padding(S(8), S(4), S(8), 0),
        };
        var file = new Label
        {
            AutoSize = true,
            BackColor = Color.Transparent,
            ForeColor = Views.CardTheme.EditorFile,
            Font = Views.CardTheme.Mono(11f, FontStyle.Bold),
            Text = "script.py",
            Location = new Point(S(8), S(4)),
        };
        var meta = new Label
        {
            AutoSize = true,
            BackColor = Color.Transparent,
            ForeColor = Views.CardTheme.EditorMeta,
            Font = Views.CardTheme.Font(Views.CardTheme.LabelSize),
            Text = "Python 3",
            Location = new Point(S(8), S(4)),
        };
        header.Controls.Add(file);
        header.Controls.Add(meta);
        header.Resize += (_, _) => meta.Location = new Point(Math.Max(S(8), header.Width - meta.Width - S(8)), S(4));

        _scriptBox = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            BorderStyle = BorderStyle.None,
            Font = Views.CardTheme.Mono(12f),
            BackColor = Views.CardTheme.EditorSurface,
            ForeColor = Views.CardTheme.Value,
            Margin = new Padding(0),
        };
        _scriptBox.TextChanged += OnScriptTextChanged;

        // A text box's scrollbars are non-client and follow the system theme: on a dark card they are the one
        // opaque white L in the whole design. Sizing the box past its viewport puts them outside the clip
        // instead of on show, which leaves the near-black surface the reference asks for intact; the wheel and
        // the caret still scroll the text, and the visible text area is exactly the viewport's width.
        var viewport = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Views.CardTheme.EditorSurface,
        };
        viewport.Controls.Add(_scriptBox);
        viewport.Layout += (_, _) =>
        {
            var scrollbar = SystemInformation.VerticalScrollBarWidth;
            _scriptBox.Bounds = new Rectangle(
                0, 0,
                viewport.ClientSize.Width + scrollbar,
                viewport.ClientSize.Height + scrollbar);
        };

        // Dock order matters: the header takes the top strip, the editor viewport fills what is left.
        frame.Controls.Add(viewport);
        frame.Controls.Add(header);
        host.Controls.Add(frame);
        return host;
    }

    // ── Layout: Timer node ─────────────────────────────────────────────────────────
    private void BuildTimer()
    {
        BuildHeader(Views.CardTheme.AccentAgent);
        SetRows(Views.CardTheme.HeaderHeight, 0F, showFooter: false);

        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Views.CardTheme.Surface,
        };

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty,
            Padding = new Padding(S(Views.CardTheme.BodyPaddingX), S(Views.CardTheme.BodyPaddingY),
                S(Views.CardTheme.BodyPaddingX), S(Views.CardTheme.BodyPaddingY)),
            BackColor = Views.CardTheme.Surface,
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, F(28)));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        body.Controls.Add(MakeLabel("INTERVAL (MS)", Views.CardTheme.LabelSize, FontStyle.Regular, Views.CardTheme.Label), 0, 0);
        _intervalBox = MakeField();
        _intervalBox.TextChanged += OnIntervalTextChanged;
        body.Controls.Add(MakeFieldHost(_intervalBox), 0, 1);
        body.Controls.Add(MakeLabel("LAST TICK", Views.CardTheme.LabelSize, FontStyle.Regular, Views.CardTheme.Label, new Padding(0, S(4), 0, 0)), 0, 2);

        _tickLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ForeColor = Views.CardTheme.AccentAgent,
            Font = Views.CardTheme.Font(Views.CardTheme.ValueSize, FontStyle.Bold),
            TextAlign = ContentAlignment.TopLeft,
            Margin = Padding.Empty,
            Height = S(16),
        };
        body.Controls.Add(_tickLabel, 0, 3);

        scroll.Controls.Add(body);
        _bodyPanel.Controls.Add(scroll);

        InputSlotButton = AddSlotButton(null, designSize: 32);
        OutputSlotButton = AddSlotButton(null, designSize: 32);
    }

    // ── Layout: fallback ───────────────────────────────────────────────────────────
    /// <summary>
    /// The fallback card, for a node type this demo ships no view of its own for — the reference's
    /// <c>NodeView</c>. Bare on purpose: the framework's <see cref="IWorkflowNodeViewModel"/> carries geometry
    /// and slots and no display name, so this card cannot honestly label a node it knows nothing else about, and
    /// the accent is the neutral slate because there is no type here to give it a colour.
    /// </summary>
    private void BuildFallback()
    {
        BuildHeader(Views.CardTheme.AccentFallback);
        SetRows(Views.CardTheme.HeaderHeight, 0F, showFooter: false);
        if (_titleLabel is not null)
        {
            _titleLabel.Text = "?";
            _titleLabel.Font = Views.CardTheme.Font(Views.CardTheme.LabelSize);
            _titleLabel.ForeColor = Views.CardTheme.Label;
        }
    }

    // ── Data application ──────────────────────────────────────────────────────────────
    private void ApplyController(ControllerViewModel c)
    {
        SetText(_titleLabel, "Network Flow Controller");
        SetText(_seedBox, c.SeedPayload);
        // 参考实现里 Run 只在编译出了图之后可用；这条也正是「幽灵按钮的禁用两态」唯一会露出来的地方
        if (_runButton is not null && _runButton.Enabled != c.HasCompiledGraphs)
        {
            _runButton.Enabled = c.HasCompiledGraphs;
        }

        OutputSlotButton!.ViewModel = c.OutputSlot;
    }

    private void ApplyFallback() => SetText(_titleLabel, "?");

    private static void PopulateCombo<T>(ComboBox? combo, T[] items, T selected)
    {
        if (combo is null) return;
        combo.Items.Clear();
        foreach (var item in items)
        {
            if (item is not null)
                _ = combo.Items.Add(item);
        }
        combo.SelectedItem = selected;
    }

    /// <summary>Populates the route compile-mode dropdown (shared by router cards).</summary>
    private void PopulateRouterModeCombo(RouterCompileMode selected)
    {
        if (_routerModeCombo is null) return;
        _routerModeCombo.Items.Clear();
        foreach (var m in new[] { RouterCompileMode.Static, RouterCompileMode.Dynamic })
            _routerModeCombo.Items.Add(m);
        _routerModeCombo.SelectedItem = selected;
    }

    private void ApplyEnumSelector(EnumSelectorNodeViewModel e)
    {
        SetText(_titleLabel, e.Title);
        SetCapsuleText(e.LastRouted, !string.IsNullOrEmpty(e.LastRouted) && e.LastRouted != "-");
        SetText(_orderBadge, e.ExecutionOrderText);
        SetVisible(_orderBadge, e.HasExecutionOrder);
        InputSlotButton!.ViewModel = e.InputSlot;
        UpdateEnumCombo(e);
        RebuildEnumSlots(e);
    }

    private void ApplyPython(PythonScriptNodeViewModel p)
    {
        SetText(_titleLabel, p.Title);
        SetCapsuleText(p.LastStatus, !string.IsNullOrEmpty(p.LastStatus) && p.LastStatus != "Idle");
        SetText(_orderBadge, p.ExecutionOrderText);
        SetVisible(_orderBadge, p.HasExecutionOrder);
        SetText(_descriptionLabel, p.Description);
        SetText(_scriptBox, p.Script);
        RebuildPythonSlots(p);
    }

    private void ApplyTimer(TimerNodeViewModel t)
    {
        SetText(_titleLabel, t.Title);
        SetText(_orderBadge, t.ExecutionOrderText);
        SetVisible(_orderBadge, t.HasExecutionOrder);
        SetText(_intervalBox, t.IntervalMilliseconds.ToString(CultureInfo.InvariantCulture));
        SetText(_tickLabel, t.LastTick);
        InputSlotButton!.ViewModel = t.InputSlot;
        OutputSlotButton!.ViewModel = t.OutputSlot;
    }

    /// <summary>Rebuilds the Python node's dynamic input/output slot rows (reuses existing SlotViews when the count matches).</summary>
    private void RebuildPythonSlots(PythonScriptNodeViewModel p)
    {
        if (_inputSlotsLayout is null || _outputSlotsLayout is null) return;

        var inputs = p.InputSlots?.Items
            .Select((item, i) => (Name: string.IsNullOrWhiteSpace(item.Name) ? $"In {i + 1}" : item.Name, Slot: (IWorkflowSlotViewModel?)item.Slot))
            .ToArray() ?? [];
        var outputs = p.OutputSlots?.Items
            .Select((item, i) => (Name: string.IsNullOrWhiteSpace(item.Name) ? $"Out {i + 1}" : item.Name, Slot: (IWorkflowSlotViewModel?)item.Slot))
            .ToArray() ?? [];

        if (_pythonSlotRows.Count == inputs.Length + outputs.Length)
        {
            for (var i = 0; i < inputs.Length; i++) _pythonSlotRows[i].ViewModel = inputs[i].Slot;
            for (var i = 0; i < outputs.Length; i++) _pythonSlotRows[inputs.Length + i].ViewModel = outputs[i].Slot;
            return;
        }

        foreach (var s in _pythonSlotRows)
        {
            _inputSlotsLayout.Controls.Remove(s);
            _outputSlotsLayout.Controls.Remove(s);
            s.Dispose();
        }
        _pythonSlotRows.Clear();
        _inputSlotsLayout.SuspendLayout();
        _inputSlotsLayout.Controls.Clear();
        _inputSlotsLayout.RowStyles.Clear();
        _inputSlotsLayout.RowCount = Math.Max(1, inputs.Length);
        _outputSlotsLayout.SuspendLayout();
        _outputSlotsLayout.Controls.Clear();
        _outputSlotsLayout.RowStyles.Clear();
        _outputSlotsLayout.RowCount = Math.Max(1, outputs.Length);

        for (var i = 0; i < inputs.Length; i++)
        {
            _inputSlotsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var btn = MakePortSlot(output: false);
            btn.ViewModel = inputs[i].Slot;
            _inputSlotsLayout.Controls.Add(btn, 0, i);
            var lbl = MakePortName(inputs[i].Name, Views.CardTheme.AccentAgent, ContentAlignment.MiddleLeft);
            _inputSlotsLayout.Controls.Add(lbl, 1, i);
            _pythonSlotRows.Add(btn);
        }
        for (var i = 0; i < outputs.Length; i++)
        {
            _outputSlotsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var lbl = MakePortName(outputs[i].Name, Views.CardTheme.AccentAgent, ContentAlignment.MiddleRight);
            _outputSlotsLayout.Controls.Add(lbl, 0, i);
            var btn = MakePortSlot(output: true);
            btn.ViewModel = outputs[i].Slot;
            _outputSlotsLayout.Controls.Add(btn, 1, i);
            _pythonSlotRows.Add(btn);
        }

        _inputSlotsLayout.ResumeLayout();
        _outputSlotsLayout.ResumeLayout();
    }

    private void RebuildEnumSlots(EnumSelectorNodeViewModel e)
    {
        if (_outputSlotsLayout is null) return;

        var items = e.OutputSlots?.Items;
        if (items is null) { RebuildDynamicSlots([], Views.CardTheme.AccentEnum); return; }

        var entries = items
            .Select((item, i) => (
                Name: string.IsNullOrWhiteSpace(item.Name) ? $"Output {i + 1}" : item.Name,
                Slot: (IWorkflowSlotViewModel?)item.Slot))
            .ToArray();

        RebuildDynamicSlots(entries!, Views.CardTheme.AccentEnum);
    }

    private void RebuildDynamicSlots(IReadOnlyList<(string Name, IWorkflowSlotViewModel? Slot)> entries, Color labelColor)
    {
        if (_outputSlotsLayout is null) return;

        // Try to reuse: only reuse when the row count matches
        if (_dynamicSlotRows.Count == entries.Count)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                _dynamicSlotRows[i].label.Text = entries[i].Name;
                _dynamicSlotRows[i].label.ForeColor = labelColor;
                _dynamicSlotRows[i].slot.ViewModel = entries[i].Slot;
            }

            return;
        }

        // Rebuild
        foreach (var (_, slot) in _dynamicSlotRows)
        {
            _outputSlotsLayout.Controls.Remove(slot);
            slot.Dispose();
        }

        _dynamicSlotRows.Clear();
        _outputSlotsLayout.SuspendLayout();
        _outputSlotsLayout.Controls.Clear();
        _outputSlotsLayout.RowStyles.Clear();
        _outputSlotsLayout.RowCount = entries.Count;

        for (var i = 0; i < entries.Count; i++)
        {
            _outputSlotsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var lbl = MakePortName(entries[i].Name, labelColor, ContentAlignment.MiddleRight);
            lbl.Padding = new Padding(0, S(4), S(12), S(4));
            _outputSlotsLayout.Controls.Add(lbl, 0, i);

            var btn = MakePortSlot(output: true);
            btn.ViewModel = entries[i].Slot;
            _outputSlotsLayout.Controls.Add(btn, 1, i);
            _dynamicSlotRows.Add((lbl, btn));
        }

        _outputSlotsLayout.ResumeLayout();
    }

    private void UpdateEnumCombo(EnumSelectorNodeViewModel e)
    {
        if (_enumCombo is null) return;
        var values = e.EnumValues;
        if (_enumCombo.Items.Count != values.Length ||
            !values.Cast<object>().SequenceEqual(_enumCombo.Items.Cast<object>()))
        {
            _enumCombo.Items.Clear();
            foreach (var v in values)
            {
                if (v is not null)
                    _enumCombo.Items.Add(v);
            }
        }

        if (!Equals(_enumCombo.SelectedItem, e.SelectedValue))
            _enumCombo.SelectedItem = e.SelectedValue;

        PopulateRouterModeCombo(e.CompileMode);
    }

    // ── User input events ──────────────────────────────────────────────────────────
    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(new PropertyChangedEventHandler(OnNodePropertyChanged), sender, e); return; }
        if (_node is not null) Refresh(_node);
    }

    private void OnSeedTextChanged(object? sender, EventArgs e)
    {
        if (_updatingFromVm || _node is not ControllerViewModel c || _seedBox is null) return;
        if (!string.Equals(c.SeedPayload, _seedBox.Text, StringComparison.Ordinal))
            c.SeedPayload = _seedBox.Text;
    }

    private void OnScriptTextChanged(object? sender, EventArgs e)
    {
        if (_updatingFromVm || _node is not PythonScriptNodeViewModel p || _scriptBox is null) return;
        if (!string.Equals(p.Script, _scriptBox.Text, StringComparison.Ordinal))
            p.Script = _scriptBox.Text;
    }

    private void OnIntervalTextChanged(object? sender, EventArgs e)
    {
        if (_updatingFromVm || _node is not TimerNodeViewModel t || _intervalBox is null) return;
        if (int.TryParse(_intervalBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && t.IntervalMilliseconds != v)
            t.IntervalMilliseconds = v;
    }

    private void OnEnumValueChanged(object? sender, EventArgs e)
    {
        if (_updatingFromVm || _node is not EnumSelectorNodeViewModel node || _enumCombo is null) return;
        if (!Equals(node.SelectedValue, _enumCombo.SelectedItem))
            node.SelectedValue = _enumCombo.SelectedItem;
    }

    private void OnRouterModeChanged(object? sender, EventArgs e)
    {
        if (_updatingFromVm || _routerModeCombo is null) return;
        if (_routerModeCombo.SelectedItem is not RouterCompileMode mode) return;
        switch (_node)
        {
            case EnumSelectorNodeViewModel en when en.CompileMode != mode: en.CompileMode = mode; break;
        }
    }

    private async void OnCommandButtonClick(object? sender, EventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string cmdProp || _node is null) return;
        try
        {
            await ExecuteCommandAsync(_node, cmdProp).ConfigureAwait(true);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            MessageBox.Show(FindForm(), ex.InnerException.Message, cmdProp, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), ex.Message, cmdProp, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static async Task ExecuteCommandAsync(object source, string propertyName)
    {
        var prop = source.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (prop?.GetValue(source) is not { } cmd)
            throw new InvalidOperationException($"Unable to resolve command '{propertyName}'.");

        if (cmd.GetType().GetMethod("ExecuteAsync", [typeof(object)])?.Invoke(cmd, [null]) is Task t)
        { await t.ConfigureAwait(true); return; }

        cmd.GetType().GetMethod("Execute", [typeof(object)])?.Invoke(cmd, [null]);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────
    private void UnsubscribeVm()
    {
        if (_nodeNotifier is not null)
        {
            _nodeNotifier.PropertyChanged -= OnNodePropertyChanged;
            _nodeNotifier = null;
        }
    }

    private void ResetRefs()
    {
        _accentBar = null;
        _titleLabel = _orderBadge = _capsuleLabel = null;
        _capsule = null;
        _seedBox = null;
        _runButton = null;
        _enumCombo = null;
        _routerModeCombo = null;
        _outputSlotsLayout = null;
        _inputSlotsLayout = null;
        _scriptBox = null;
        _descriptionLabel = null;
        _intervalBox = null;
        _tickLabel = null;
        InputSlotButton = OutputSlotButton = null;
        _pythonSlotRows.Clear();
    }

    private static void SetText(Control? ctrl, string? text)
    {
        if (ctrl is not null && ctrl.Text != text) ctrl.Text = text ?? string.Empty;
    }

    private static void SetVisible(Control? ctrl, bool visible)
    {
        if (ctrl is not null && ctrl.Visible != visible) ctrl.Visible = visible;
    }

    /// <summary>
    /// Puts the status capsule's own size right: it is a hand-drawn pill, so unlike a label it does not grow to
    /// its text on its own, and it has to be re-measured after every zoom step as well as on every text change.
    /// </summary>
    private void SetCapsuleText(string? text, bool visible)
    {
        SetText(_capsuleLabel, text);
        SetVisible(_capsule, visible);
        ResizeCapsule();
    }

    private void ResizeCapsule()
    {
        if (_capsule is null || _capsuleLabel is null) return;

        var text = string.IsNullOrEmpty(_capsuleLabel.Text) ? " " : _capsuleLabel.Text;
        var measured = TextRenderer.MeasureText(text, _capsuleLabel.Font);
        var padX = Math.Max(1, S(Views.CardTheme.CapsulePaddingX));
        var padY = Math.Max(1, S(Views.CardTheme.CapsulePaddingY));
        _capsule.Size = new Size(measured.Width + (padX * 2) + 2, measured.Height + (padY * 2) + 2);
        _capsuleLabel.Location = new Point(padX + 1, padY + 1);
        _capsuleLabel.BackColor = Views.CardTheme.Hover;
    }

    // ── Control factory ──────────────────────────────────────────────────────────────
    private static Panel MakeSection()
        => new()
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Views.CardTheme.Surface,
        };

    private static Panel MakeDivider()
        => new()
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = Views.CardTheme.Divider,
        };

    /// <summary>Wraps a port list in a scrolling viewport, so extra dynamic ports stay reachable.</summary>
    private static Panel MakePortStrip(Control inner)
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Views.CardTheme.Surface,
        };
        host.Controls.Add(inner);
        return host;
    }

    private TableLayoutPanel MakePortStripLayout(bool inputPortsLeft)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Views.CardTheme.Surface,
        };

        // The port's column is one glyph wide and the port carries a spacer margin as wide as itself on the
        // outer side (see MakePortSlot), which squeezes the layout's display rectangle for that cell to zero:
        // the port is then centred on the strip's outer edge — the card's edge — and half of it rides outside,
        // the way the design asks. The half outside is drawn by the canvas (see Ports()).
        var portColumn = new ColumnStyle(SizeType.Absolute, Math.Max(1, S(24)));
        var names = new ColumnStyle(SizeType.Percent, 100F);

        if (inputPortsLeft)
        {
            layout.ColumnStyles.Add(portColumn);
            layout.ColumnStyles.Add(names);
        }
        else
        {
            layout.ColumnStyles.Add(names);
            layout.ColumnStyles.Add(portColumn);
        }

        return layout;
    }

    /// <summary>The current uniform scale factor (design × k = rendered metric).</summary>
    private float K => (float)Math.Max(0.05, _k);

    /// <summary>A design-pixel metric at the current scale, as a float (paddings, radii, row heights).</summary>
    private float F(float design) => design * K;

    /// <summary>A design-pixel metric at the current scale, rounded (glyph and hairline sizes).</summary>
    private int S(float design) => (int)Math.Round(design * K);

    private Label MakeLabel(string text, float size, FontStyle style, Color fore, Padding margin = default)
        => new()
        {
            AutoSize = true,
            BackColor = Color.Transparent,
            ForeColor = fore,
            Font = Views.CardTheme.Font(size, style),
            Margin = margin,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
        };

    /// <summary>A port's name in a strip — the same blue/semibold pair the reference gives those rows.</summary>
    private Label MakePortName(string text, Color fore, ContentAlignment align)
        => new()
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ForeColor = fore,
            Font = Views.CardTheme.Font(Views.CardTheme.PortNameSize, FontStyle.Bold),
            Margin = new Padding(S(4), S(4), S(4), S(4)),
            Text = text,
            TextAlign = align,
            AutoEllipsis = true,
        };

    /// <summary>
    /// A borderless text box on the field's own near-black ground. It is multi-line only so that its height is
    /// controlled by the row it sits in — a single-line WinForms text box ignores its height and would float at
    /// the top of the field.
    /// </summary>
    private TextBox MakeField()
        => new()
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = false,
            WordWrap = false,
            ScrollBars = ScrollBars.None,
            BorderStyle = BorderStyle.None,
            BackColor = Views.CardTheme.Field,
            ForeColor = Views.CardTheme.Value,
            Font = Views.CardTheme.Font(Views.CardTheme.ValueSize),
            Margin = Padding.Empty,
        };

    /// <summary>The rounded, hairline-framed ground an input field sits in.</summary>
    private Views.FramePanel MakeFieldHost(Control inner)
    {
        var frame = new Views.FramePanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, S(5), 0, 0),
            Padding = new Padding(S(7), S(5), S(7), S(3)),
            Fill = Views.CardTheme.Field,
            Frame = Views.CardTheme.Border,
            Radius = F(Views.CardTheme.FieldRadius),
        };
        frame.Controls.Add(inner);
        return frame;
    }

    private ComboBox MakeComboBox()
    {
        var combo = new ComboBox
        {
            BackColor = Views.CardTheme.Field,
            ForeColor = Views.CardTheme.Value,
            FlatStyle = FlatStyle.Flat,
            DropDownStyle = ComboBoxStyle.DropDownList,
            // Owner-drawn for two reasons: the list drops down in the card's own dark palette instead of the
            // system's light one, and only an owner-drawn combo lets ItemHeight set its height, which the layout
            // below needs (a combo's height is otherwise fixed by its font).
            DrawMode = DrawMode.OwnerDrawFixed,
            Font = Views.CardTheme.Font(Views.CardTheme.ValueSize),
            Margin = Padding.Empty,
        };
        combo.DrawItem += OnComboDrawItem;
        return combo;
    }

    private static void OnComboDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox combo) return;

        var edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
        var selected = e.Index == combo.SelectedIndex;
        var back = edit || !selected ? Views.CardTheme.Field : Views.CardTheme.Hover;

        using (var fill = new SolidBrush(back))
        {
            e.Graphics.FillRectangle(fill, e.Bounds);
        }

        if (e.Index < 0 || e.Index >= combo.Items.Count) return;

        TextRenderer.DrawText(
            e.Graphics,
            combo.Items[e.Index]?.ToString() ?? string.Empty,
            combo.Font,
            e.Bounds,
            Views.CardTheme.Value,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }

    /// <summary>
    /// The rounded, hairline-framed ground a drop-down sits in, with a chevron beside it standing in for the
    /// system's arrow.
    /// <para>
    /// A WinForms combo draws its own 1px frame and its arrow as a light system button, and neither colour has a
    /// property (a combo has <c>FlatStyle</c> but no <c>FlatAppearance</c>). So the combo is laid out one pixel
    /// past its host on every side and well past on the right, which puts its frame and its arrow outside the
    /// host's clip — the host is the thing that clips it — while leaving the field's fill, text and chevron
    /// entirely ours. Clicking the field opens the list itself, so hiding the arrow costs nothing.
    /// </para>
    /// </summary>
    private Control MakeComboField(ComboBox inner)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, S(2), 0, S(2)),
            Padding = Padding.Empty,
            BackColor = Views.CardTheme.Surface,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Math.Max(1, S(14))));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var frame = new Views.FramePanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Fill = Views.CardTheme.Field,
            Frame = Views.CardTheme.Border,
            Radius = F(Views.CardTheme.FieldRadius),
        };

        void Fit()
        {
            inner.ItemHeight = Math.Max(8, frame.ClientSize.Height - 4);
            inner.Bounds = new Rectangle(
                -1,
                -1,
                frame.ClientSize.Width + SystemInformation.VerticalScrollBarWidth + 6,
                frame.ClientSize.Height + 2);
        }

        frame.Layout += (_, _) => Fit();
        inner.MouseDown += (_, _) => inner.DroppedDown = true;
        frame.Controls.Add(inner);

        var chevron = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            BackColor = Color.Transparent,
            ForeColor = Views.CardTheme.Label,
            Font = Views.CardTheme.Font(Views.CardTheme.LabelSize),
            Text = "▾",
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = Padding.Empty,
        };

        row.Controls.Add(frame, 0, 0);
        row.Controls.Add(chevron, 1, 0);
        return row;
    }

    /// <summary>
    /// A port inside a port strip. Its size follows the uniform scale (24 at scale 1), and it sits flush against
    /// the strip's outer edge — which is the card's edge.
    /// <para>
    /// The reference lets these ports ride half a glyph past the card's edge, on a negative margin. WinForms
    /// cannot: a child window is clipped to its parent, so an overhanging port loses the half the layout put
    /// outside, and squeezing the layout's display rectangle to push the port out — the only other lever a table
    /// layout gives — shrinks the port to nothing instead. Flush is the closest placement that stays whole, and
    /// the ports that do ride the card's own edge (the ones the card positions itself) are completed by the
    /// canvas, as <see cref="Ports"/> describes.
    /// </para>
    /// </summary>
    private Views.SlotView MakePortSlot(bool output)
        => new()
        {
            DesignSize = 24,
            Size = new Size(Math.Max(9, S(24)), Math.Max(9, S(24))),
            Margin = new Padding(0, S(4), 0, S(4)),
        };

    private Views.GhostButton MakeGhostButton(string text, string cmdProp, Color textColor)
    {
        var btn = new Views.GhostButton
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(S(3)),
            Radius = F(Views.CardTheme.FieldRadius),
            TextColor = textColor,
            Text = text,
            Tag = cmdProp,
            AccessibleName = text,
        };
        btn.Click += OnCommandButtonClick;
        return btn;
    }
}
