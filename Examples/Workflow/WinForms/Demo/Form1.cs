using Demo.ViewModels;
using Demo.Workflow;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.AI.Safety;
using VeloxDev.MVVM;
using VeloxDev.Serialization;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo
{
    public partial class Form1 : Form
    {
        private readonly BindingSource _controllerBindingSource = [];
        private WorkflowDemoSession? _demo;

        // Agent 一轮里每个工具调用都要求刷新，而一次刷新是全量的（重新定位命名控件、跑布局、重算可见区）。
        // 合并之后一轮只刷新一次 —— 刷新本身是幂等的，后来的请求要的正是前一次即将看到的状态。
        private ViewModels.Workflow.Helper.CoalescedRefresh? _surfaceRefresh;

        // ── VS Code 式左栏 ───────────────────────────────────────────────────
        // 活动栏 48px + 侧栏；五个活动页一次只显示一个。命名控件仍由 Designer 创建，
        // BuildSidebar 只把它们从原来的 toolbarPanel / 标签页里搬到对应页，事件与绑定原样保留。
        private static readonly Color ActivityBack = Color.FromArgb(0x33, 0x33, 0x33);
        private static readonly Color ActivityHover = Color.FromArgb(0x2A, 0x2D, 0x2E);
        private static readonly Color ActivityActive = Color.FromArgb(0x00, 0x7A, 0xCC);
        private static readonly Color ActivityGlyph = Color.FromArgb(0x9D, 0x9D, 0x9D);
        private static readonly Color SidebarBack = Views.CardTheme.FromHex("#252526");
        private static readonly Color SidebarField = Views.CardTheme.FromHex("#1E1E1E");
        private static readonly Color SidebarText = Views.CardTheme.FromHex("#CCCCCC");
        private static readonly Color SidebarValue = Views.CardTheme.FromHex("#E7E7E7");
        private static readonly Color SidebarMuted = Views.CardTheme.FromHex("#94A3B8");
        private static readonly Color SidebarAccent = Views.CardTheme.FromHex("#7EC8FF");
        private static readonly Color SidebarHover = Views.CardTheme.FromHex("#3A3D41");
        private static readonly Color SidebarButtonBack = Views.CardTheme.FromHex("#333333");
        private static readonly Color SidebarBorder = Views.CardTheme.FromHex("#3C3C3C");

        private readonly Dictionary<string, Control> _sidebarPages = [];
        private readonly Dictionary<string, ActivityButton> _activityButtons = [];
        private Label _sidebarHeaderLabel = null!;
        private Panel _sidebarPageHost = null!;
        private Panel _explorerElementHost = null!;
        private Panel _mcpHost = null!;
        private ToolTip _toolTip = null!;

        public Form1()
        {
            InitializeComponent();
            BuildSidebar();
            WorkflowBehaviors.WorkflowSurfaceBehavior.SetIsEnabled(workflowSurfaceControl, true);
            WorkflowBehaviors.WorkflowSurfaceBehavior.SetCanvas(workflowSurfaceControl, workflowSurfaceControl);
            workflowSurfaceControl.MinimapOverlay = minimapOverlay;
            LoadDemo(WorkflowDemoSession.Create());
        }

        /// <summary>
        /// Replaces the designer's stacked toolbar + log tabs with a VS Code-style rail:
        /// a 48px activity bar, a dark sidebar, and five pages that are shown one at a time.
        /// </summary>
        /// <remarks>
        /// The named controls are not rebuilt — they are moved out of <see cref="toolbarPanel"/> and the
        /// log tabs into their new page. WinForms moves a control, its bindings and its event handlers
        /// together when it is added to a new parent, so this preserves every existing handler and
        /// the seed binding that <see cref="LoadDemo"/> installs.
        /// </remarks>
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
            splitContainer.Panel1.BackColor = Views.CardTheme.Ground;

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
            sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _sidebarHeaderLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = "资源管理器",
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = SidebarText,
                BackColor = SidebarBack,
                Font = new Font(Views.CardTheme.UiFamily, 9F, FontStyle.Bold),
                Padding = new Padding(12, 0, 12, 0),
                Margin = Padding.Empty,
            };
            _sidebarPageHost = new Panel { Dock = DockStyle.Fill, BackColor = SidebarBack };

            sidebarLayout.Controls.Add(_sidebarHeaderLabel, 0, 0);
            sidebarLayout.Controls.Add(_sidebarPageHost, 0, 1);

            BuildExplorerPage();
            BuildRunPage();
            BuildAgentPage();
            BuildMcpPage();
            BuildOutputPage();

            var activityBar = new Panel { Dock = DockStyle.Fill, BackColor = ActivityBack };
            var activityStack = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 5,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = ActivityBack,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            activityStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (var i = 0; i < 5; i++) activityStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));

            AddActivityButton(activityStack, 0, "explorer", "", "资源管理器");
            AddActivityButton(activityStack, 1, "run", "", "运行");
            AddActivityButton(activityStack, 2, "agent", "", "Agent 对话");
            AddActivityButton(activityStack, 3, "mcp", "", "MCP 服务器");
            AddActivityButton(activityStack, 4, "output", "", "执行输出");
            activityBar.Controls.Add(activityStack);

            var rail = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Views.CardTheme.Ground,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            rail.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48F));
            rail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            rail.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            rail.Controls.Add(activityBar, 0, 0);
            rail.Controls.Add(sidebarLayout, 1, 0);

            splitContainer.Panel1.Controls.Add(rail);
            splitContainer.Panel1.ResumeLayout(false);

            // Both old hosts are empty by now: every named child was re-parented above.
            toolbarPanel.Dispose();
            logTabControl.Dispose();

            SelectActivity("explorer");
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

        /// <summary>Shows one page at a time and moves the activity bar's active marker and the header.</summary>
        private void SelectActivity(string key)
        {
            foreach (var (name, page) in _sidebarPages)
                page.Visible = name == key;

            if (_sidebarPages.TryGetValue(key, out var active)) active.BringToFront();

            _sidebarHeaderLabel.Text = key switch
            {
                "run" => "运行",
                "agent" => "AGENT",
                "mcp" => "MCP",
                "output" => "输出",
                _ => "资源管理器",
            };

            foreach (var (name, button) in _activityButtons)
            {
                button.IsActive = name == key;
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
                RowCount = 5,
                BackColor = SidebarBack,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            titleLabel.AutoSize = true;
            titleLabel.ForeColor = SidebarValue;
            titleLabel.BackColor = Color.Transparent;
            titleLabel.Margin = new Padding(12, 12, 12, 0);
            layout.Controls.Add(titleLabel, 0, 0);

            var actions = CreateButtonFlow();
            actions.Padding = new Padding(10, 2, 10, 0);
            foreach (var button in new[] { saveButton, selectButton, undoButton, redoButton, reloadButton, loadDemoButton })
            {
                StyleSidebarActionButton(button);
                actions.Controls.Add(button);
            }
            layout.Controls.Add(actions, 0, 1);

            layout.Controls.Add(CreateSectionLabel("元素"), 0, 2);

            _explorerElementHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SidebarBack,
                Margin = new Padding(12, 0, 12, 0),
            };
            layout.Controls.Add(_explorerElementHost, 0, 3);

            summaryLabel.ForeColor = SidebarMuted;
            summaryLabel.BackColor = Color.Transparent;
            summaryLabel.Visible = false;
            layout.Controls.Add(summaryLabel, 0, 4);

            page.Controls.Add(layout);
        }

        private void BuildRunPage()
        {
            var page = CreateSidebarPage("run");
            var scroll = new Panel { Dock = DockStyle.Fill, BackColor = SidebarBack, AutoScroll = true };
            var stack = CreateVerticalStack();

            AddStackRow(stack, CreateSectionLabel("运行控制"));

            StyleCaptionLabel(runControlLabel);
            StyleValueLabel(runGateStateLabel);
            StyleCaptionLabel(statusLabel);
            StyleValueLabel(statusValueLabel);
            AddStackRow(stack, CreatePairRow(runControlLabel, runGateStateLabel));
            AddStackRow(stack, CreatePairRow(statusLabel, statusValueLabel));

            var runButtons = CreateButtonFlow();
            foreach (var button in new[] { stopButton, pauseButton, resumeButton, continueFromCheckpointButton })
            {
                StyleSidebarControlButton(button);
                runButtons.Controls.Add(button);
            }
            stopButton.ForeColor = Views.CardTheme.ActionStop;
            AddStackRow(stack, runButtons);

            AddStackRow(stack, CreateSectionLabel("画布"));
            StyleCaptionLabel(nodeCountCaptionLabel);
            StyleValueLabel(nodeCountLabel);
            StyleCaptionLabel(visibleCountCaptionLabel);
            StyleValueLabel(visibleCountLabel);
            AddStackRow(stack, CreatePairRow(nodeCountCaptionLabel, nodeCountLabel));
            AddStackRow(stack, CreatePairRow(visibleCountCaptionLabel, visibleCountLabel));

            AddStackRow(stack, CreateSectionLabel("本次运行"));
            StyleCaptionLabel(seedLabel);
            seedTextBox.BackColor = SidebarField;
            seedTextBox.ForeColor = SidebarValue;
            seedTextBox.BorderStyle = BorderStyle.FixedSingle;
            AddStackRow(stack, CreatePairRow(seedLabel, seedTextBox, stretchValue: true));

            scroll.Controls.Add(stack);
            page.Controls.Add(scroll);
        }

        private void BuildAgentPage()
        {
            var page = CreateSidebarPage("agent");
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = SidebarBack,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // 会话权限与 Agent 放一起：模式决定模型能做什么，不决定一次运行怎么被驱动。
            var session = CreateVerticalStack();
            AddStackRow(session, CreateSectionLabel("会话"));
            StyleCaptionLabel(permissionModeLabel);
            permissionModePicker.BackColor = SidebarField;
            permissionModePicker.ForeColor = SidebarValue;
            permissionModePicker.FlatStyle = FlatStyle.Flat;
            AddStackRow(session, CreatePairRow(permissionModeLabel, permissionModePicker, stretchValue: true));
            layout.Controls.Add(session, 0, 0);

            var chatHeader = new Label
            {
                Text = "Agent 对话",
                AutoSize = true,
                ForeColor = SidebarAccent,
                BackColor = Color.Transparent,
                Font = new Font(Views.CardTheme.UiFamily, 9F, FontStyle.Bold),
                Margin = new Padding(12, 6, 12, 4),
            };
            layout.Controls.Add(chatHeader, 0, 1);

            var chat = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = SidebarBack,
                Margin = Padding.Empty,
                Padding = new Padding(8, 0, 8, 8),
            };
            chat.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            chat.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            chat.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));

            agentLogListBox.BackColor = SidebarField;
            agentLogListBox.ForeColor = SidebarValue;
            agentLogListBox.BorderStyle = BorderStyle.FixedSingle;
            agentLogListBox.Dock = DockStyle.Fill;

            agentInputPanel.BackColor = SidebarBack;
            agentInputPanel.Dock = DockStyle.Fill;
            agentInputTextBox.BackColor = SidebarField;
            agentInputTextBox.ForeColor = Color.White;
            agentInputTextBox.BorderStyle = BorderStyle.FixedSingle;

            StyleSidebarControlButton(agentSendButton);
            agentSendButton.AutoSize = false;
            agentSendButton.Width = 70;

            chat.Controls.Add(agentLogListBox, 0, 0);
            chat.Controls.Add(agentInputPanel, 0, 1);
            layout.Controls.Add(chat, 0, 2);

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

        private void BuildOutputPage()
        {
            var page = CreateSidebarPage("output");
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
            layout.Controls.Add(CreateSectionLabel("执行输出"), 0, 0);

            executionLogListBox.BackColor = SidebarField;
            executionLogListBox.ForeColor = SidebarValue;
            executionLogListBox.BorderStyle = BorderStyle.FixedSingle;
            executionLogListBox.Dock = DockStyle.Fill;
            executionLogListBox.Margin = new Padding(8, 0, 8, 8);
            layout.Controls.Add(executionLogListBox, 0, 1);
            page.Controls.Add(layout);
        }

        // ── Sidebar building blocks ─────────────────────────────────────────

        private static TableLayoutPanel CreateVerticalStack()
        {
            var stack = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 0,
                BackColor = SidebarBack,
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

        private static Label CreateSectionLabel(string text) => new()
        {
            Text = text,
            AutoSize = true,
            Dock = DockStyle.Top,
            ForeColor = Views.CardTheme.FromHex("#BBBBBB"),
            BackColor = Color.Transparent,
            Font = new Font(Views.CardTheme.UiFamily, 9F, FontStyle.Bold),
            Margin = new Padding(12, 14, 12, 6),
        };

        private static TableLayoutPanel CreatePairRow(Control caption, Control value, bool stretchValue = false)
        {
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
                BackColor = SidebarBack,
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
        }

        private static void StyleSidebarControlButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = SidebarBorder;
            button.FlatAppearance.MouseOverBackColor = SidebarHover;
            button.FlatAppearance.MouseDownBackColor = SidebarField;
            button.BackColor = SidebarButtonBack;
            button.ForeColor = SidebarText;
            button.UseVisualStyleBackColor = false;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Padding = new Padding(8, 3, 8, 3);
            button.Margin = new Padding(0, 0, 6, 6);
            button.Cursor = Cursors.Hand;
        }

        private static void StyleCaptionLabel(Label label)
        {
            label.ForeColor = SidebarText;
            label.BackColor = Color.Transparent;
        }

        private static void StyleValueLabel(Label label)
        {
            label.ForeColor = SidebarValue;
            label.BackColor = Color.Transparent;
        }

        private ControllerViewModel Controller => _demo!.Controller;

        private async void StopWorkflow(object? sender, EventArgs e)
            => await ExecuteAsync(() => Controller.CloseWorkflowCommand.ExecuteAsync(null), "Failed to stop workflow");

        private async void ReloadWorkflow(object? sender, EventArgs e)
            => await ExecuteAsync(ReloadWorkflowInternalAsync, "Failed to reset demo");

        private void UndoWorkflow(object? sender, EventArgs e)
            => _demo?.Tree.UndoCommand.Execute(null);

        private void RedoWorkflow(object? sender, EventArgs e)
            => _demo?.Tree.RedoCommand.Execute(null);

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
                    var result = json.Deserialize<TreeViewModel>();
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

        private async void LoadNetworkDemo(object? sender, EventArgs e)
            => await ExecuteAsync(ReloadWorkflowInternalAsync, "Failed to load demo");

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
                _demo.Controller.RunCommand.Exited -= OnRunCommandExited;
                _demo.Controller.ResumeCommand.Exited -= OnRunCommandExited;
                _demo.Tree.ExecutionLog.CollectionChanged -= OnExecutionLogCollectionChanged;
                _demo.Tree.AgentLog.CollectionChanged -= OnAgentLogCollectionChanged;
                _demo.Tree.Nodes.CollectionChanged -= OnNodesCollectionChanged;
                _demo.Tree.GetHelper().VisibleItems.CollectionChanged -= OnVisibleItemsChanged;
                UnsubscribeHelper(_demo);
            }

            _demo = session;
            _demo.Controller.PropertyChanged += OnControllerPropertyChanged;
            _demo.Controller.RunCommand.Exited += OnRunCommandExited;
            _demo.Controller.ResumeCommand.Exited += OnRunCommandExited;
            _demo.Tree.ExecutionLog.CollectionChanged += OnExecutionLogCollectionChanged;
            _demo.Tree.AgentLog.CollectionChanged += OnAgentLogCollectionChanged;
            _demo.Tree.Nodes.CollectionChanged += OnNodesCollectionChanged;
            SubscribeHelper(_demo);
            InitializePermissionMode();
            SetupMcpStatusTab();
            SetupElementTab();

            _demo.Tree.GetHelper().VisibleItems.CollectionChanged += OnVisibleItemsChanged;

            _controllerBindingSource.DataSource = _demo.Controller;

            seedTextBox.DataBindings.Clear();
            seedTextBox.DataBindings.Add(nameof(TextBox.Text), _controllerBindingSource, nameof(ControllerViewModel.SeedPayload), false, DataSourceUpdateMode.OnPropertyChanged);

            workflowSurfaceControl.Session = _demo;

            ReloadExecutionLog();
            UpdateControllerState();
            RefreshRunControls();
        }

        private void SubscribeHelper(WorkflowDemoSession session)
        {
            if (session.Tree.GetHelper() is not ViewModels.Workflow.Helper.AgentHelper helper) return;

            // 一个 surface 一个合并器：换演示会话会重新订阅，但合并器跟着窗体走，不该重建。
            // 这里的线程跳必须异步：BeginInvoke 只把委托排进消息泵，调用方立刻返回，
            // 排进去的刷新才有机会把中间那些请求吃掉。
            _surfaceRefresh ??= new ViewModels.Workflow.Helper.CoalescedRefresh(
                () => WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(workflowSurfaceControl),
                action => BeginInvoke(action));

            helper.SelectionHandler = ShowSelectionDialogAsync;
            helper.ConfirmationHandler = ShowConfirmationDialogAsync;
            helper.ToolCalled += OnAgentToolCalled;
            helper.VisualRefreshRequested += OnAgentToolCalled;
        }

        private void UnsubscribeHelper(WorkflowDemoSession session)
        {
            if (session.Tree.GetHelper() is not ViewModels.Workflow.Helper.AgentHelper helper) return;
            helper.SelectionHandler = null;
            helper.ConfirmationHandler = null;
            helper.ToolCalled -= OnAgentToolCalled;
            helper.VisualRefreshRequested -= OnAgentToolCalled;
            helper.Mcp.Status.PropertyChanged -= OnMcpStatusChanged;
        }

        /// <summary>
        /// Fills the permission-mode picker with every mode, and starts it on the one the helper was built with.
        /// </summary>
        /// <remarks>
        /// Selecting the current mode fires the change handler, which sets the same mode again —
        /// <c>SetPermissionMode</c> reports that nothing moved and does nothing. Cheaper than arranging not to.
        /// <para>
        /// Re-run on every tree swap rather than once at construction: a tree loaded from a file arrives with its
        /// own helper at that helper's default mode, and a picker still showing the previous one would be a lie
        /// about the session on screen.
        /// </para>
        /// </remarks>
        private void InitializePermissionMode()
        {
            if (_demo?.Tree.GetHelper() is not ViewModels.Workflow.Helper.AgentHelper helper) return;

            permissionModePicker.Items.Clear();
            permissionModePicker.Items.AddRange([.. Enum.GetValues<AgentPermissionMode>().Cast<object>()]);
            permissionModePicker.SelectedItem = helper.PermissionMode;
        }

        /// <summary>
        /// Moves the session to the mode the user picked. There is nothing to rebuild: the gate reads the policy
        /// per call, so the very next tool call obeys the new mode, and the prompt is re-rendered next turn.
        /// </summary>
        private void OnPermissionModeChanged(object? sender, EventArgs e)
        {
            if (permissionModePicker.SelectedItem is not AgentPermissionMode mode) return;
            if (_demo?.Tree.GetHelper() is not ViewModels.Workflow.Helper.AgentHelper helper) return;

            helper.PermissionMode = mode;
            helper.Scope?.SetPermissionMode(mode);
        }

        // ── MCP server status ─────────────────────────────────────────────────

        private FlowLayoutPanel? _mcpStatusPanel;

        private void SetupMcpStatusTab()
        {
            if (_demo?.Tree.GetHelper() is not ViewModels.Workflow.Helper.AgentHelper helper) return;

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
            if (_demo?.Tree.GetHelper() is not ViewModels.Workflow.Helper.AgentHelper helper) return;
            if (InvokeRequired) { BeginInvoke(() => OnMcpStatusChanged(sender, e)); return; }
            RefreshMcpStatusPanel(helper);
        }

        private void RefreshMcpStatusPanel(ViewModels.Workflow.Helper.AgentHelper helper)
        {
            if (_mcpStatusPanel is null) return;
            var status = helper.Mcp.Status;

            _mcpStatusPanel.Controls.Clear();
            _mcpStatusPanel.Controls.Add(new Label
            {
                Text = $"MCP: 存活 {status.ConnectedCount} · 错误 {status.ErrorCount} · 安装/连接 {status.WorkingCount}",
                AutoSize = true,
                ForeColor = Views.CardTheme.FromHex("#6BFFB8"),
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
            StyleSidebarControlButton(reload);
            reload.AutoSize = true;
            reload.Margin = new Padding(0, 8, 0, 0);
            reload.Click += OnReloadMcp;
            _mcpStatusPanel.Controls.Add(reload);
        }

        private async void OnReloadMcp(object? sender, EventArgs e)
        {
            if (_demo?.Tree.GetHelper() is ViewModels.Workflow.Helper.AgentHelper helper)
                await helper.LoadMcpServersAsync();
        }

        // ── 元素列表 ──────────────────────────────────────────────────────────

        private Views.Workflow.WorkflowElementPanel? _elementPanel;

        // 元素面板只建一次；树每换一次（重置示例、载入文件）都重绑到新树 —— 旧树的集合事件由目录自己退订。
        private void SetupElementTab()
        {
            if (_elementPanel is null)
            {
                _elementPanel = new Views.Workflow.WorkflowElementPanel { Dock = DockStyle.Fill };
                _explorerElementHost.Controls.Add(_elementPanel);
            }

            _elementPanel.Tree = _demo?.Tree;
        }

        private void OnAgentToolCalled() => _surfaceRefresh?.Request();

        private Task ShowSelectionDialogAsync(AgentSelectionEventArgs args)
        {
            if (InvokeRequired)
                return (Task)Invoke(() => ShowSelectionDialogAsync(args));

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
                return (Task)Invoke(() => ShowConfirmationDialogAsync(args));

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
                    BeginInvoke(UpdateControllerState);
                    return;
                }

                UpdateControllerState();
            }
        }

        private void OnNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (InvokeRequired) { BeginInvoke(UpdateControllerState); return; }
            UpdateControllerState();
        }

        // 可见集随缩放/平移/滚动在变，而 Nodes 不动 —— 少了这一条，工具栏那个计数会一直停在启动时的值
        private void OnVisibleItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (InvokeRequired) { BeginInvoke(UpdateControllerState); return; }
            UpdateControllerState();
        }

        private void OnExecutionLogCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(ReloadExecutionLog);
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
        }

        private void UpdateControllerState()
        {
            var isActive = _demo?.Controller.IsActive == true;
            stopButton.Enabled = isActive;
            statusValueLabel.Text = isActive ? "运行中" : "空闲";
            nodeCountLabel.Text = _demo?.Tree.TotalElementText ?? "0";
            visibleCountLabel.Text = _demo?.Tree.VisibleElementText ?? "0";
        }

        // 门与检查点都在会话上、不在树上，所以这两件事只有拿得到会话时才可按；换过树（载入文件）就什么都别做
        private void RefreshRunControls()
        {
            continueFromCheckpointButton.Enabled = _demo?.HasCheckpoint == true;
            runGateStateLabel.Text = _demo?.Gate.IsPaused == true ? "已暂停" : "空闲";
        }

        // 一轮跑完，检查点这一轮才写得下来 —— 按钮可不可按跟着它走。Exited 是在线程池上发的，所以回到 UI 线程再改控件
        private void OnRunCommandExited(CommandEventArgs e)
        {
            if (InvokeRequired) { BeginInvoke(RefreshRunControls); return; }
            RefreshRunControls();
        }

        private void PauseWorkflow(object? sender, EventArgs e)
        {
            if (_demo is null) return;
            _demo.Gate.Pause();
            runGateStateLabel.Text = "已暂停：停在下一个节点边界";
        }

        private void ResumeWorkflow(object? sender, EventArgs e)
        {
            if (_demo is null) return;
            _demo.Gate.Resume();
            runGateStateLabel.Text = "运行中";
        }

        private async void ContinueFromCheckpoint(object? sender, EventArgs e)
        {
            if (_demo is null) return;
            runGateStateLabel.Text = "从检查点继续…";
            await _demo.Controller.ResumeCommand.ExecuteAsync(null);
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
                BeginInvoke(ReloadAgentLog);
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
            [System.ComponentModel.Browsable(false)]
            [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
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
