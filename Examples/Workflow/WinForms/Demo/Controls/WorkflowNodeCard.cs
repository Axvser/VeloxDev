using Demo.ViewModels;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Reflection;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Controls;

/// <summary>
/// Card control for a single workflow node.
/// </summary>
internal sealed class WorkflowNodeCard : UserControl
{
    // ── Appearance constants ──────────────────────────────────────────────────────────────
    private static readonly Color DarkBody = Color.FromArgb(37, 37, 37);
    private static readonly Color DarkHeader = Color.FromArgb(45, 45, 45);
    private static readonly Color DarkExec = Color.FromArgb(31, 31, 31);

    // ── Layout ──────────────────────────────────────────────────────────────────
    private readonly TableLayoutPanel _rootLayout;
    private readonly Panel _headerPanel;
    private readonly Panel _bodyPanel;
    private readonly Panel _footerPanel;

    // ── Uniform design-coordinate scaling ─────────────────────────────────────────
    // The card host is sized by the canvas to the node's collapsed box (node.Size =
    // [DefaultSize] × collapse). The interior is authored once at the type's DESIGN size
    // and re-scaled uniformly on every layout by k = current / design (fonts, row/column
    // styles, paddings, glyphs), mirroring the Trimmed WinForms ApplyScale so content can
    // never overflow the collapsed card.
    private float _designW;
    private float _designH;
    private double _k = 1d;
    private bool _layoutReady;
    private bool _applyingScale;

    // ── ViewModel subscription ────────────────────────────────────────────────────────
    private IWorkflowNodeViewModel? _node;
    private INotifyPropertyChanged? _nodeNotifier;
    private bool _updatingFromVm;

    // ── Dynamic control references ──────────────────────────────────────────────────────────
    private Label? _titleLabel;
    private Label? _orderBadge;
    private Label? _routedBadge;
    private TextBox? _seedBox;
    private Label? _controllerDesc;
    private ComboBox? _enumCombo;
    private ComboBox? _routerModeCombo;
    private TableLayoutPanel? _outputSlotsLayout;
    private readonly List<(Label label, Views.SlotView slot)> _dynamicSlotRows = [];
    private TableLayoutPanel? _inputSlotsLayout;
    private TextBox? _scriptBox;
    private Label? _descriptionLabel;
    private Label? _pythonStatusLabel;
    private readonly List<Views.SlotView> _pythonSlotRows = [];
    private TextBox? _intervalBox;
    private Label? _tickLabel;

    // ── Main slot buttons (edge-anchored slot views; screen position computed by WorkflowCanvas) ────
    internal Views.SlotView? InputSlotButton { get; private set; }
    internal Views.SlotView? OutputSlotButton { get; private set; }

    // ── Events ──────────────────────────────────────────────────────────────────
    /// <summary>
    /// Gets the bound node view model.
    /// </summary>
    internal IWorkflowNodeViewModel? ViewModel => _node;

    // ── Constructor ──────────────────────────────────────────────────────────────────
    internal WorkflowNodeCard()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(11, 17, 32);
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
            Padding = Padding.Empty,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = DarkBody,
        };
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        _headerPanel = MakeSection();
        _bodyPanel = MakeSection();
        _footerPanel = MakeSection();

        Controls.Add(_rootLayout);
        _rootLayout.Controls.Add(_headerPanel, 0, 0);
        _rootLayout.Controls.Add(_bodyPanel, 0, 1);
        _rootLayout.Controls.Add(_footerPanel, 0, 2);
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
            }
        }
        finally
        {
            _updatingFromVm = false;
        }

        RefreshVisual();
    }

    /// <summary>Refreshes only visual state such as border color and section backgrounds.</summary>
    internal void RefreshVisual()
    {
        if (_node is null) return;

        Color border, header, body, footer;
        switch (_node)
        {
            case ControllerViewModel c:
                border = c.IsActive ? Color.FromArgb(103, 232, 249) : Color.White;
                header = c.IsActive ? Color.FromArgb(21, 94, 117) : DarkHeader;
                body = DarkBody;
                footer = DarkHeader;
                break;
            case EnumSelectorNodeViewModel:
                border = Color.FromArgb(214, 160, 255);
                header = Color.FromArgb(58, 37, 80);
                body = Color.FromArgb(42, 30, 53);
                footer = body;
                break;
            case PythonScriptNodeViewModel:
            case TimerNodeViewModel:
                border = Color.FromArgb(110, 198, 255);
                header = Color.FromArgb(37, 53, 69);
                body = Color.FromArgb(30, 42, 53);
                footer = body;
                break;
            default:
                border = Color.FromArgb(75, 85, 99);
                header = body = footer = DarkBody;
                break;
        }

        _borderColor = border;
        _headerPanel.BackColor = header;
        _bodyPanel.BackColor = body;
        _footerPanel.BackColor = footer;
        PropagateBackColor(_headerPanel);
        PropagateBackColor(_bodyPanel);
        PropagateBackColor(_footerPanel);
        Invalidate();
    }

    // ── Drawing (rounded border) ─────────────────────────────────────────────────────────
    private Color _borderColor = Color.FromArgb(75, 85, 99);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundRect(rect, 18);
        using var pen = new Pen(_borderColor, 1.5F);
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_layoutReady && !_applyingScale)
        {
            ApplyScaleToCurrent();
        }

        PositionOverlaySlotButtons();
    }

    /// <summary>Positions the floating slot buttons at the card's left-center / right-center edges.</summary>
    private void PositionOverlaySlotButtons()
    {
        if (InputSlotButton is not null)
            InputSlotButton.Location = new Point(
                -(InputSlotButton.Width / 2),
                (Height - InputSlotButton.Height) / 2);

        if (OutputSlotButton is not null)
            OutputSlotButton.Location = new Point(
                Width - OutputSlotButton.Width / 2,
                (Height - OutputSlotButton.Height) / 2);
    }

    // ── Uniform scale application ─────────────────────────────────────────────────
    /// <summary>Records the card's design (scale-1) dimensions for the bound node type — matching
    /// each node view-model's [DefaultSize] (Controller 220×340, Timer 200×140, Python 280×260,
    /// Enum 280×380).</summary>
    private void SetDesignSize(IWorkflowNodeViewModel node)
    {
        switch (node)
        {
            case ControllerViewModel:
                _designW = 220; _designH = 340; break;
            case TimerNodeViewModel:
                _designW = 200; _designH = 140; break;
            case PythonScriptNodeViewModel:
                _designW = 280; _designH = 260; break;
            case EnumSelectorNodeViewModel:
                _designW = 280; _designH = 380; break;
            default:
                _designW = 220; _designH = 340; break;
        }
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
                _rootLayout.ResumeLayout(true);
            }
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
                c.Font = new Font(f.FontFamily, Math.Max(0.5f, f.SizeInPoints * (float)r), f.Style);
            }
        }

        // In-card dynamic port glyphs scale with the card from their DESIGN size (20 × k), so a
        // round-trip to a tiny zoom cannot drift via the minimum clamp. Edge-anchored overlay slot
        // buttons live OUTSIDE _rootLayout and are intentionally not scaled (canvas positions them).
        if (c is Views.SlotView sv)
        {
            var s = Math.Max(9, (int)Math.Round(20 * k));
            sv.Width = s;
            sv.Height = s;
        }

        if (c.Padding != Padding.Empty)
            c.Padding = ScalePadding(c.Padding, r);
        if (c.Margin != Padding.Empty)
            c.Margin = ScalePadding(c.Margin, r);
        if (c.MinimumSize != System.Drawing.Size.Empty)
            c.MinimumSize = ScaleSize(c.MinimumSize, r);
        if (c.MaximumSize != System.Drawing.Size.Empty)
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

    private static System.Drawing.Size ScaleSize(System.Drawing.Size s, double r)
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

    private Views.SlotView AddSlotButton(IWorkflowSlotViewModel? slot)
    {
        var btn = new Views.SlotView();
        btn.ViewModel = slot;
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

        switch (node)
        {
            case ControllerViewModel: BuildController(); break;
            case EnumSelectorNodeViewModel: BuildEnumSelector(); break;
            case PythonScriptNodeViewModel: BuildPython(); break;
            case TimerNodeViewModel: BuildTimer(); break;
        }
    }

    private void SetRows(float headerH, float footerH, bool showFooter)
    {
        _rootLayout.SuspendLayout();
        _rootLayout.RowStyles.Clear();
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, headerH));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, footerH));
        _footerPanel.Visible = showFooter;
        _rootLayout.ResumeLayout();
    }

    // ── Layout: Controller ──────────────────────────────────────────────────────
    private void BuildController()
    {
        SetRows(52F, 88F, true);

        _titleLabel = MakeLabel(Color.White, 10.5F, FontStyle.Bold, autoSize: false, ContentAlignment.MiddleCenter, "Network Flow Controller");
        _titleLabel.Dock = DockStyle.Fill;
        _headerPanel.Controls.Add(_titleLabel);

        var bodyHost = new Panel
        {
            Dock = DockStyle.Fill, AutoScroll = true,
            MinimumSize = new System.Drawing.Size(0, 40),
            BackColor = DarkBody, Padding = new Padding(12),
        };
        var bodyTlp = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, RowCount = 3,
            Margin = Padding.Empty, Padding = Padding.Empty, BackColor = DarkBody,
        };
        bodyTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        // Row 0-1: Seed Payload
        bodyTlp.Controls.Add(MakeLabel(Color.FromArgb(220, 220, 220), 9F, FontStyle.Regular, autoSize: false, ContentAlignment.MiddleLeft, "Seed Payload"), 0, 0);
        _seedBox = MakeTextBox();
        _seedBox.TextChanged += OnSeedTextChanged;
        bodyTlp.Controls.Add(_seedBox, 0, 1);
        // Row 2: Description
        _controllerDesc = MakeLabel(Color.FromArgb(189, 189, 189), 8.5F, FontStyle.Regular, autoSize: false, ContentAlignment.TopLeft);
        _controllerDesc.Dock = DockStyle.Fill;
        bodyTlp.Controls.Add(_controllerDesc, 0, 2);
        bodyHost.Controls.Add(bodyTlp);
        _bodyPanel.Controls.Add(bodyHost);

        var ctrlFooterTlp = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
            Margin = Padding.Empty, Padding = new Padding(8), BackColor = DarkBody,
        };
        ctrlFooterTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        ctrlFooterTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        ctrlFooterTlp.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        ctrlFooterTlp.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        ctrlFooterTlp.Controls.Add(MakeCmdButton("Compile", nameof(ControllerViewModel.CompileCommand)), 0, 0);
        ctrlFooterTlp.Controls.Add(MakeCmdButton("Run", nameof(ControllerViewModel.RunCommand)), 1, 0);
        ctrlFooterTlp.Controls.Add(MakeCmdButton("Stop", nameof(ControllerViewModel.StopCommand)), 0, 1);
        ctrlFooterTlp.Controls.Add(MakeCmdButton("Close", nameof(ControllerViewModel.CloseWorkflowCommand)), 1, 1);
        _footerPanel.Controls.Add(ctrlFooterTlp);

        OutputSlotButton = AddSlotButton(null);
    }

    // ── Layout: EnumSelector ────────────────────────────────────────────────────
    private void BuildEnumSelector()
    {
        SetRows(48F, 0F, false);

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Margin = Padding.Empty,
            Padding = new Padding(12, 14, 12, 10), BackColor = Color.FromArgb(58, 37, 80),
        };
        _titleLabel = MakeLabel(Color.White, 10.5F, FontStyle.Bold, autoSize: true);
        _routedBadge = MakeBadge(Color.FromArgb(228, 216, 255), Color.FromArgb(43, 21, 64));
        flow.Controls.Add(_titleLabel);
        flow.Controls.Add(_routedBadge);
        _headerPanel.Controls.Add(flow);

        // Whole body scrolls (Auto row heights, content-sized) so an arbitrary number of dynamic
        // output rows stays reachable and is never hard-clipped when the node is small.
        var bodyHost = new Panel
        {
            Dock = DockStyle.Fill, AutoScroll = true,
            MinimumSize = new System.Drawing.Size(0, 40),
            Margin = Padding.Empty, Padding = Padding.Empty,
            BackColor = Color.FromArgb(42, 30, 53),
        };
        var bodyTlp = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, RowCount = 6,
            Margin = Padding.Empty, Padding = new Padding(14), BackColor = Color.FromArgb(42, 30, 53),
        };
        for (var i = 0; i < 6; i++) bodyTlp.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bodyTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bodyTlp.Controls.Add(MakeLabel(Color.FromArgb(191, 191, 191), 8.5F, FontStyle.Regular, autoSize: false, ContentAlignment.MiddleLeft, "Selected Method"), 0, 0);
        _enumCombo = MakeComboBox();
        _enumCombo.SelectedIndexChanged += OnEnumValueChanged;
        bodyTlp.Controls.Add(_enumCombo, 0, 1);
        bodyTlp.Controls.Add(MakeLabel(Color.FromArgb(191, 191, 191), 8.5F, FontStyle.Regular, autoSize: false, ContentAlignment.MiddleLeft, "Compile Mode"), 0, 2);
        _routerModeCombo = MakeComboBox();
        _routerModeCombo.SelectedIndexChanged += OnRouterModeChanged;
        bodyTlp.Controls.Add(_routerModeCombo, 0, 3);
        bodyTlp.Controls.Add(MakeLabel(Color.FromArgb(191, 191, 191), 8.5F, FontStyle.Regular, autoSize: false, ContentAlignment.MiddleLeft, "Output Slots"), 0, 4);

        _outputSlotsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, Margin = Padding.Empty,
            Padding = Padding.Empty, BackColor = Color.FromArgb(42, 30, 53),
        };
        _outputSlotsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _outputSlotsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28F));
        bodyTlp.Controls.Add(_outputSlotsLayout, 0, 5);
        bodyHost.Controls.Add(bodyTlp);
        _bodyPanel.Controls.Add(bodyHost);

        InputSlotButton = AddSlotButton(null);
    }

    // ── Layout: Python node ───────────────────────────────────────────────────────
    private void BuildPython()
    {
        SetRows(48F, 0F, false);

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Margin = Padding.Empty,
            Padding = new Padding(12, 14, 12, 10), BackColor = Color.FromArgb(37, 53, 69),
        };
        _titleLabel = MakeLabel(Color.White, 10.5F, FontStyle.Bold, autoSize: true);
        _pythonStatusLabel = MakeBadge(Color.FromArgb(228, 216, 255), Color.FromArgb(43, 36, 64));
        flow.Controls.Add(_titleLabel);
        flow.Controls.Add(_pythonStatusLabel);
        _headerPanel.Controls.Add(flow);

        // Purpose description spans the full body width above the editor row (Auto row, wraps; no cap),
        // so wrapped text is never hard-clipped; the editor row below fills the remaining space. The
        // port strips are isolated in fixed left/right columns and scroll so extra dynamic ports stay
        // reachable no matter how many the selector produces.
        var bodyHost = new Panel
        {
            Dock = DockStyle.Fill, AutoScroll = true,
            MinimumSize = new System.Drawing.Size(0, 40),
            BackColor = Color.FromArgb(30, 42, 53), Padding = Padding.Empty,
        };
        var pythonBody = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.FromArgb(30, 42, 53),
        };
        pythonBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // full-width description
        pythonBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // editor row

        _descriptionLabel = MakeLabel(Color.FromArgb(139, 148, 158), 8.5F, FontStyle.Regular, autoSize: false, ContentAlignment.TopLeft);
        _descriptionLabel.Dock = DockStyle.Fill;
        _descriptionLabel.Margin = new Padding(10, 6, 10, 2);
        pythonBody.Controls.Add(_descriptionLabel, 0, 0);

        var bodyGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.FromArgb(30, 42, 53),
        };
        bodyGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        bodyGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64F));   // input ports (left)
        bodyGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));   // middle: script editor
        bodyGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64F));   // output ports (right)

        _inputSlotsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, Margin = Padding.Empty,
            Padding = Padding.Empty, BackColor = Color.FromArgb(30, 42, 53),
        };
        _inputSlotsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28F));
        _inputSlotsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bodyGrid.Controls.Add(MakePortStrip(_inputSlotsLayout, Color.FromArgb(30, 42, 53)), 0, 0);

        var middle = new Panel
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty,
            Padding = new Padding(4, 6, 4, 6), BackColor = Color.FromArgb(30, 42, 53),
        };

        _scriptBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            Font = ScaledFont("Consolas", 10F, FontStyle.Regular),
            BackColor = Color.FromArgb(13, 17, 23),
            ForeColor = Color.FromArgb(230, 237, 243),
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 2, 0, 0),
        };
        _scriptBox.TextChanged += OnScriptTextChanged;
        middle.Controls.Add(_scriptBox);
        bodyGrid.Controls.Add(middle, 1, 0);

        _outputSlotsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, Margin = Padding.Empty,
            Padding = Padding.Empty, BackColor = Color.FromArgb(30, 42, 53),
        };
        _outputSlotsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _outputSlotsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28F));
        bodyGrid.Controls.Add(MakePortStrip(_outputSlotsLayout, Color.FromArgb(30, 42, 53)), 2, 0);

        pythonBody.Controls.Add(bodyGrid, 0, 1);
        bodyHost.Controls.Add(pythonBody);
        _bodyPanel.Controls.Add(bodyHost);
    }

    // ── Layout: Timer node ─────────────────────────────────────────────────────────
    private void BuildTimer()
    {
        SetRows(48F, 0F, false);

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Margin = Padding.Empty,
            Padding = new Padding(12, 14, 12, 10), BackColor = Color.FromArgb(37, 53, 69),
        };
        _titleLabel = MakeLabel(Color.White, 10.5F, FontStyle.Bold, autoSize: true);
        _orderBadge = MakeBadge(Color.FromArgb(200, 255, 200), Color.FromArgb(31, 61, 31));
        flow.Controls.Add(_titleLabel);
        flow.Controls.Add(_orderBadge);
        _headerPanel.Controls.Add(flow);

        // Auto rows in a scrollable host: content takes its natural height and the body scrolls
        // instead of clipping when the node is small or the tick text wraps long.
        var bodyHost = new Panel
        {
            Dock = DockStyle.Fill, AutoScroll = true,
            MinimumSize = new System.Drawing.Size(0, 40),
            Margin = Padding.Empty, Padding = Padding.Empty,
            BackColor = Color.FromArgb(30, 42, 53),
        };
        var bodyTlp = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, RowCount = 4,
            Margin = Padding.Empty, Padding = new Padding(14, 10, 14, 10), BackColor = Color.FromArgb(30, 42, 53),
        };
        for (var i = 0; i < 4; i++) bodyTlp.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bodyTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        bodyTlp.Controls.Add(MakeLabel(Color.FromArgb(191, 191, 191), 8.5F, FontStyle.Regular, autoSize: false, ContentAlignment.MiddleLeft, "Interval (ms)"), 0, 0);
        _intervalBox = MakeTextBox();
        _intervalBox.TextChanged += OnIntervalTextChanged;
        bodyTlp.Controls.Add(_intervalBox, 0, 1);
        bodyTlp.Controls.Add(MakeLabel(Color.FromArgb(191, 191, 191), 8.5F, FontStyle.Regular, autoSize: false, ContentAlignment.MiddleLeft, "Last Tick"), 0, 2);
        _tickLabel = MakeLabel(Color.FromArgb(110, 198, 255), 8.8F, FontStyle.Bold, autoSize: false, ContentAlignment.TopLeft);
        bodyTlp.Controls.Add(_tickLabel, 0, 3);
        bodyHost.Controls.Add(bodyTlp);
        _bodyPanel.Controls.Add(bodyHost);

        InputSlotButton = AddSlotButton(null);
        OutputSlotButton = AddSlotButton(null);
    }

    // ── Data application ──────────────────────────────────────────────────────────────
    private void ApplyController(ControllerViewModel c)
    {
        SetText(_seedBox, c.SeedPayload);

        if (_controllerDesc is not null)
        {
            _controllerDesc.Text = c.IsActive
                ? "The controller is currently streaming the initial context into the workflow."
                : "The controller only pushes the initial context into the workflow.";
        }

        OutputSlotButton!.ViewModel = c.OutputSlot;
    }

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
        SetText(_routedBadge, e.LastRouted);
        SetVisible(_routedBadge, !string.IsNullOrEmpty(e.LastRouted) && e.LastRouted != "-");
        InputSlotButton!.ViewModel = e.InputSlot;
        UpdateEnumCombo(e);
        RebuildEnumSlots(e);
    }

    private void ApplyPython(PythonScriptNodeViewModel p)
    {
        SetText(_titleLabel, p.Title);
        SetText(_pythonStatusLabel, p.LastStatus);
        SetVisible(_pythonStatusLabel, !string.IsNullOrEmpty(p.LastStatus) && p.LastStatus != "Idle");
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
            var btn = MakeDynamicSlot();
            btn.ViewModel = inputs[i].Slot;
            _inputSlotsLayout.Controls.Add(btn, 0, i);
            var lbl = MakeLabel(Color.FromArgb(110, 198, 255), 8.8F, FontStyle.Bold, autoSize: false, ContentAlignment.MiddleLeft);
            lbl.Dock = DockStyle.Fill;
            lbl.Text = inputs[i].Name;
            _inputSlotsLayout.Controls.Add(lbl, 1, i);
            _pythonSlotRows.Add(btn);
        }
        for (var i = 0; i < outputs.Length; i++)
        {
            _outputSlotsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var lbl = MakeLabel(Color.FromArgb(110, 198, 255), 8.8F, FontStyle.Bold, autoSize: false, ContentAlignment.MiddleRight);
            lbl.Dock = DockStyle.Fill;
            lbl.Text = outputs[i].Name;
            _outputSlotsLayout.Controls.Add(lbl, 0, i);
            var btn = MakeDynamicSlot();
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
        if (items is null) { RebuildDynamicSlots([], Color.FromArgb(214, 160, 255)); return; }

        var entries = items
            .Select((item, i) => (
                Name: string.IsNullOrWhiteSpace(item.Name) ? $"Output {i + 1}" : item.Name,
                Slot: (IWorkflowSlotViewModel?)item.Slot))
            .ToArray();

        RebuildDynamicSlots(entries!, Color.FromArgb(214, 160, 255));
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
            var lbl = MakeLabel(labelColor, 8.8F, FontStyle.Bold, autoSize: false, ContentAlignment.MiddleRight);
            lbl.Dock = DockStyle.Fill;
            lbl.Text = entries[i].Name;
            _outputSlotsLayout.Controls.Add(lbl, 0, i);

            var btn = MakeDynamicSlot();
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
        _titleLabel = _orderBadge = _routedBadge = null;
        _seedBox = null;
        _enumCombo = null;
        _routerModeCombo = null;
        _controllerDesc = null;
        _outputSlotsLayout = null;
        _inputSlotsLayout = null;
        _scriptBox = null;
        _descriptionLabel = null;
        _pythonStatusLabel = null;
        _intervalBox = null;
        _tickLabel = null;
        InputSlotButton = OutputSlotButton = null;
    }

    private static void PropagateBackColor(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is Label || child is TableLayoutPanel || child is Panel || child is FlowLayoutPanel)
            {
                child.BackColor = parent.BackColor;
                PropagateBackColor(child);
            }
        }
    }

    private static void SetText(Control? ctrl, string? text)
    {
        if (ctrl is not null && ctrl.Text != text) ctrl.Text = text ?? string.Empty;
    }

    private static void SetVisible(Control? ctrl, bool visible)
    {
        if (ctrl is not null && ctrl.Visible != visible) ctrl.Visible = visible;
    }

    private static void SetChecked(CheckBox? check, bool value)
    {
        if (check is not null && check.Checked != value) check.Checked = value;
    }

    private static Color ParseColor(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try { return ColorTranslator.FromHtml(value); }
        catch (ArgumentException) { return fallback; }
    }

    private static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // ── Control factory ──────────────────────────────────────────────────────────────
    private static Panel MakeSection()
        => new() { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = DarkBody };

    /// <summary>Wraps a growing port list in a vertical AutoScroll viewport so extra dynamic port
    /// rows are reachable (never hard-clipped) and the strip keeps a small minimum height even when
    /// the node collapses.</summary>
    private static Panel MakePortStrip(Control inner, Color back)
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            MinimumSize = new System.Drawing.Size(0, 28),
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = back,
        };
        host.Controls.Add(inner);
        return host;
    }

    /// <summary>The current uniform scale factor (design × k = rendered metric).</summary>
    private float K => (float)Math.Max(0.05, _k);

    /// <summary>Creates the font for a control at the current uniform scale (design size × k).</summary>
    private Font ScaledFont(string family, float size, FontStyle style)
        => new(family, Math.Max(0.5f, size * K), style);

    /// <summary>Scales an in-card metric (font size, glyph, padding) by the current uniform factor k,
    /// so content authored at the design size stays proportional inside the collapsed card.</summary>
    private int S(float design) => (int)Math.Round(design * K);

    private Label MakeLabel(Color fore, float size, FontStyle style, bool autoSize,
        ContentAlignment align = ContentAlignment.MiddleLeft, string text = "")
        => new()
        {
            AutoSize = autoSize,
            ForeColor = fore,
            BackColor = Color.Transparent,
            Font = ScaledFont("Microsoft YaHei UI", size, style),
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            TextAlign = align,
            Text = text,
            Dock = autoSize ? DockStyle.None : DockStyle.Fill,
        };

    private Label MakeBadge(Color fore, Color back)
        => new()
        {
            AutoSize = true,
            ForeColor = fore,
            BackColor = back,
            BorderStyle = BorderStyle.FixedSingle,
            Font = ScaledFont("Microsoft YaHei UI", 8.2F, FontStyle.Bold),
            Margin = new Padding(S(8), 0, 0, 0),
            Padding = new Padding(S(6), Math.Max(1, S(2)), S(6), Math.Max(1, S(2))),
            Visible = false,
        };

    private TextBox MakeTextBox()
        => new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(55, 55, 55),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = ScaledFont("Microsoft YaHei UI", 9F, FontStyle.Regular),
            Margin = new Padding(0, Math.Max(1, S(2)), 0, Math.Max(1, S(2))),
        };

    private ComboBox MakeComboBox()
        => new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(55, 55, 55),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = ScaledFont("Microsoft YaHei UI", 9F, FontStyle.Regular),
            Margin = new Padding(0, Math.Max(1, S(2)), 0, Math.Max(1, S(2))),
        };

    /// <summary>Creates an in-card dynamic slot glyph at the current uniform scale (the row's AutoSize
    /// height follows). Edge-anchored overlay slot buttons are created elsewhere and stay unscaled.</summary>
    private Views.SlotView MakeDynamicSlot()
    {
        var s = Math.Max(9, S(20));
        return new Views.SlotView
        {
            Size = new System.Drawing.Size(s, s),
            Margin = new Padding(Math.Max(0, S(2)), Math.Max(1, S(4)), Math.Max(0, S(2)), Math.Max(1, S(4))),
        };
    }

    private Button MakeCmdButton(string text, string cmdProp)
    {
        var btn = new Button
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.White,
            Font = ScaledFont("Microsoft YaHei UI", 9F, FontStyle.Regular),
            Text = text,
            Tag = cmdProp,
            AccessibleName = text,
        };
        btn.FlatAppearance.BorderColor = Color.FromArgb(71, 85, 105);
        btn.Click += OnCommandButtonClick;
        return btn;
    }
}
