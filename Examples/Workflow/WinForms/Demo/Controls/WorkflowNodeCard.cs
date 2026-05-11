using Demo.ViewModels;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Reflection;
using VeloxDev.WorkflowSystem;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Controls;

/// <summary>
/// 单个工作流节点的卡片控件。
/// </summary>
internal sealed class WorkflowNodeCard : UserControl
{
    // ── 外观常量 ──────────────────────────────────────────────────────────────
    private static readonly Color DarkBody = Color.FromArgb(37, 37, 37);
    private static readonly Color DarkHeader = Color.FromArgb(45, 45, 45);
    private static readonly Color DarkExec = Color.FromArgb(31, 31, 31);

    // ── 布局 ──────────────────────────────────────────────────────────────────
    private readonly TableLayoutPanel _rootLayout;
    private readonly Panel _headerPanel;
    private readonly Panel _bodyPanel;
    private readonly Panel _footerPanel;

    // ── ViewModel 订阅 ────────────────────────────────────────────────────────
    private IWorkflowNodeViewModel? _node;
    private INotifyPropertyChanged? _nodeNotifier;
    private bool _updatingFromVm;

    // ── 动态控件引用 ──────────────────────────────────────────────────────────
    private Label? _titleLabel;
    private Label? _orderBadge;
    private Label? _loadBadge;
    private Label? _durationValue;
    private Label? _routedBadge;
    private TextBox? _delayBox;
    private TextBox? _titleBox;
    private CheckBox? _autoBroadcastCheck;
    private Label? _runCountLabel;
    private Label? _waitCountLabel;
    private Label? _traceLabel;
    private Label? _statusLabel;
    private Label? _bodyDuration;
    private Label? _errorLabel;
    private Label? _responseLabel;
    private TextBox? _seedBox;
    private Label? _controllerDesc;
    private CheckBox? _conditionCheck;
    private ComboBox? _enumCombo;
    private TableLayoutPanel? _outputSlotsLayout;
    private readonly List<(Label label, SlotButton slot)> _dynamicSlotRows = [];

    // ── 主槽位按钮（边缘固定的圆形槽位，由 WorkflowCanvas 计算屏幕位置）────
    internal SlotButton? InputSlotButton { get; private set; }
    internal SlotButton? OutputSlotButton { get; private set; }

    // ── 事件 ──────────────────────────────────────────────────────────────────
    /// <summary>
    /// Gets the bound node view model.
    /// </summary>
    internal IWorkflowNodeViewModel? ViewModel => _node;

    // ── 构造 ──────────────────────────────────────────────────────────────────
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

    // ── 公开绑定 API ──────────────────────────────────────────────────────────

    /// <summary>将卡片绑定到新的节点 ViewModel。</summary>
    internal void Bind(IWorkflowNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (ReferenceEquals(_node, node)) { Refresh(node); return; }

        UnsubscribeVm();
        _node = node;
        Tag = node;

        if (node is INotifyPropertyChanged n)
        {
            _nodeNotifier = n;
            n.PropertyChanged += OnNodePropertyChanged;
        }

        BuildLayout(node);
        Refresh(node);
    }

    /// <summary>解除绑定，重置卡片到空状态。</summary>
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

    /// <summary>从 ViewModel 刷新所有显示值（不重建布局）。</summary>
    internal void Refresh(IWorkflowNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _updatingFromVm = true;
        try
        {
            switch (node)
            {
                case NodeViewModel w: ApplyWorker(w); break;
                case ControllerViewModel c: ApplyController(c); break;
                case BoolSelectorNodeViewModel b: ApplyBoolSelector(b); break;
                case EnumSelectorNodeViewModel e: ApplyEnumSelector(e); break;
            }
        }
        finally
        {
            _updatingFromVm = false;
        }

        RefreshVisual();
    }

    /// <summary>仅刷新边框颜色、区块背景等视觉状态。</summary>
    internal void RefreshVisual()
    {
        if (_node is null) return;

        Color border, header, body, footer;
        switch (_node)
        {
            case NodeViewModel w:
                border = ParseColor(w.ChromeBorderBrush, Color.FromArgb(75, 85, 99));
                header = ParseColor(w.HeaderBackground, DarkHeader);
                body = ParseColor(w.ChromeBackground, DarkBody);
                footer = DarkHeader;
                break;
            case ControllerViewModel c:
                border = c.IsActive ? Color.FromArgb(103, 232, 249) : Color.White;
                header = c.IsActive ? Color.FromArgb(21, 94, 117) : DarkHeader;
                body = DarkBody;
                footer = DarkHeader;
                break;
            case BoolSelectorNodeViewModel:
                border = Color.FromArgb(110, 198, 255);
                header = Color.FromArgb(37, 53, 69);
                body = Color.FromArgb(30, 42, 53);
                footer = body;
                break;
            case EnumSelectorNodeViewModel:
                border = Color.FromArgb(214, 160, 255);
                header = Color.FromArgb(58, 37, 80);
                body = Color.FromArgb(42, 30, 53);
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

    // ── 绘制（圆角边框）────────────────────────────────────────────────────────
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
        PositionOverlaySlotButtons();
    }

    /// <summary>将悬浮槽位按钮定位到卡片左中 / 右中边缘。</summary>
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UnsubscribeVm();
        }

        base.Dispose(disposing);
    }

    // ── 槽位按钮管理 ──────────────────────────────────────────────────────────

    private SlotButton AddSlotButton(IWorkflowSlotViewModel? slot)
    {
        var btn = new SlotButton();
        btn.Bind(slot);
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
    }

    // ── 布局构建 ──────────────────────────────────────────────────────────────

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
            case NodeViewModel: BuildWorker(); break;
            case ControllerViewModel: BuildController(); break;
            case BoolSelectorNodeViewModel: BuildBoolSelector(); break;
            case EnumSelectorNodeViewModel: BuildEnumSelector(); break;
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

    // ── 布局：Worker 节点 ─────────────────────────────────────────────────────
    private void BuildWorker()
    {
        SetRows(52F, 54F, true);

        // ── Header ──
        var headerTlp = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, BackColor = DarkHeader,
        };
        headerTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headerTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));

        var titleFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Margin = Padding.Empty,
            Padding = new Padding(12, 14, 12, 10), BackColor = DarkHeader,
        };
        _titleLabel = MakeLabel(Color.White, 11F, FontStyle.Bold, autoSize: true);
        _orderBadge = MakeBadge(Color.FromArgb(200, 255, 200), Color.FromArgb(31, 61, 31));
        _loadBadge = MakeBadge(Color.FromArgb(228, 216, 255), Color.FromArgb(43, 36, 64));
        titleFlow.Controls.Add(_titleLabel);
        titleFlow.Controls.Add(_orderBadge);
        titleFlow.Controls.Add(_loadBadge);
        headerTlp.Controls.Add(titleFlow, 0, 0);

        var durationTlp = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = Padding.Empty, Padding = new Padding(0, 8, 12, 6), BackColor = DarkHeader,
        };
        durationTlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
        durationTlp.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        durationTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        durationTlp.Controls.Add(MakeLabel(Color.FromArgb(191, 191, 191), 8F, FontStyle.Regular, autoSize: false, ContentAlignment.BottomRight, "耗时"), 0, 0);
        _durationValue = MakeLabel(Color.FromArgb(126, 200, 255), 16F, FontStyle.Bold, autoSize: false, ContentAlignment.TopRight);
        durationTlp.Controls.Add(_durationValue, 0, 1);
        headerTlp.Controls.Add(durationTlp, 1, 0);
        _headerPanel.Controls.Add(headerTlp);

        // ── Body ──
        var bodyHost = new Panel
        {
            Dock = DockStyle.Fill, AutoScroll = true,
            BackColor = DarkBody, Padding = new Padding(14),
        };
        var bodyTlp = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = DarkBody,
        };
        bodyTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bodyTlp.Controls.Add(MakeEditorRow("Delay (ms)", out _delayBox), 0, 0);
        bodyTlp.Controls.Add(MakeEditorRow("Title", out _titleBox), 0, 1);
        bodyTlp.Controls.Add(MakeCheckRow("执行完成后自动广播到下游", out _autoBroadcastCheck), 0, 2);
        bodyTlp.Controls.Add(BuildExecPanel(), 0, 3);
        bodyHost.Controls.Add(bodyTlp);
        _bodyPanel.Controls.Add(bodyHost);

        // ── Footer ──
        var footerTlp = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(1), BackColor = DarkBody,
        };
        footerTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        footerTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        footerTlp.Controls.Add(MakeCmdButton("Run", nameof(NodeViewModel.WorkCommand)), 0, 0);
        footerTlp.Controls.Add(MakeCmdButton("Forward", nameof(NodeViewModel.BroadcastCommand)), 1, 0);
        _footerPanel.Controls.Add(footerTlp);

        InputSlotButton = AddSlotButton(null);
        OutputSlotButton = AddSlotButton(null);
    }

    private Panel BuildExecPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top, Height = 146,
            Margin = Padding.Empty, Padding = new Padding(10), BackColor = DarkExec,
        };
        panel.Paint += OnExecPanelPaint;

        var tlp = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7,
            Margin = Padding.Empty, Padding = Padding.Empty, BackColor = DarkExec,
        };
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        int[] rowHeights = [20, 20, 20, 36, 20, 20, 0];
        for (var i = 0; i < rowHeights.Length; i++)
        {
            tlp.RowStyles.Add(i < rowHeights.Length - 1
                ? new RowStyle(SizeType.Absolute, rowHeights[i])
                : new RowStyle(SizeType.Percent, 100F));
        }

        tlp.Controls.Add(MakeLabel(Color.FromArgb(126, 200, 255), 9F, FontStyle.Bold, autoSize: false, ContentAlignment.MiddleLeft, "Execution"), 0, 0);
        _runCountLabel = MakeLabel(Color.FromArgb(255, 213, 74), 8.6F, FontStyle.Bold, autoSize: false, ContentAlignment.MiddleLeft);
        _waitCountLabel = MakeLabel(Color.FromArgb(214, 183, 255), 8.6F, FontStyle.Bold, autoSize: false, ContentAlignment.MiddleLeft);
        _traceLabel = MakeLabel(Color.FromArgb(158, 231, 158), 8.6F, FontStyle.Bold, autoSize: false, ContentAlignment.MiddleLeft);
        _statusLabel = MakeLabel(Color.White, 8.6F, FontStyle.Bold, autoSize: false, ContentAlignment.MiddleLeft);
        _bodyDuration = MakeLabel(Color.FromArgb(126, 200, 255), 16F, FontStyle.Bold, autoSize: false, ContentAlignment.MiddleLeft);
        tlp.Controls.Add(_runCountLabel, 0, 1);
        tlp.Controls.Add(_waitCountLabel, 0, 2);
        tlp.Controls.Add(_traceLabel, 0, 3);
        tlp.Controls.Add(_statusLabel, 0, 4);
        tlp.Controls.Add(_bodyDuration, 0, 5);

        var bottomTlp = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = Padding.Empty, Padding = Padding.Empty, BackColor = DarkExec,
        };
        bottomTlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        bottomTlp.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        bottomTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _errorLabel = MakeLabel(Color.FromArgb(255, 155, 155), 8.2F, FontStyle.Regular, autoSize: false, ContentAlignment.MiddleLeft);
        _responseLabel = MakeLabel(Color.FromArgb(207, 207, 207), 8.2F, FontStyle.Regular, autoSize: false, ContentAlignment.TopLeft);
        bottomTlp.Controls.Add(_errorLabel, 0, 0);
        bottomTlp.Controls.Add(_responseLabel, 0, 1);
        tlp.Controls.Add(bottomTlp, 0, 6);
        panel.Controls.Add(tlp);
        return panel;
    }

    private void OnExecPanelPaint(object? sender, PaintEventArgs e)
    {
        if (sender is not Panel p || _node is not NodeViewModel node) return;
        var borderColor = ParseColor(node.ExecutionBorderBrush, Color.Transparent);
        if (borderColor == Color.Transparent) return;
        var rect = new Rectangle(0, 0, p.Width - 1, p.Height - 1);
        using var pen = new Pen(borderColor, 1.5F);
        e.Graphics.DrawRectangle(pen, rect);
    }

    // ── 布局：Controller ──────────────────────────────────────────────────────
    private void BuildController()
    {
        SetRows(52F, 56F, true);

        _titleLabel = MakeLabel(Color.White, 10.5F, FontStyle.Bold, autoSize: false, ContentAlignment.MiddleCenter, "Network Flow Controller");
        _titleLabel.Dock = DockStyle.Fill;
        _headerPanel.Controls.Add(_titleLabel);

        var bodyTlp = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Margin = Padding.Empty, Padding = new Padding(16), BackColor = DarkBody,
        };
        bodyTlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        bodyTlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        bodyTlp.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        bodyTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bodyTlp.Controls.Add(MakeLabel(Color.FromArgb(220, 220, 220), 9F, FontStyle.Regular, autoSize: false, ContentAlignment.MiddleLeft, "Seed Payload"), 0, 0);
        _seedBox = MakeTextBox();
        _seedBox.TextChanged += OnSeedTextChanged;
        bodyTlp.Controls.Add(_seedBox, 0, 1);
        _controllerDesc = MakeLabel(Color.FromArgb(189, 189, 189), 8.5F, FontStyle.Regular, autoSize: false, ContentAlignment.TopLeft);
        _controllerDesc.Dock = DockStyle.Fill;
        bodyTlp.Controls.Add(_controllerDesc, 0, 2);
        _bodyPanel.Controls.Add(bodyTlp);

        var footerTlp = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(8), BackColor = DarkBody,
        };
        footerTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        footerTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        footerTlp.Controls.Add(MakeCmdButton("Run Flow", nameof(ControllerViewModel.OpenWorkflowCommand)), 0, 0);
        footerTlp.Controls.Add(MakeCmdButton("Close Tree", nameof(ControllerViewModel.CloseWorkflowCommand)), 1, 0);
        _footerPanel.Controls.Add(footerTlp);

        OutputSlotButton = AddSlotButton(null);
    }

    // ── 布局：BoolSelector ────────────────────────────────────────────────────
    private void BuildBoolSelector()
    {
        SetRows(48F, 0F, false);

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Margin = Padding.Empty,
            Padding = new Padding(12, 14, 12, 10), BackColor = Color.FromArgb(37, 53, 69),
        };
        _titleLabel = MakeLabel(Color.White, 10.5F, FontStyle.Bold, autoSize: true);
        _routedBadge = MakeBadge(Color.FromArgb(200, 255, 200), Color.FromArgb(31, 61, 31));
        flow.Controls.Add(_titleLabel);
        flow.Controls.Add(_routedBadge);
        _headerPanel.Controls.Add(flow);

        var bodyTlp = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Margin = Padding.Empty, Padding = new Padding(14), BackColor = Color.FromArgb(30, 42, 53),
        };
        bodyTlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        bodyTlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        bodyTlp.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        bodyTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        _conditionCheck = new CheckBox
        {
            AutoSize = true, ForeColor = Color.FromArgb(220, 220, 220),
            Text = "Condition = True", Margin = Padding.Empty, Dock = DockStyle.Fill,
            UseVisualStyleBackColor = false,
        };
        _conditionCheck.CheckedChanged += OnConditionChanged;
        bodyTlp.Controls.Add(_conditionCheck, 0, 0);
        bodyTlp.Controls.Add(MakeLabel(Color.FromArgb(191, 191, 191), 8.5F, FontStyle.Regular, autoSize: false, ContentAlignment.MiddleLeft, "Output Slots"), 0, 1);

        _outputSlotsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty,
            Padding = Padding.Empty, BackColor = Color.FromArgb(30, 42, 53),
        };
        _outputSlotsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _outputSlotsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28F));
        bodyTlp.Controls.Add(_outputSlotsLayout, 0, 2);
        _bodyPanel.Controls.Add(bodyTlp);

        InputSlotButton = AddSlotButton(null);
    }

    // ── 布局：EnumSelector ────────────────────────────────────────────────────
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

        var bodyTlp = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Margin = Padding.Empty, Padding = new Padding(14), BackColor = Color.FromArgb(42, 30, 53),
        };
        bodyTlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        bodyTlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        bodyTlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        bodyTlp.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        bodyTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bodyTlp.Controls.Add(MakeLabel(Color.FromArgb(191, 191, 191), 8.5F, FontStyle.Regular, autoSize: false, ContentAlignment.MiddleLeft, "Selected Method"), 0, 0);
        _enumCombo = MakeComboBox();
        _enumCombo.SelectedIndexChanged += OnEnumValueChanged;
        bodyTlp.Controls.Add(_enumCombo, 0, 1);
        bodyTlp.Controls.Add(MakeLabel(Color.FromArgb(191, 191, 191), 8.5F, FontStyle.Regular, autoSize: false, ContentAlignment.MiddleLeft, "Output Slots"), 0, 2);

        _outputSlotsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty,
            Padding = Padding.Empty, BackColor = Color.FromArgb(42, 30, 53),
        };
        _outputSlotsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _outputSlotsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28F));
        bodyTlp.Controls.Add(_outputSlotsLayout, 0, 3);
        _bodyPanel.Controls.Add(bodyTlp);

        InputSlotButton = AddSlotButton(null);
    }

    // ── 数据应用 ──────────────────────────────────────────────────────────────
    private void ApplyWorker(NodeViewModel n)
    {
        SetText(_titleLabel, n.Title);
        SetText(_orderBadge, n.ExecutionOrderText);
        SetVisible(_orderBadge, n.HasExecutionOrder);
        SetText(_loadBadge, n.WorkLoadText);
        SetVisible(_loadBadge, n.HasWorkLoad);
        SetText(_durationValue, n.LastDuration);
        SetText(_delayBox, n.DelayMilliseconds.ToString(CultureInfo.InvariantCulture));
        SetText(_titleBox, n.Title);
        SetChecked(_autoBroadcastCheck, n.AutoBroadcast);
        SetText(_runCountLabel, $"Active: {n.RunCount}");
        SetText(_waitCountLabel, $"Queued: {n.WaitCount}");
        SetText(_traceLabel, $"Order: {n.LastExecutionTrace}");
        SetText(_statusLabel, $"Status: {n.LastStatus}");
        SetText(_bodyDuration, n.LastDuration);
        SetText(_errorLabel, $"Error: {n.LastError}");
        SetText(_responseLabel, n.LastResponsePreview);
        InputSlotButton?.Bind(n.InputSlot);
        OutputSlotButton?.Bind(n.OutputSlot);
    }

    private void ApplyController(ControllerViewModel c)
    {
        SetText(_seedBox, c.SeedPayload);
        if (_controllerDesc is not null)
        {
            _controllerDesc.Text = c.IsActive
                ? "The controller is currently streaming the initial context into the workflow."
                : "The controller only pushes the initial context into the workflow.";
        }

        OutputSlotButton?.Bind(c.OutputSlot);
    }

    private void ApplyBoolSelector(BoolSelectorNodeViewModel b)
    {
        SetText(_titleLabel, b.Title);
        SetText(_routedBadge, b.LastRouted);
        SetVisible(_routedBadge, !string.IsNullOrEmpty(b.LastRouted) && b.LastRouted != "-");
        SetChecked(_conditionCheck, b.Condition);
        InputSlotButton?.Bind(b.InputSlot);
        RebuildBoolSlots(b);
    }

    private void ApplyEnumSelector(EnumSelectorNodeViewModel e)
    {
        SetText(_titleLabel, e.Title);
        SetText(_routedBadge, e.LastRouted);
        SetVisible(_routedBadge, !string.IsNullOrEmpty(e.LastRouted) && e.LastRouted != "-");
        InputSlotButton?.Bind(e.InputSlot);
        UpdateEnumCombo(e);
        RebuildEnumSlots(e);
    }

    // ── 动态 Slot 行（BoolSelector）──────────────────────────────────────────
    private void RebuildBoolSlots(BoolSelectorNodeViewModel b)
    {
        if (_outputSlotsLayout is null) return;

        var entries = new (string Name, IWorkflowSlotViewModel? Slot)[]
        {
            ("False", b.FalseSlot),
            ("True", b.TrueSlot),
        };

        RebuildDynamicSlots(entries, Color.FromArgb(110, 198, 255));
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

        // 尝试复用：只有数量相同时才复用
        if (_dynamicSlotRows.Count == entries.Count)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                _dynamicSlotRows[i].label.Text = entries[i].Name;
                _dynamicSlotRows[i].label.ForeColor = labelColor;
                _dynamicSlotRows[i].slot.Bind(entries[i].Slot);
            }

            return;
        }

        // 重建
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
            _outputSlotsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            var lbl = MakeLabel(labelColor, 8.8F, FontStyle.Bold, autoSize: false, ContentAlignment.MiddleRight);
            lbl.Dock = DockStyle.Fill;
            lbl.Text = entries[i].Name;
            _outputSlotsLayout.Controls.Add(lbl, 0, i);

            var btn = new SlotButton { Margin = new Padding(2, 4, 2, 4) };
            btn.Bind(entries[i].Slot);
            _outputSlotsLayout.Controls.Add(btn, 1, i);
            _dynamicSlotRows.Add((lbl, btn));
        }

        _outputSlotsLayout.ResumeLayout();
    }

    private void UpdateEnumCombo(EnumSelectorNodeViewModel e)
    {
        if (_enumCombo is null) return;
        var values = e.EnumValues;
        if (_enumCombo.Items.Count != values.Length)
        {
            _enumCombo.Items.Clear();
            foreach (var v in values) _enumCombo.Items.Add(v);
        }

        if (!Equals(_enumCombo.SelectedItem, e.SelectedValue))
            _enumCombo.SelectedItem = e.SelectedValue;
    }

    // ── 用户输入事件 ──────────────────────────────────────────────────────────
    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(new PropertyChangedEventHandler(OnNodePropertyChanged), sender, e); return; }
        if (_node is not null) Refresh(_node);
    }

    private void OnDelayTextChanged(object? sender, EventArgs e)
    {
        if (_updatingFromVm || _node is not NodeViewModel node || _delayBox is null) return;
        if (int.TryParse(_delayBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && node.DelayMilliseconds != v)
            node.DelayMilliseconds = v;
    }

    private void OnTitleTextChanged(object? sender, EventArgs e)
    {
        if (_updatingFromVm || _node is not NodeViewModel node || _titleBox is null) return;
        if (!string.Equals(node.Title, _titleBox.Text, StringComparison.Ordinal))
            node.Title = _titleBox.Text;
    }

    private void OnAutoBroadcastChanged(object? sender, EventArgs e)
    {
        if (_updatingFromVm || _node is not NodeViewModel node || _autoBroadcastCheck is null) return;
        if (node.AutoBroadcast != _autoBroadcastCheck.Checked)
            node.AutoBroadcast = _autoBroadcastCheck.Checked;
    }

    private void OnSeedTextChanged(object? sender, EventArgs e)
    {
        if (_updatingFromVm || _node is not ControllerViewModel c || _seedBox is null) return;
        if (!string.Equals(c.SeedPayload, _seedBox.Text, StringComparison.Ordinal))
            c.SeedPayload = _seedBox.Text;
    }

    private void OnConditionChanged(object? sender, EventArgs e)
    {
        if (_updatingFromVm || _node is not BoolSelectorNodeViewModel node || _conditionCheck is null) return;
        if (node.Condition != _conditionCheck.Checked)
            node.Condition = _conditionCheck.Checked;
    }

    private void OnEnumValueChanged(object? sender, EventArgs e)
    {
        if (_updatingFromVm || _node is not EnumSelectorNodeViewModel node || _enumCombo is null) return;
        if (!Equals(node.SelectedValue, _enumCombo.SelectedItem))
            node.SelectedValue = _enumCombo.SelectedItem;
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

    // ── 辅助 ──────────────────────────────────────────────────────────────────
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
        _titleLabel = _orderBadge = _loadBadge = _durationValue = _routedBadge = null;
        _delayBox = _titleBox = _seedBox = null;
        _autoBroadcastCheck = _conditionCheck = null;
        _enumCombo = null;
        _runCountLabel = _waitCountLabel = _traceLabel = _statusLabel = _bodyDuration = null;
        _errorLabel = _responseLabel = _controllerDesc = null;
        _outputSlotsLayout = null;
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

    // ── 控件工厂 ──────────────────────────────────────────────────────────────
    private static Panel MakeSection()
        => new() { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = DarkBody };

    private static Label MakeLabel(Color fore, float size, FontStyle style, bool autoSize,
        ContentAlignment align = ContentAlignment.MiddleLeft, string text = "")
        => new()
        {
            AutoSize = autoSize,
            ForeColor = fore,
            BackColor = Color.Transparent,
            Font = new Font("Microsoft YaHei UI", size, style),
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            TextAlign = align,
            Text = text,
            Dock = autoSize ? DockStyle.None : DockStyle.Fill,
        };

    private static Label MakeBadge(Color fore, Color back)
        => new()
        {
            AutoSize = true,
            ForeColor = fore,
            BackColor = back,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Bold),
            Margin = new Padding(8, 0, 0, 0),
            Padding = new Padding(6, 2, 6, 2),
            Visible = false,
        };

    private TableLayoutPanel MakeEditorRow(string caption, out TextBox textBox)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 10),
            Padding = Padding.Empty, BackColor = DarkBody,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.Controls.Add(MakeLabel(Color.FromArgb(220, 220, 220), 9F, FontStyle.Regular, autoSize: false, ContentAlignment.MiddleLeft, caption), 0, 0);
        textBox = MakeTextBox();
        textBox.TextChanged += caption.Contains("Delay") ? OnDelayTextChanged : OnTitleTextChanged;
        row.Controls.Add(textBox, 1, 0);
        return row;
    }

    private TableLayoutPanel MakeCheckRow(string caption, out CheckBox check)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 10),
            Padding = Padding.Empty, BackColor = DarkBody,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        check = new CheckBox
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty,
            UseVisualStyleBackColor = false, BackColor = DarkBody,
        };
        check.CheckedChanged += OnAutoBroadcastChanged;
        row.Controls.Add(check, 0, 0);
        row.Controls.Add(MakeLabel(Color.FromArgb(220, 220, 220), 8.8F, FontStyle.Regular, autoSize: false, ContentAlignment.MiddleLeft, caption), 1, 0);
        return row;
    }

    private static TextBox MakeTextBox()
        => new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(55, 55, 55),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 2, 0, 2),
        };

    private static ComboBox MakeComboBox()
        => new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(55, 55, 55),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 2, 0, 2),
        };

    private Button MakeCmdButton(string text, string cmdProp)
    {
        var btn = new Button
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.White,
            Text = text,
            Tag = cmdProp,
            AccessibleName = text,
        };
        btn.FlatAppearance.BorderColor = Color.FromArgb(71, 85, 105);
        btn.Click += OnCommandButtonClick;
        return btn;
    }
}

/// <summary>
/// 工作流槽位的可点击按钮控件（圆形，owner-draw）。
/// 直接内嵌在 WorkflowNodeCard 或 _outputSlotsLayout 中。
/// 槽位在画布层的 anchor 由 WorkflowCanvas 读取此按钮的屏幕位置计算。
/// </summary>
internal sealed class SlotButton : Control
{
    private IWorkflowSlotViewModel? _slot;
    private INotifyPropertyChanged? _notifier;

    internal IWorkflowSlotViewModel? ViewModel => _slot;

    internal SlotButton()
    {
        Size = new System.Drawing.Size(20, 20);
        Margin = Padding.Empty;
        Cursor = Cursors.Hand;
        TabStop = false;
        WorkflowBehaviors.WorkflowSlotConnectionBehavior.SetIsEnabled(this, true);

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
    }

    internal void Bind(IWorkflowSlotViewModel? slot)
    {
        if (ReferenceEquals(_slot, slot)) return;

        if (_notifier is not null)
        {
            _notifier.PropertyChanged -= OnSlotChanged;
            _notifier = null;
        }

        _slot = slot;
        Tag = slot;
        Visible = slot is not null;

        if (slot is INotifyPropertyChanged n)
        {
            _notifier = n;
            n.PropertyChanged += OnSlotChanged;
        }

        Invalidate();
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // 用父控件背景色填充，避免 WinForms 透明背景异常
        e.Graphics.Clear(Parent?.BackColor ?? Color.FromArgb(30, 42, 53));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_slot is null) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(SlotColor(_slot.State));
        using var pen = new Pen(Color.White, 1.4F);
        var rect = new RectangleF(1.5F, 1.5F, Width - 3F, Height - 3F);
        e.Graphics.FillEllipse(brush, rect);
        e.Graphics.DrawEllipse(pen, rect);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _notifier is not null)
        {
            _notifier.PropertyChanged -= OnSlotChanged;
            _notifier = null;
        }

        base.Dispose(disposing);
    }

    private void OnSlotChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(new PropertyChangedEventHandler(OnSlotChanged), sender, e); return; }
        Invalidate();
    }

    private static Color SlotColor(SlotState state) => state switch
    {
        var s when s.HasFlag(SlotState.Sender) && s.HasFlag(SlotState.Receiver) => Color.Violet,
        var s when s.HasFlag(SlotState.Sender) => Color.Tomato,
        var s when s.HasFlag(SlotState.Receiver) => Color.Lime,
        _ => Color.White,
    };
}
