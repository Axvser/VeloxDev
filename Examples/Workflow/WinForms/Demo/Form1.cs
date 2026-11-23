using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Views.Workbench;
using Demo.Views.Workflow;
using Demo.Workflow;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.AI.Safety;
using VeloxDev.AI.SubAgents;
using VeloxDev.MVVM;
using VeloxDev.Serialization;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo
{
    public partial class Form1 : Form
    {
        private WorkflowDemoSession? _demo;

        // Agent 一轮里每个工具调用都要求刷新，而一次刷新是全量的（重新定位命名控件、跑布局、重算可见区）。
        // 合并之后一轮只刷新一次 —— 刷新本身是幂等的，后来的请求要的正是前一次即将看到的状态。
        private CoalescedRefresh? _surfaceRefresh;

        // ── VS Code 式外壳 ───────────────────────────────────────────────────
        // 48px 活动栏 + 272px 侧栏 | 主区（标签页 / 画布 / 输出） | 348px 右侧检查器。
        // 命名控件仍由 Designer 创建，Build* 方法只把它们搬到对应页；事件与绑定原样保留。
        private static readonly Color ActivityBack = WorkbenchPalette.ActivityBar;
        private static readonly Color ActivityHover = WorkbenchPalette.Hover;
        private static readonly Color ActivityActive = WorkbenchPalette.Accent;
        private static readonly Color ActivityGlyph = WorkbenchPalette.TextTertiary;
        private static readonly Color SidebarBack = WorkbenchPalette.Panel;
        private static readonly Color SidebarField = WorkbenchPalette.Input;
        private static readonly Color SidebarText = WorkbenchPalette.TextSecondary;
        private static readonly Color SidebarValue = WorkbenchPalette.TextPrimary;
        private static readonly Color SidebarMuted = WorkbenchPalette.TextTertiary;
        private static readonly Color SidebarAccent = WorkbenchPalette.AccentText;
        private static readonly Color SidebarHover = WorkbenchPalette.Hover;
        private static readonly Color SidebarButtonBack = WorkbenchPalette.Input;
        private static readonly Color SidebarBorder = WorkbenchPalette.Border;

        private readonly Dictionary<string, Control> _sidebarPages = [];
        private readonly Dictionary<string, ActivityButton> _activityButtons = [];
        private Label _sidebarHeaderLabel = null!;
        private Panel _sidebarPageHost = null!;
        private Panel _explorerElementHost = null!;
        private Panel _mcpHost = null!;
        private ToolTip _toolTip = null!;
        private string _sidebarActivity = "explorer";
        private string _inspectorPage = "properties";

        private WorkflowElementPanel? _elementPanel;

        // ── 主区 ─────────────────────────────────────────────────────────────
        private SplitContainer _rightSplit = null!;
        private Panel _mainContent = null!;
        private Panel _outputPanel = null!;
        private WorkbenchTabButton _canvasTab = null!;
        private WorkbenchTabButton _outputTab = null!;
        private Label _mainHintLabel = null!;
        private Label _outputRootLabel = null!;
        private Label _outputStateLabel = null!;
        private Label _outputProgressLabel = null!;
        private Label _outputCountLabel = null!;
        private Label _outputHintLabel = null!;
        private WorkflowRunTaskViewModel? _outputTask;

        // ── 右侧检查器 ───────────────────────────────────────────────────────
        private Panel _inspectorHost = null!;
        private Panel _inspectorPageHost = null!;
        private Panel _propertiesPage = null!;
        private Panel _agentPage = null!;
        private WorkbenchTabButton _propertiesTab = null!;
        private WorkbenchTabButton _agentTab = null!;
        private WorkflowInspectorPanel _inspector = null!;
        private RunManagerPanel _runManager = null!;
        private SubAgentPanel _subAgentPanel = null!;
        private StatusBarStrip _statusBar = null!;
        private SubAgentScope? _attachedSubAgents;

        private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 500 };

        // 这里设置的指标发生在 WinForms 自动缩放之后，必须自己把 96-DPI 设计值换成当前监视器像素。
        private int Dip(int logical) => (int)Math.Round(logical * DeviceDpi / 96f);

        public Form1()
        {
            InitializeComponent();
            KeyPreview = true;
            splitContainer.Panel1.Padding = Padding.Empty;
            splitContainer.Panel2.Padding = Padding.Empty;
            splitContainer.SplitterWidth = 4;
            splitContainer.SplitterDistance = 320;
            splitContainer.Panel1MinSize = 200;
            splitContainer.Panel2MinSize = 400;
            splitContainer.BackColor = WorkbenchPalette.Splitter;

            BuildWorkbenchFrame();
            BuildMainArea();
            BuildInspector();
            BuildSidebar();

            _uiTimer.Tick += OnUiTimerTick;
            _uiTimer.Start();

            workflowSurfaceControl.StatusMessage += OnSurfaceStatus;

            LoadDemo(WorkflowDemoSession.Create());
            FormClosed += (_, _) =>
            {
                _uiTimer.Stop();
                _uiTimer.Dispose();
            };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            PositionInspector();
            RepaintShellDeferred();
        }

        /// <inheritdoc />
        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);

            // 外壳由 AutoScaleMode.Font 缩放；排到消息队列末尾再按新 DPI 重新钉住检查器宽度。
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(new Action(() =>
                {
                    PositionInspector();
                    RepaintShellDeferred();
                }));
            }
        }

        // 菜单上印的手势必须与焦点无关：画布/输入框拿焦点时，那一层的 ProcessCmdKey 没接手就冒泡到这里。
        // 文本框自己会先吃掉 Ctrl+Z/Ctrl+Y，其余情况由窗体统一执行，效果与画布内那条 Core 路由一致。
        /// <inheritdoc />
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            var control = (keyData & Keys.Control) != 0;
            var shift = (keyData & Keys.Shift) != 0;

            if (control && shift && key == Keys.P)
            {
                ShowCommandCenter(null);
                return true;
            }

            if (control && !shift)
            {
                switch (key)
                {
                    case Keys.S:
                        SaveWorkflow(this, EventArgs.Empty);
                        return true;
                    case Keys.O:
                        SelectWorkflow(this, EventArgs.Empty);
                        return true;
                    case Keys.Z:
                        // 画布聚焦时让 Core 的按键路由照旧处理，这一层只补「焦点在别处」的缺口。
                        if (workflowSurfaceControl.ContainsFocus) break;
                        UndoWorkflow(this, EventArgs.Empty);
                        return true;
                    case Keys.Y:
                        if (workflowSurfaceControl.ContainsFocus) break;
                        RedoWorkflow(this, EventArgs.Empty);
                        return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        // The shell's cards and strips are custom-painted containers with child labels. WinForms can finish the
        // parent's paint after a child has drawn, which leaves a label invisible until its own WM_PAINT runs; a
        // single invalidate(true) on the containers after layout puts every label back.
        private void RepaintShellDeferred()
        {
            if (IsHandleCreated && !IsDisposed) BeginInvoke(RepaintShell);
        }

        private void RepaintShell()
        {
            if (IsDisposed) return;

            _statusBar.Refresh();
            _inspector.Refresh();
            _runManager.Refresh();
            _outputPanel.Refresh();
            _agentPage.Refresh();
            _subAgentPanel.Refresh();
            _mainContent.Refresh();
        }

        // The inspector's width is a constant of the shell, not a fraction: the Avalonia grid pins it at 348 DIPs.
        // SplitterDistance is only valid after the container has a width, so it is set once the form is shown.
        private void PositionInspector()
        {
            if (_rightSplit.IsDisposed || _rightSplit.Width <= 0) return;

            // Minimums are set only once the control has a width: assigning them while the splitter is still
            // at its construction size makes the framework clamp SplitterDistance to an impossible range.
            _rightSplit.Panel1MinSize = Dip(520);
            _rightSplit.Panel2MinSize = Dip(260);

            var distance = _rightSplit.Width - Dip(348) - _rightSplit.SplitterWidth;
            if (distance < _rightSplit.Panel1MinSize) return;
            if (distance > _rightSplit.Width - _rightSplit.Panel2MinSize) return;
            _rightSplit.SplitterDistance = distance;
        }

        // A 32px workbench title strip, the body and a 24px status strip.
        private void BuildWorkbenchFrame()
        {
            var title = BuildTitleStrip();

            _statusBar = new StatusBarStrip { Dock = DockStyle.Fill };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = WorkbenchPalette.Window,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));

            Controls.Clear();
            root.Controls.Add(title, 0, 0);
            root.Controls.Add(splitContainer, 0, 1);
            root.Controls.Add(_statusBar, 0, 2);
            Controls.Add(root);
        }

        // The title strip mirrors Avalonia's: brand + menu + command centre. The native window border
        // already owns minimise/maximise/close, so no fake window buttons are drawn here.
        private Control BuildTitleStrip()
        {
            var host = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = WorkbenchPalette.ActivityBar,
                Margin = Padding.Empty,
                Padding = new Padding(12, 0, 12, 0),
            };
            host.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            host.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            host.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var left = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = WorkbenchPalette.ActivityBar,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            left.Controls.Add(new Label
            {
                Text = "VeloxDev",
                AutoSize = true,
                ForeColor = WorkbenchPalette.TextPrimary,
                BackColor = WorkbenchPalette.ActivityBar,
                Font = new Font(CardTheme.UiFamily, 9F, FontStyle.Bold),
                Margin = new Padding(0, 0, 14, 0),
                Anchor = AnchorStyles.Left,
            });
            left.Controls.Add(CreateMenuButton("文件", BuildFileMenu()));
            left.Controls.Add(CreateMenuButton("编辑", BuildEditMenu()));
            left.Controls.Add(CreateMenuButton("视图", BuildViewMenu()));
            left.Controls.Add(CreateMenuButton("帮助", BuildHelpMenu()));
            host.Controls.Add(left, 0, 0);

            var centre = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = WorkbenchPalette.ActivityBar,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            centre.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            centre.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            centre.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            centre.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var command = new GhostButton
            {
                Text = "搜索命令    Ctrl+Shift+P",
                AutoSize = false,
                Size = new Size(330, 24),
                Radius = 4,
                TextColor = WorkbenchPalette.TextTertiary,
                BackColor = WorkbenchPalette.Input,
                Font = CardTheme.Font(8.5f),
                Anchor = AnchorStyles.None,
                Margin = Padding.Empty,
            };
            command.Click += (_, _) => ShowCommandCenter(command);
            centre.Controls.Add(command, 1, 0);
            host.Controls.Add(centre, 1, 0);

            var right = new Label
            {
                Text = "工作台",
                AutoSize = true,
                ForeColor = WorkbenchPalette.TextTertiary,
                BackColor = WorkbenchPalette.ActivityBar,
                Font = CardTheme.Font(8f),
                Anchor = AnchorStyles.Right,
                Margin = Padding.Empty,
            };
            host.Controls.Add(right, 2, 0);
            return host;
        }

        private GhostButton CreateMenuButton(string text, DarkPopupMenu menu)
        {
            var button = new GhostButton
            {
                Text = text,
                AutoSize = false,
                Size = new Size(52, 24),
                Radius = 3,
                FrameWidth = 0F,
                TextColor = WorkbenchPalette.TextSecondary,
                BackColor = WorkbenchPalette.ActivityBar,
                Font = CardTheme.Font(8.5f),
                Margin = new Padding(0, 0, 2, 0),
                Anchor = AnchorStyles.Left,
            };
            button.Click += (_, _) => menu.ShowBelow(button);
            return button;
        }

        private DarkPopupMenu BuildFileMenu()
        {
            var menu = new DarkPopupMenu();
            menu.AddItem("保存工作流", "Ctrl+S", () => SaveWorkflow(this, EventArgs.Empty));
            menu.AddItem("加载工作流", "Ctrl+O", () => SelectWorkflow(this, EventArgs.Empty));
            menu.AddItem("重新载入示例", null, () => ReloadWorkflow(this, EventArgs.Empty));
            menu.AddSeparator();
            menu.AddItem("退出", null, Close);
            return menu;
        }

        private DarkPopupMenu BuildEditMenu()
        {
            var menu = new DarkPopupMenu();
            menu.AddItem("撤销", "Ctrl+Z", () => UndoWorkflow(this, EventArgs.Empty));
            menu.AddItem("重做", "Ctrl+Y", () => RedoWorkflow(this, EventArgs.Empty));
            menu.AddSeparator();
            menu.AddItem("命令中心", "Ctrl+Shift+P", () => ShowCommandCenter(null));
            return menu;
        }

        private DarkPopupMenu BuildViewMenu()
        {
            var menu = new DarkPopupMenu();
            menu.AddItem("资源管理器", null, () => SelectActivity("explorer"));
            menu.AddItem("运行", null, () => SelectActivity("run"));
            menu.AddItem("MCP 服务器", null, () => SelectActivity("mcp"));
            menu.AddSeparator();
            menu.AddItem("属性", null, () => SelectInspectorPage("properties"));
            menu.AddItem("AI 助手", null, () => SelectInspectorPage("agent"));
            menu.AddItem("执行输出", null, () => SelectMainTab("output"));
            menu.AddSeparator();
            menu.AddItem("复制画布信息", null, () => OnCopyHud(null, EventArgs.Empty));
            return menu;
        }

        private DarkPopupMenu BuildHelpMenu()
        {
            var menu = new DarkPopupMenu();
            menu.AddItem("关于 VeloxDev", null, () => MessageBox.Show(
                this,
                "VeloxDev — Workflow Studio\n演示宿主：工作流画布、运行门、Agent 与 MCP 面板。",
                "关于 VeloxDev",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information));
            return menu;
        }

        private void ShowCommandCenter(Control? owner)
        {
            var menu = BuildCommandMenu();
            if (owner is not null)
            {
                menu.ShowBelow(owner);
                return;
            }

            menu.ShowAt(PointToScreen(new Point(Dip(330), Dip(24))), this);
        }

        private DarkPopupMenu BuildCommandMenu()
        {
            var menu = new DarkPopupMenu(320);
            menu.AddItem("保存工作流", "Ctrl+S", () => SaveWorkflow(this, EventArgs.Empty));
            menu.AddItem("加载工作流", "Ctrl+O", () => SelectWorkflow(this, EventArgs.Empty));
            menu.AddItem("重新载入示例", null, () => ReloadWorkflow(this, EventArgs.Empty));
            menu.AddSeparator();
            menu.AddItem("撤销", "Ctrl+Z", () => UndoWorkflow(this, EventArgs.Empty));
            menu.AddItem("重做", "Ctrl+Y", () => RedoWorkflow(this, EventArgs.Empty));
            menu.AddSeparator();
            menu.AddItem("资源管理器", null, () => SelectActivity("explorer"));
            menu.AddItem("运行", null, () => SelectActivity("run"));
            menu.AddItem("MCP 服务器", null, () => SelectActivity("mcp"));
            menu.AddItem("属性", null, () => SelectInspectorPage("properties"));
            menu.AddItem("AI 助手", null, () => SelectInspectorPage("agent"));
            menu.AddItem("执行输出", null, () => SelectMainTab("output"));
            return menu;
        }

        // ── Main area: editor tabs + canvas / output ─────────────────────────

        private void BuildMainArea()
        {
            splitContainer.Panel2.SuspendLayout();

            splitContainer.Panel2.Controls.Clear();
            splitContainer.Panel2.BackColor = WorkbenchPalette.Window;

            _rightSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel2,
                SplitterWidth = 4,
                BackColor = WorkbenchPalette.Splitter,
            };
            _rightSplit.Panel1.Padding = Padding.Empty;
            _rightSplit.Panel2.Padding = Padding.Empty;
            _rightSplit.Panel1.BackColor = WorkbenchPalette.Window;
            _rightSplit.Panel2.BackColor = WorkbenchPalette.Panel;

            var mainArea = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = WorkbenchPalette.Window,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            mainArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            mainArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            mainArea.Controls.Add(BuildMainTabStrip(), 0, 0);

            _mainContent = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = WorkbenchPalette.Editor,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };

            workflowSurfaceControl.Dock = DockStyle.Fill;
            workflowSurfaceControl.Margin = Padding.Empty;
            _mainContent.Controls.Add(workflowSurfaceControl);

            _outputPanel = BuildOutputPanel();
            _outputPanel.Dock = DockStyle.Fill;
            _outputPanel.Visible = false;
            _mainContent.Controls.Add(_outputPanel);

            mainArea.Controls.Add(_mainContent, 0, 1);
            _rightSplit.Panel1.Controls.Add(mainArea);

            _inspectorHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = WorkbenchPalette.Panel,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            _rightSplit.Panel2.Controls.Add(_inspectorHost);

            splitContainer.Panel2.Controls.Add(_rightSplit);
            splitContainer.Panel2.ResumeLayout(true);

            SelectMainTab("canvas");
        }

        private Panel BuildMainTabStrip()
        {
            var strip = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = WorkbenchPalette.Panel,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };

            _canvasTab = new WorkbenchTabButton { Text = "流程图", Width = 80, IsActive = true };
            _outputTab = new WorkbenchTabButton { Text = "输出", Width = 64 };
            _canvasTab.Click += (_, _) => SelectMainTab("canvas");
            _outputTab.Click += (_, _) => SelectMainTab("output");

            // 画布信息 HUD 是只画的；复制按钮是 WorkflowCanvas 的 demo-local 原生覆盖层。
            var tabs = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = WorkbenchPalette.Panel,
                Margin = Padding.Empty,
                Padding = new Padding(6, 0, 0, 0),
            };
            tabs.Controls.Add(_canvasTab);
            tabs.Controls.Add(_outputTab);

            _mainHintLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Ctrl+滚轮缩放",
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = WorkbenchPalette.TextTertiary,
                BackColor = Color.Transparent,
                Font = CardTheme.Font(8f),
                Margin = Padding.Empty,
            };

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = WorkbenchPalette.Panel,
                Margin = Padding.Empty,
                Padding = new Padding(0, 0, 12, 0),
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            header.Controls.Add(tabs, 0, 0);
            header.Controls.Add(_mainHintLabel, 1, 0);

            var line = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = WorkbenchPalette.BorderSubtle,
                Margin = Padding.Empty,
            };

            strip.Controls.Add(header);
            strip.Controls.Add(line);
            return strip;
        }

        private Panel BuildOutputPanel()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = WorkbenchPalette.Editor,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _outputRootLabel = CreateHeaderLabel(WorkbenchPalette.AccentText, 9.5f, FontStyle.Bold);
            _outputStateLabel = CreateHeaderLabel(WorkbenchPalette.TextTertiary, 8.5f);
            _outputProgressLabel = CreateHeaderLabel(WorkbenchPalette.TextTertiary, 8f);

            var headerFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = WorkbenchPalette.Panel,
                Margin = Padding.Empty,
                Padding = new Padding(0, 10, 0, 0),
            };
            var outputTitle = CreateHeaderLabel(WorkbenchPalette.TextSecondary, 9.5f, FontStyle.Bold);
            outputTitle.Text = "执行输出";
            outputTitle.Margin = new Padding(0, 0, 10, 0);
            headerFlow.Controls.Add(outputTitle);
            headerFlow.Controls.Add(_outputRootLabel);
            headerFlow.Controls.Add(_outputStateLabel);
            headerFlow.Controls.Add(_outputProgressLabel);

            _outputCountLabel = CreateHeaderLabel(WorkbenchPalette.TextTertiary, 8f);
            _outputCountLabel.TextAlign = ContentAlignment.MiddleRight;
            _outputCountLabel.Dock = DockStyle.Fill;
            _outputCountLabel.Margin = Padding.Empty;

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = WorkbenchPalette.Panel,
                Margin = Padding.Empty,
                Padding = new Padding(12, 0, 12, 0),
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            header.Controls.Add(headerFlow, 0, 0);
            header.Controls.Add(_outputCountLabel, 1, 0);

            var headerHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = WorkbenchPalette.Panel,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            headerHost.Controls.Add(header);

            var heading = CreateHeaderLabel(WorkbenchPalette.AccentText, 9f, FontStyle.Bold);
            heading.Text = "实际执行顺序";
            heading.AutoSize = true;
            heading.Margin = new Padding(0, 8, 0, 6);

            _outputHintLabel = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                MaximumSize = new Size(760, 0),
                Text = "运行一次，或让 Agent 编译 / 运行工作流。节点开始、完成、重试、重定向和运行收尾都会按发生顺序落在这里。",
                ForeColor = WorkbenchPalette.TextTertiary,
                BackColor = Color.Transparent,
                Font = CardTheme.Font(8f),
                Margin = new Padding(0, 0, 0, 6),
            };

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = WorkbenchPalette.Editor,
                Margin = Padding.Empty,
                Padding = new Padding(12, 0, 12, 12),
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            body.Controls.Add(heading, 0, 0);
            body.Controls.Add(_outputHintLabel, 0, 1);

            executionLogListBox.BackColor = WorkbenchPalette.Editor;
            executionLogListBox.ForeColor = WorkbenchPalette.TextPrimary;
            executionLogListBox.BorderStyle = BorderStyle.None;
            executionLogListBox.Dock = DockStyle.Fill;
            executionLogListBox.IntegralHeight = false;
            executionLogListBox.HorizontalScrollbar = true;
            executionLogListBox.Margin = Padding.Empty;
            executionLogListBox.Font = new Font(CardTheme.MonoFamily, 9.5F);
            body.Controls.Add(executionLogListBox, 0, 2);

            root.Controls.Add(headerHost, 0, 0);
            root.Controls.Add(body, 0, 1);

            RefreshOutputHeader();
            return root;
        }

        private static Label CreateHeaderLabel(Color color, float size, FontStyle style = FontStyle.Regular) => new()
        {
            AutoSize = true,
            ForeColor = color,
            BackColor = Color.Transparent,
            Font = CardTheme.Font(size, style),
            Margin = new Padding(0, 0, 8, 0),
        };

        private void SelectMainTab(string view)
        {
            var output = view == "output";
            _mainContent.Visible = true;
            workflowSurfaceControl.Visible = !output;
            _outputPanel.Visible = output;
            _canvasTab.IsActive = !output;
            _outputTab.IsActive = output;

            if (output) _outputPanel.BringToFront();
            else
            {
                workflowSurfaceControl.BringToFront();
                WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(workflowSurfaceControl);
            }

            RefreshOutputHeader();
            UpdateActivityMarkers();
            RepaintShellDeferred();
        }

        // ── Right inspector: 属性 / AI 助手 ──────────────────────────────────

        private void BuildInspector()
        {
            _inspectorHost.SuspendLayout();

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = WorkbenchPalette.Panel,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _propertiesTab = new WorkbenchTabButton { Text = "属性", Width = 62, IsActive = true };
            _agentTab = new WorkbenchTabButton { Text = "AI 助手", Width = 78 };
            _propertiesTab.Click += (_, _) => SelectInspectorPage("properties");
            _agentTab.Click += (_, _) => SelectInspectorPage("agent");

            var tabs = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = WorkbenchPalette.Panel,
                Margin = Padding.Empty,
                Padding = new Padding(6, 0, 0, 0),
            };
            tabs.Controls.Add(_propertiesTab);
            tabs.Controls.Add(_agentTab);

            var header = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = WorkbenchPalette.Panel,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            var headerLine = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = WorkbenchPalette.BorderSubtle,
            };
            header.Controls.Add(tabs);
            header.Controls.Add(headerLine);

            _inspectorPageHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = WorkbenchPalette.Panel,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };

            _propertiesPage = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = WorkbenchPalette.Panel,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            _inspector = new WorkflowInspectorPanel { Dock = DockStyle.Fill };
            _propertiesPage.Controls.Add(_inspector);

            _agentPage = BuildAgentPage();
            _agentPage.Dock = DockStyle.Fill;
            _agentPage.Visible = false;

            _inspectorPageHost.Controls.Add(_agentPage);
            _inspectorPageHost.Controls.Add(_propertiesPage);
            _propertiesPage.BringToFront();

            layout.Controls.Add(header, 0, 0);
            layout.Controls.Add(_inspectorPageHost, 0, 1);
            _inspectorHost.Controls.Add(layout);
            _inspectorHost.ResumeLayout(true);

            SelectInspectorPage("properties");
        }

        private Panel BuildAgentPage()
        {
            var page = new Panel
            {
                BackColor = WorkbenchPalette.Panel,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                BackColor = WorkbenchPalette.Panel,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var session = CreateVerticalStack(WorkbenchPalette.Panel);
            AddStackRow(session, CreateSectionLabel("会话", WorkbenchPalette.Panel));

            StyleCaptionLabel(permissionModeLabel);
            permissionModeLabel.Font = CardTheme.Font(8.5f);
            permissionModePicker.BackColor = WorkbenchPalette.Input;
            permissionModePicker.ForeColor = WorkbenchPalette.TextPrimary;
            permissionModePicker.FlatStyle = FlatStyle.Flat;
            permissionModePicker.Font = CardTheme.Font(8.5f);
            AddStackRow(session, CreatePairRow(permissionModeLabel, permissionModePicker, stretchValue: true, back: WorkbenchPalette.Panel));

            var chatHeader = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Text = "Agent 对话",
                ForeColor = WorkbenchPalette.AccentText,
                BackColor = Color.Transparent,
                Font = CardTheme.Font(9f, FontStyle.Bold),
                Margin = new Padding(12, 4, 12, 6),
            };

            agentLogListBox.BackColor = WorkbenchPalette.Surface;
            agentLogListBox.ForeColor = WorkbenchPalette.TextPrimary;
            agentLogListBox.BorderStyle = BorderStyle.None;
            agentLogListBox.Dock = DockStyle.Fill;
            agentLogListBox.IntegralHeight = false;
            agentLogListBox.HorizontalScrollbar = true;
            agentLogListBox.Margin = new Padding(12, 0, 12, 0);
            agentLogListBox.Font = new Font(CardTheme.MonoFamily, 9F);

            agentInputPanel.BackColor = WorkbenchPalette.Panel;
            agentInputPanel.Dock = DockStyle.Fill;
            agentInputPanel.Margin = new Padding(12, 6, 12, 6);
            agentInputTextBox.BackColor = WorkbenchPalette.Input;
            agentInputTextBox.ForeColor = WorkbenchPalette.TextPrimary;
            agentInputTextBox.BorderStyle = BorderStyle.FixedSingle;
            agentInputTextBox.Font = CardTheme.Font(9f);

            StyleSessionButton(agentSendButton);
            agentSendButton.BackColor = WorkbenchPalette.Accent;
            agentSendButton.ForeColor = Color.White;
            agentSendButton.FlatAppearance.BorderSize = 0;
            agentSendButton.FlatAppearance.MouseOverBackColor = WorkbenchPalette.AccentText;
            agentSendButton.AutoSize = false;
            agentSendButton.Width = 70;
            agentSendButton.Font = CardTheme.Font(9f, FontStyle.Bold);

            _subAgentPanel = new SubAgentPanel
            {
                Dock = DockStyle.Top,
                Margin = new Padding(12, 4, 12, 12),
            };

            layout.Controls.Add(session, 0, 0);
            layout.Controls.Add(chatHeader, 0, 1);
            layout.Controls.Add(agentLogListBox, 0, 2);
            layout.Controls.Add(agentInputPanel, 0, 3);
            layout.Controls.Add(_subAgentPanel, 0, 4);

            page.Controls.Add(layout);
            return page;
        }

        private void SelectInspectorPage(string view)
        {
            _inspectorPage = view;
            var properties = view == "properties";
            _propertiesPage.Visible = properties;
            _agentPage.Visible = !properties;
            _propertiesTab.IsActive = properties;
            _agentTab.IsActive = !properties;

            if (properties) _propertiesPage.BringToFront();
            else _agentPage.BringToFront();

            UpdateActivityMarkers();
            RepaintShellDeferred();
        }

        // ── Sidebar: activity bar + explorer / run / MCP ─────────────────────

        private void BuildSidebar()
        {
            _toolTip = new ToolTip
            {
                AutoPopDelay = 5000,
                InitialDelay = 300,
                ReshowDelay = 100,
                ShowAlways = true,
            };
            components.Add(_toolTip);

            splitContainer.Panel1.SuspendLayout();
            splitContainer.Panel1.Controls.Clear();
            splitContainer.Panel1.BackColor = WorkbenchPalette.Window;

            var sidebarLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = SidebarBack,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            sidebarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _sidebarHeaderLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = "资源管理器",
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = SidebarText,
                BackColor = SidebarBack,
                Font = new Font(CardTheme.UiFamily, 8.5F, FontStyle.Bold),
                Padding = new Padding(12, 0, 12, 0),
                Margin = Padding.Empty,
            };
            _sidebarPageHost = new Panel { Dock = DockStyle.Fill, BackColor = SidebarBack };

            sidebarLayout.Controls.Add(_sidebarHeaderLabel, 0, 0);
            sidebarLayout.Controls.Add(_sidebarPageHost, 0, 1);

            BuildExplorerPage();
            BuildRunPage();
            BuildMcpPage();

            var activityBar = new Panel { Dock = DockStyle.Fill, BackColor = ActivityBack };

            var topStack = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 4,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = ActivityBack,
                Margin = Padding.Empty,
                Padding = new Padding(0, 6, 0, 0),
            };
            topStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (var i = 0; i < 4; i++) topStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            AddActivityButton(topStack, 0, "explorer", "", "资源管理器");
            AddActivityButton(topStack, 1, "run", "", "运行");
            AddActivityButton(topStack, 2, "mcp", "", "MCP 服务器");
            AddActivityButton(topStack, 3, "output", "", "执行输出");

            var bottomStack = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                ColumnCount = 1,
                RowCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = ActivityBack,
                Margin = Padding.Empty,
                Padding = new Padding(0, 0, 0, 6),
            };
            bottomStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (var i = 0; i < 2; i++) bottomStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            AddActivityButton(bottomStack, 0, "properties", "", "属性");
            AddActivityButton(bottomStack, 1, "agent", "", "AI 助手");

            activityBar.Controls.Add(topStack);
            activityBar.Controls.Add(bottomStack);

            var rail = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = WorkbenchPalette.Window,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            rail.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48F));
            rail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            rail.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            rail.Controls.Add(activityBar, 0, 0);
            rail.Controls.Add(sidebarLayout, 1, 0);

            splitContainer.Panel1.Controls.Add(rail);
            splitContainer.Panel1.ResumeLayout(true);

            SelectActivity("explorer");
            SelectMainTab("canvas");
            SelectInspectorPage("properties");
        }

        private void AddActivityButton(TableLayoutPanel stack, int row, string key, string glyph, string tooltip)
        {
            var button = new ActivityButton
            {
                Text = glyph,
                Tag = key,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
            };
            _toolTip.SetToolTip(button, tooltip);
            button.Click += OnActivityClick;
            _activityButtons[key] = button;
            stack.Controls.Add(button, 0, row);
        }

        private Panel CreateSidebarPage(string key)
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = SidebarBack, Visible = false };
            _sidebarPages[key] = page;
            _sidebarPageHost.Controls.Add(page);
            return page;
        }

        private void OnActivityClick(object? sender, EventArgs e)
        {
            if (sender is Control { Tag: string key }) SelectActivity(key);
        }

        private void SelectActivity(string key)
        {
            switch (key)
            {
                case "properties":
                case "agent":
                    SelectInspectorPage(key);
                    return;
                case "output":
                    SelectMainTab("output");
                    return;
            }

            _sidebarActivity = key;
            foreach (var (name, page) in _sidebarPages)
                page.Visible = name == key;

            if (_sidebarPages.TryGetValue(key, out var active)) active.BringToFront();

            _sidebarHeaderLabel.Text = key switch
            {
                "run" => "运行",
                "mcp" => "MCP 服务器",
                _ => "资源管理器",
            };

            UpdateActivityMarkers();
            RepaintShellDeferred();
        }

        private void UpdateActivityMarkers()
        {
            if (_activityButtons.Count == 0) return;

            foreach (var (name, button) in _activityButtons)
            {
                button.IsActive = name switch
                {
                    "explorer" or "run" or "mcp" => _sidebarActivity == name,
                    "output" => _outputTab is { IsActive: true },
                    "properties" or "agent" => _inspectorPage == name,
                    _ => false,
                };
                button.Invalidate();
            }
        }

        // ── Page builders ────────────────────────────────────────────────────

        private void BuildExplorerPage()
        {
            var page = CreateSidebarPage("explorer");
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = SidebarBack,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // The workbench already has a brand + title strip; the old sidebar brand made the
            // explorer read as a second application header. Only the actions remain here, as
            // glyph buttons in the same 24px rhythm as the Avalonia panel header. The old
            // "Load Workflow Demo" button duplicated the refresh action and is no longer added.
            var actions = CreateButtonFlow();
            actions.Padding = new Padding(10, 8, 10, 0);
            foreach (var (button, glyph, tip) in new[]
                     {
                         (saveButton, "", "保存工作流"),
                         (selectButton, "", "加载工作流"),
                         (undoButton, "", "撤销"),
                         (redoButton, "", "重做"),
                         (reloadButton, "", "重新载入示例"),
                     })
            {
                StyleSidebarActionButton(button);
                button.Text = glyph;
                button.Font = new Font("Segoe Fluent Icons", 9.5F);
                button.AutoSize = false;
                button.Size = new Size(24, 24);
                button.Padding = Padding.Empty;
                button.Margin = new Padding(0, 0, 4, 0);
                button.AccessibleName = tip;
                _toolTip.SetToolTip(button, tip);
                actions.Controls.Add(button);
            }
            layout.Controls.Add(actions, 0, 0);
            layout.Controls.Add(CreateMinimapRow(), 0, 1);
            layout.Controls.Add(CreateSectionLabel("元素"), 0, 2);

            _explorerElementHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SidebarBack,
                Margin = new Padding(12, 0, 12, 0),
            };
            layout.Controls.Add(_explorerElementHost, 0, 3);

            page.Controls.Add(layout);
        }

        private Control CreateMinimapRow()
        {
            var label = new Label
            {
                AutoSize = true,
                Text = "小地图",
                ForeColor = SidebarText,
                BackColor = Color.Transparent,
                Font = CardTheme.Font(8.5f),
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 8, 0),
            };

            var toggle = new GhostButton
            {
                Text = "开",
                TextColor = CardTheme.ActionCompile,
                AutoSize = false,
                Size = new Size(44, 22),
                Anchor = AnchorStyles.Left,
                Margin = Padding.Empty,
            };
            toggle.Click += (_, _) =>
            {
                var visible = !workflowSurfaceControl.MinimapOverlay.IsMinimapVisible;
                workflowSurfaceControl.MinimapOverlay.IsMinimapVisible = visible;
                workflowSurfaceControl.MinimapOverlay.Visibility = visible
                    ? Easel.Controls.Visibility.Visible
                    : Easel.Controls.Visibility.Collapsed;
                workflowSurfaceControl.MinimapOverlay.InvalidateVisual();
                toggle.Text = visible ? "开" : "关";
                toggle.TextColor = visible ? CardTheme.ActionCompile : CardTheme.DisabledText;
            };

            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = SidebarBack,
                Margin = new Padding(12, 2, 12, 2),
                Padding = Padding.Empty,
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            row.Controls.Add(label, 0, 0);
            row.Controls.Add(toggle, 1, 0);
            return row;
        }

        private void BuildRunPage()
        {
            var page = CreateSidebarPage("run");
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 1,
                BackColor = SidebarBack,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // The seed belongs to the controller card (where it is edited); a second field under the run
            // manager was a second copy of the same state and did not exist in the Avalonia shell.
            _runManager = new RunManagerPanel { Dock = DockStyle.Fill };
            layout.Controls.Add(_runManager, 0, 0);

            page.Controls.Add(layout);
        }

        private void BuildMcpPage()
        {
            var page = CreateSidebarPage("mcp");
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = SidebarBack,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.Controls.Add(CreateSectionLabel("服务器"), 0, 0);

            _mcpHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SidebarBack,
                Margin = new Padding(12, 0, 12, 0),
            };
            layout.Controls.Add(_mcpHost, 0, 1);
            page.Controls.Add(layout);
        }

        // ── Sidebar building blocks ─────────────────────────────────────────

        private static TableLayoutPanel CreateVerticalStack(Color? back = null)
        {
            var stack = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 0,
                BackColor = back ?? SidebarBack,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            return stack;
        }

        private static void AddStackRow(TableLayoutPanel stack, Control control)
        {
            var row = stack.RowStyles.Count;
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowCount = stack.RowStyles.Count;
            control.Dock = DockStyle.Top;
            stack.Controls.Add(control, 0, row);
        }

        private static Label CreateSectionLabel(string text, Color? back = null) => new()
        {
            Text = text,
            AutoSize = true,
            Dock = DockStyle.Top,
            ForeColor = WorkbenchPalette.TextSecondary,
            BackColor = back ?? SidebarBack,
            Font = new Font(CardTheme.UiFamily, 8.5F, FontStyle.Bold),
            Margin = new Padding(12, 14, 12, 6),
        };

        private static TableLayoutPanel CreatePairRow(Control caption, Control value, bool stretchValue = false, Color? back = null)
        {
            var background = back ?? SidebarBack;
            caption.AutoSize = true;
            caption.Anchor = AnchorStyles.Left;
            caption.Margin = new Padding(0, 0, 8, 0);
            caption.BackColor = Color.Transparent;
            value.Margin = Padding.Empty;
            if (stretchValue)
            {
                value.Dock = DockStyle.Fill;
            }
            else
            {
                value.Anchor = AnchorStyles.Left;
            }

            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = background,
                Margin = new Padding(12, 2, 12, 2),
                Padding = Padding.Empty,
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.Controls.Add(caption, 0, 0);
            row.Controls.Add(value, 1, 0);
            return row;
        }

        private static FlowLayoutPanel CreateButtonFlow() => new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = SidebarBack,
            Margin = Padding.Empty,
            Padding = new Padding(12, 4, 12, 4),
            MaximumSize = new Size(320, 0),
        };

        private static void StyleSidebarActionButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = SidebarHover;
            button.FlatAppearance.MouseDownBackColor = SidebarField;
            button.BackColor = SidebarBack;
            button.ForeColor = SidebarText;
            button.UseVisualStyleBackColor = false;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Padding = new Padding(8, 3, 8, 3);
            button.Margin = new Padding(0, 0, 6, 6);
            button.Cursor = Cursors.Hand;
            button.Font = CardTheme.Font(8.5f);
        }

        private static void StyleSessionButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = SidebarBorder;
            button.FlatAppearance.MouseOverBackColor = SidebarHover;
            button.FlatAppearance.MouseDownBackColor = SidebarField;
            button.BackColor = SidebarButtonBack;
            button.ForeColor = SidebarText;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
        }

        private static void StyleCaptionLabel(Label label)
        {
            label.ForeColor = SidebarText;
            label.BackColor = Color.Transparent;
        }

        private async void ReloadWorkflow(object? sender, EventArgs e)
            => await ExecuteAsync(ReloadWorkflowInternalAsync, "Failed to reset demo");

        private void UndoWorkflow(object? sender, EventArgs e)
            => _demo?.Tree.UndoCommand.Execute(null);

        private void RedoWorkflow(object? sender, EventArgs e)
            => _demo?.Tree.RedoCommand.Execute(null);

        // HUD 的每一行都来自适配器的只读快照；复制失败只换状态栏文字，不再弹窗打断画布操作。
        private void OnCopyHud(object? sender, EventArgs e)
        {
            var lines = workflowSurfaceControl.InfoOverlay.Lines;
            if (lines.Count == 0) return;

            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, lines));
                _statusBar.Hint.Text = "已复制画布信息";
            }
            catch (ExternalException)
            {
                _statusBar.Hint.Text = "复制失败：剪贴板被占用";
            }
            catch (ThreadStateException)
            {
                _statusBar.Hint.Text = "复制失败：剪贴板不可用";
            }
        }

        private void OnSurfaceStatus(object? sender, string message)
        {
            if (IsDisposed) return;
            _statusBar.Hint.Text = message;
        }

        private void SaveWorkflow(object? sender, EventArgs e)
        {
            if (_demo is null) return;
            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                var path = System.IO.Path.Combine(dialog.SelectedPath, "Workflow.json");
                _demo.Tree.SaveCommand.Execute(path);
                MessageBox.Show(this, $"Workflow Saved At {dialog.SelectedPath}", "OK", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private async void SelectWorkflow(object? sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "选择工作流文件",
                Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                DefaultExt = ".json",
                CheckFileExists = true,
                CheckPathExists = true
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    using var stream = System.IO.File.OpenRead(dialog.FileName);
                    using var reader = new System.IO.StreamReader(stream);
                    var json = await reader.ReadToEndAsync();
                    if (IsDisposed) return;

                    var result = json.Deserialize<TreeViewModel>();

                    // 反序列化器对缺失字段写 null（生成器读值后跟 !），而 LoadDemo 与表面会立即解引用。
                    // 先整体验形，坏文件只弹一次错误，不留下半挂载的 session。
                    if (result is null
                        || result.Layout is null
                        || result.Nodes is null
                        || result.Links is null
                        || result.VirtualLink is null
                        || result.ExecutionLog is null
                        || result.AgentLog is null
                        || result.CompiledStructure is null)
                    {
                        MessageBox.Show(this, "Invalid workflow file: required fields are missing or null.",
                            "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    result.Layout = result.Layout.AdaptTo(
                        new VeloxDev.WorkflowSystem.Size(1920, 1080));
                    LoadDemo(WorkflowDemoSession.FromTree(result));
                    MessageBox.Show(this, $"Workflow loaded successfully from {dialog.FileName}.", "Load Succeeded", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Failed to load file: {ex.GetType().Name}\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private async void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            if (_demo is not null)
            {
                await _demo.Tree.GetHelper().CloseAsync();
            }
        }

        private async Task ReloadWorkflowInternalAsync()
        {
            if (_demo is not null)
            {
                await _demo.Tree.GetHelper().CloseAsync();
            }

            LoadDemo(WorkflowDemoSession.Create());
        }

        private void LoadDemo(WorkflowDemoSession session)
        {
            if (_demo is not null)
            {
                _demo.Controller.PropertyChanged -= OnControllerPropertyChanged;
                _demo.Tree.ExecutionLog.CollectionChanged -= OnExecutionLogCollectionChanged;
                _demo.Tree.AgentLog.CollectionChanged -= OnAgentLogCollectionChanged;
                _demo.Tree.Nodes.CollectionChanged -= OnNodesCollectionChanged;
                _demo.Tree.CompiledStructure.CollectionChanged -= OnCompiledStructureChanged;
                _demo.Tree.PropertyChanged -= OnTreePropertyChanged;
                _demo.Tree.Runs.PropertyChanged -= OnRunsPropertyChanged;
                if (_outputTask is not null) _outputTask.PropertyChanged -= OnOutputTaskPropertyChanged;
                _outputTask = null;
                _demo.Tree.GetHelper().VisibleItems.CollectionChanged -= OnVisibleItemsChanged;
                UnsubscribeHelper(_demo);
            }

            _demo = session;
            _demo.Controller.PropertyChanged += OnControllerPropertyChanged;
            _demo.Tree.ExecutionLog.CollectionChanged += OnExecutionLogCollectionChanged;
            _demo.Tree.AgentLog.CollectionChanged += OnAgentLogCollectionChanged;
            _demo.Tree.Nodes.CollectionChanged += OnNodesCollectionChanged;
            _demo.Tree.CompiledStructure.CollectionChanged += OnCompiledStructureChanged;
            _demo.Tree.PropertyChanged += OnTreePropertyChanged;
            _demo.Tree.Runs.PropertyChanged += OnRunsPropertyChanged;
            SubscribeHelper(_demo);
            InitializePermissionMode();
            SetupMcpStatusTab();
            SetupElementTab();

            _demo.Tree.GetHelper().VisibleItems.CollectionChanged += OnVisibleItemsChanged;

            workflowSurfaceControl.Session = _demo;

            _runManager.Catalog = _demo.Tree.Runs;
            _inspector.Tree = _demo.Tree;
            SyncSubAgents();

            ReloadExecutionLog();
            ReloadAgentLog();
            RefreshOutputHeader();
            UpdateWorkbenchStatus();
            RepaintShellDeferred();
        }

        private void SyncSubAgents()
        {
            var scope = (_demo?.Tree.GetHelper() as AgentHelper)?.SubAgents;
            if (ReferenceEquals(scope, _attachedSubAgents)) return;

            _attachedSubAgents = scope;
            _subAgentPanel.SetScope(scope);
        }

        private void SubscribeHelper(WorkflowDemoSession session)
        {
            if (session.Tree.GetHelper() is not AgentHelper helper) return;

            // 一个 surface 一个合并器：换演示会话会重新订阅，但合并器跟着窗体走，不该重建。
            // 这里的线程跳必须异步：BeginInvoke 只把委托排进消息泵，调用方立刻返回，
            // 排进去的刷新才有机会把中间那些请求吃掉。
            _surfaceRefresh ??= new CoalescedRefresh(
                () => WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(workflowSurfaceControl),
                action => BeginInvoke(action));

            helper.SelectionHandler = ShowSelectionDialogAsync;
            helper.ConfirmationHandler = ShowConfirmationDialogAsync;
            helper.ToolCalled += OnAgentToolCalled;
            helper.VisualRefreshRequested += OnAgentToolCalled;
        }

        private void UnsubscribeHelper(WorkflowDemoSession session)
        {
            if (session.Tree.GetHelper() is not AgentHelper helper) return;
            helper.SelectionHandler = null;
            helper.ConfirmationHandler = null;
            helper.ToolCalled -= OnAgentToolCalled;
            helper.VisualRefreshRequested -= OnAgentToolCalled;
            helper.Mcp.Status.PropertyChanged -= OnMcpStatusChanged;
        }

        /// <summary>
        /// Fills the permission-mode picker with every mode, and starts it on the one the helper was built with.
        /// </summary>
        private void InitializePermissionMode()
        {
            if (_demo?.Tree.GetHelper() is not AgentHelper helper) return;

            permissionModePicker.Items.Clear();
            permissionModePicker.Items.AddRange([.. Enum.GetValues<AgentPermissionMode>().Cast<object>()]);
            permissionModePicker.SelectedItem = helper.PermissionMode;
        }

        private void OnPermissionModeChanged(object? sender, EventArgs e)
        {
            if (permissionModePicker.SelectedItem is not AgentPermissionMode mode) return;
            if (_demo?.Tree.GetHelper() is not AgentHelper helper) return;

            helper.PermissionMode = mode;
            helper.Scope?.SetPermissionMode(mode);
        }

        // ── MCP server status ─────────────────────────────────────────────────

        private FlowLayoutPanel? _mcpStatusPanel;

        private void SetupMcpStatusTab()
        {
            if (_demo?.Tree.GetHelper() is not AgentHelper helper) return;

            if (_mcpStatusPanel is null)
            {
                _mcpStatusPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    AutoScroll = true,
                    Padding = new Padding(10),
                    BackColor = SidebarBack,
                };
                _mcpHost.Controls.Add(_mcpStatusPanel);
            }

            // Marshal status updates to the UI thread (ObservableCollection bindings require the same thread).
            helper.Mcp.WithSynchronizationContext(SynchronizationContext.Current);
            helper.Mcp.Status.PropertyChanged += OnMcpStatusChanged;

            RefreshMcpStatusPanel(helper);
            _ = helper.LoadMcpServersAsync();
        }

        private void OnMcpStatusChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_demo?.Tree.GetHelper() is not AgentHelper helper) return;
            if (InvokeRequired) { TryBeginInvoke(() => OnMcpStatusChanged(sender, e)); return; }
            RefreshMcpStatusPanel(helper);
        }

        // 模型/代理线程的回调可能落在窗体销毁之后；BeginInvoke 在已释放的控件上会抛。
        private bool TryBeginInvoke(Action action)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return false;
            try { BeginInvoke(action); return true; }
            catch (InvalidOperationException) { return false; }
        }

        private void RefreshMcpStatusPanel(AgentHelper helper)
        {
            if (_mcpStatusPanel is null) return;
            var status = helper.Mcp.Status;

            _mcpStatusPanel.Controls.Clear();
            _mcpStatusPanel.Controls.Add(new Label
            {
                Text = $"MCP: 存活 {status.ConnectedCount} · 错误 {status.ErrorCount} · 安装/连接 {status.WorkingCount}",
                AutoSize = true,
                ForeColor = CardTheme.FromHex("#6BFFB8"),
                BackColor = Color.Transparent,
                Font = new Font(Font, FontStyle.Bold),
            });

            foreach (var s in status.Servers)
            {
                var line = $"{s.Name} — {s.StateText}" + (s.ToolCount > 0 ? $" ({s.ToolCount} tools)" : string.Empty);
                _mcpStatusPanel.Controls.Add(new Label
                {
                    Text = line,
                    AutoSize = true,
                    BackColor = Color.Transparent,
                    ForeColor = s.IsError ? Color.Red
                        : s.IsConnected ? Color.FromArgb(80, 220, 130)
                        : Color.Gray,
                });
                if (s.IsError && !string.IsNullOrEmpty(s.Error))
                    _mcpStatusPanel.Controls.Add(new Label
                    {
                        Text = "    " + s.Error,
                        AutoSize = true,
                        BackColor = Color.Transparent,
                        ForeColor = Color.Red,
                    });
            }

            var reload = new Button { Text = "重载", AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            StyleSidebarActionButton(reload);
            reload.Margin = new Padding(0, 8, 0, 0);
            reload.Click += OnReloadMcp;
            _mcpStatusPanel.Controls.Add(reload);
        }

        private async void OnReloadMcp(object? sender, EventArgs e)
        {
            if (_demo?.Tree.GetHelper() is AgentHelper helper)
                await helper.LoadMcpServersAsync();
        }

        // ── 元素列表 ──────────────────────────────────────────────────────────

        // 元素面板只建一次；树每换一次（重置示例、载入文件）都重绑到新树 —— 旧树的集合事件由目录自己退订。
        private void SetupElementTab()
        {
            if (_elementPanel is null)
            {
                _elementPanel = new WorkflowElementPanel { Dock = DockStyle.Fill };
                _elementPanel.SelectedElementChanged += OnElementSelected;
                _explorerElementHost.Controls.Add(_elementPanel);
            }

            _elementPanel.Tree = _demo?.Tree;
            _elementPanel.SetSelectedElement(_demo?.Tree.SelectedElement);
        }

        private void OnElementSelected(object? sender, WorkflowElementEntry? entry)
        {
            if (_demo is null) return;
            if (ReferenceEquals(_demo.Tree.SelectedElement, entry)) return;
            _demo.Tree.SelectedElement = entry;
        }

        private void OnAgentToolCalled() => _surfaceRefresh?.Request();

        private Task ShowSelectionDialogAsync(AgentSelectionEventArgs args)
        {
            if (InvokeRequired)
            {
                if (IsDisposed || Disposing || !IsHandleCreated) return Task.CompletedTask;
                try { return (Task)Invoke(() => ShowSelectionDialogAsync(args)); }
                catch (InvalidOperationException) { return Task.CompletedTask; }
            }

            using var dlg = new AgentSelectionDialog(
                args.Prompt,
                [.. args.Options],
                args.FreeTextPrompt,
                args.AllowMultiSelect);
            dlg.ShowDialog(this);
            args.SelectedOption = dlg.ChosenOption;
            args.SelectedOptions = dlg.ChosenOptions;
            args.FreeTextResponse = dlg.FreeTextResponse;
            return Task.CompletedTask;
        }

        private Task ShowConfirmationDialogAsync(AgentConfirmationEventArgs args)
        {
            if (InvokeRequired)
            {
                if (IsDisposed || Disposing || !IsHandleCreated) return Task.CompletedTask;
                try { return (Task)Invoke(() => ShowConfirmationDialogAsync(args)); }
                catch (InvalidOperationException) { return Task.CompletedTask; }
            }

            using var dlg = new AgentConfirmationDialog(args.OperationKey, args.Description);
            dlg.ShowDialog(this);
            args.Result = dlg.Result;
            return Task.CompletedTask;
        }

        private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ControllerViewModel.IsActive))
            {
                if (InvokeRequired)
                {
                    TryBeginInvoke(UpdateWorkbenchStatus);
                    return;
                }

                UpdateWorkbenchStatus();
            }
        }

        private void OnNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (InvokeRequired) { TryBeginInvoke(UpdateWorkbenchStatus); return; }
            UpdateWorkbenchStatus();
        }

        // 可见集随缩放/平移/滚动在变，而 Nodes 不动 —— 少了这一条，状态栏那个计数会一直停在启动时的值
        private void OnVisibleItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (InvokeRequired) { TryBeginInvoke(UpdateWorkbenchStatus); return; }
            UpdateWorkbenchStatus();
        }

        private void OnCompiledStructureChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (InvokeRequired) { TryBeginInvoke(UpdateWorkbenchStatus); return; }
            UpdateWorkbenchStatus();
        }

        private void OnTreePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (InvokeRequired) { TryBeginInvoke(UpdateWorkbenchStatus); return; }

            UpdateWorkbenchStatus();

            if (e.PropertyName is nameof(TreeViewModel.SelectedElement) or nameof(TreeViewModel.HasSelectedElement))
                _elementPanel?.SetSelectedElement(_demo?.Tree.SelectedElement);
        }

        private void OnExecutionLogCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                TryBeginInvoke(ReloadExecutionLog);
                return;
            }

            ReloadExecutionLog();
        }

        private void ReloadExecutionLog()
        {
            if (_demo is null)
            {
                return;
            }

            executionLogListBox.BeginUpdate();
            executionLogListBox.Items.Clear();

            foreach (var entry in _demo.Tree.ExecutionLog)
            {
                if (entry is not null)
                    executionLogListBox.Items.Add(entry);
            }

            if (executionLogListBox.Items.Count > 0)
            {
                executionLogListBox.TopIndex = executionLogListBox.Items.Count - 1;
            }

            executionLogListBox.EndUpdate();
            RefreshOutputHeader();
        }

        private void OnRunsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (InvokeRequired) { TryBeginInvoke(RefreshOutputHeader); return; }
            RefreshOutputHeader();
            UpdateWorkbenchStatus();
        }

        private void OnOutputTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (InvokeRequired) { TryBeginInvoke(RefreshOutputHeader); return; }
            RefreshOutputHeader();
        }

        private void RefreshOutputHeader()
        {
            if (_outputRootLabel is null) return;

            var selected = _demo?.Tree.Runs.Selected;
            if (!ReferenceEquals(selected, _outputTask))
            {
                if (_outputTask is not null) _outputTask.PropertyChanged -= OnOutputTaskPropertyChanged;
                _outputTask = selected;
                if (_outputTask is not null) _outputTask.PropertyChanged += OnOutputTaskPropertyChanged;
            }

            var hasSelection = selected is not null;
            _outputRootLabel.Visible = hasSelection;
            _outputStateLabel.Visible = hasSelection;
            _outputProgressLabel.Visible = hasSelection;
            _outputHintLabel.Visible = !hasSelection;

            _outputRootLabel.Text = selected?.RootLabel ?? string.Empty;
            _outputStateLabel.Text = selected?.StateText ?? string.Empty;
            _outputProgressLabel.Text = selected?.ProgressText ?? string.Empty;
            _outputCountLabel.Text = $"已记录 {_demo?.Tree.ExecutionLog.Count ?? 0} 行";
        }

        private void UpdateWorkbenchStatus()
        {
            var tree = _demo?.Tree;
            var running = tree?.IsWorkflowRunning == true;

            _statusBar.SetState(running);
            _statusBar.Compiled.Text = $"编译 {tree?.CompiledStructure.Count ?? 0} 段";
            _statusBar.Elements.Text = $"元素 {tree?.TotalElementText ?? "0"}";
            _statusBar.VisibleCount.Text = $"可见 {tree?.VisibleElementText ?? "0"}";
            _mainHintLabel.Text = $"Ctrl+滚轮缩放 · {tree?.TotalElementText ?? "0"}";
            UpdateZoom();
        }

        private void UpdateZoom()
        {
            var scale = _demo?.Tree.Layout?.Scale.Horizontal ?? 1d;
            if (scale <= 0d) scale = 1d;
            _statusBar.Zoom.Text = $"{scale * 100d:0}%";
        }

        private void OnUiTimerTick(object? sender, EventArgs e)
        {
            UpdateZoom();
            SyncSubAgents();
        }

        private async Task ExecuteAsync(Func<Task> action, string title)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnSendToAgent(object? sender, EventArgs e)
        {
            var text = agentInputTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(text) || _demo is null) return;
            _demo.Tree.AskCommand.Execute(text);
            agentInputTextBox.Text = string.Empty;
        }

        private void OnAgentInputKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                OnSendToAgent(sender, e);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void OnAgentLogCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                TryBeginInvoke(ReloadAgentLog);
                return;
            }

            ReloadAgentLog();
        }

        private void ReloadAgentLog()
        {
            if (_demo is null) return;

            agentLogListBox.BeginUpdate();
            agentLogListBox.Items.Clear();

            foreach (var entry in _demo.Tree.AgentLog)
            {
                if (entry is not null)
                    agentLogListBox.Items.Add(entry);
            }

            if (agentLogListBox.Items.Count > 0)
            {
                agentLogListBox.TopIndex = agentLogListBox.Items.Count - 1;
            }

            agentLogListBox.EndUpdate();
        }

        /// <summary>
        /// One square button in the 48px activity bar. WinForms' flat button can outline every edge but not
        /// draw the 2px active strip on the left alone, so the face is painted here: hover lifts the ground,
        /// active adds the #007ACC strip and brightens the glyph.
        /// </summary>
        private sealed class ActivityButton : Button
        {
            private bool _hover;

            public ActivityButton()
            {
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw |
                    ControlStyles.UserPaint,
                    true);

                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                UseVisualStyleBackColor = false;
                BackColor = ActivityBack;
                ForeColor = ActivityGlyph;
                Cursor = Cursors.Hand;
                TabStop = false;
                Font = new Font("Segoe MDL2 Assets", 14F, FontStyle.Regular, GraphicsUnit.Point);
            }

            // 运行时绘制状态，不是设计器属性；显式隐藏，WFO1000 才不会要求为它生成序列化代码。
            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            internal bool IsActive { get; set; }

            protected override void OnMouseEnter(EventArgs e)
            {
                _hover = true;
                Invalidate();
                base.OnMouseEnter(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                _hover = false;
                Invalidate();
                base.OnMouseLeave(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(_hover ? ActivityHover : ActivityBack);

                if (IsActive)
                {
                    using var strip = new SolidBrush(ActivityActive);
                    e.Graphics.FillRectangle(strip, 0, 0, 2, Height);
                }

                var glyph = IsActive ? Color.White : _hover ? SidebarValue : ActivityGlyph;
                TextRenderer.DrawText(
                    e.Graphics,
                    Text,
                    Font,
                    ClientRectangle,
                    glyph,
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix);
            }
        }
    }
}
