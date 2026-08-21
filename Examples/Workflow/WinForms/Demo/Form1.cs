using Demo.ViewModels;
using Demo.Workflow;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.MVVM.Serialization;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo
{
    public partial class Form1 : Form
    {
        private readonly BindingSource _controllerBindingSource = [];
        private WorkflowDemoSession? _demo;

        public Form1()
        {
            InitializeComponent();
            WorkflowBehaviors.WorkflowSurfaceBehavior.SetIsEnabled(workflowSurfaceControl, true);
            WorkflowBehaviors.WorkflowSurfaceBehavior.SetCanvasName(workflowSurfaceControl, nameof(workflowSurfaceControl));
            WorkflowBehaviors.WorkflowSurfaceBehavior.SetPointerPressSourceName(workflowSurfaceControl, nameof(workflowSurfaceControl));
            workflowSurfaceControl.MinimapOverlay = minimapOverlay;
            LoadDemo(WorkflowDemoSession.Create());
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
                _demo.Tree.ExecutionLog.CollectionChanged -= OnExecutionLogCollectionChanged;
                _demo.Tree.AgentLog.CollectionChanged -= OnAgentLogCollectionChanged;
                _demo.Tree.Nodes.CollectionChanged -= OnNodesCollectionChanged;
                UnsubscribeHelper(_demo);
            }

            _demo = session;
            _demo.Controller.PropertyChanged += OnControllerPropertyChanged;
            _demo.Tree.ExecutionLog.CollectionChanged += OnExecutionLogCollectionChanged;
            _demo.Tree.AgentLog.CollectionChanged += OnAgentLogCollectionChanged;
            _demo.Tree.Nodes.CollectionChanged += OnNodesCollectionChanged;
            SubscribeHelper(_demo);
            SetupMcpStatusTab();

            _controllerBindingSource.DataSource = _demo.Controller;

            seedTextBox.DataBindings.Clear();
            seedTextBox.DataBindings.Add(nameof(TextBox.Text), _controllerBindingSource, nameof(ControllerViewModel.SeedPayload), false, DataSourceUpdateMode.OnPropertyChanged);

            workflowSurfaceControl.Session = _demo;

            ReloadExecutionLog();
            UpdateControllerState();
        }

        private void SubscribeHelper(WorkflowDemoSession session)
        {
            if (session.Tree.GetHelper() is not ViewModels.Workflow.Helper.AgentHelper helper) return;
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

        // ── MCP server status ─────────────────────────────────────────────────

        private TabPage? _mcpStatusTab;
        private FlowLayoutPanel? _mcpStatusPanel;

        private void SetupMcpStatusTab()
        {
            if (_demo?.Tree.GetHelper() is not ViewModels.Workflow.Helper.AgentHelper helper) return;

            if (_mcpStatusTab is null)
            {
                _mcpStatusTab = new TabPage("MCP 服务器状态");
                _mcpStatusPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    AutoScroll = true,
                    Padding = new Padding(10),
                };
                _mcpStatusTab.Controls.Add(_mcpStatusPanel);
                logTabControl.TabPages.Add(_mcpStatusTab);
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
                Font = new Font(Font, FontStyle.Bold),
            });

            foreach (var s in status.Servers)
            {
                var line = $"{s.Name} — {s.StateText}" + (s.ToolCount > 0 ? $" ({s.ToolCount} tools)" : string.Empty);
                _mcpStatusPanel.Controls.Add(new Label
                {
                    Text = line,
                    AutoSize = true,
                    ForeColor = s.IsError ? Color.Red
                        : s.IsConnected ? Color.FromArgb(80, 220, 130)
                        : Color.Gray,
                });
                if (s.IsError && !string.IsNullOrEmpty(s.Error))
                    _mcpStatusPanel.Controls.Add(new Label
                    {
                        Text = "    " + s.Error,
                        AutoSize = true,
                        ForeColor = Color.Red,
                    });
            }

            var reload = new Button { Text = "重载", AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            reload.Click += OnReloadMcp;
            _mcpStatusPanel.Controls.Add(reload);
        }

        private async void OnReloadMcp(object? sender, EventArgs e)
        {
            if (_demo?.Tree.GetHelper() is ViewModels.Workflow.Helper.AgentHelper helper)
                await helper.LoadMcpServersAsync();
        }

        private void OnAgentToolCalled()
        {
            if (InvokeRequired) { BeginInvoke(OnAgentToolCalled); return; }
            WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(workflowSurfaceControl);
        }

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
            nodeCountLabel.Text = (_demo?.Tree.Nodes.Count ?? 0).ToString();
            visibleCountLabel.Text = (_demo?.Tree.Helper?.VisibleItems?.Count ?? 0).ToString();
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
                agentLogListBox.Items.Add(entry);
            }

            if (agentLogListBox.Items.Count > 0)
            {
                agentLogListBox.TopIndex = agentLogListBox.Items.Count - 1;
            }

            agentLogListBox.EndUpdate();
        }
    }
}
