using Demo.ViewModels;
using Demo.Workflow;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Demo
{
    public partial class Form1 : Form
    {
        private readonly BindingSource _controllerBindingSource = new();
        private WorkflowDemoSession? _demo;

        public Form1()
        {
            InitializeComponent();
            LoadDemo(WorkflowDemoSession.Create());
        }

        private ControllerViewModel Controller => _demo!.Controller;

        private async void RunWorkflow(object? sender, EventArgs e)
            => await ExecuteAsync(() => Controller.OpenWorkflowCommand.ExecuteAsync(null), "运行工作流失败");

        private async void StopWorkflow(object? sender, EventArgs e)
            => await ExecuteAsync(() => Controller.CloseWorkflowCommand.ExecuteAsync(null), "停止工作流失败");

        private async void ReloadWorkflow(object? sender, EventArgs e)
            => await ExecuteAsync(ReloadWorkflowInternalAsync, "重置示例失败");

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
            }

            _demo = session;
            _demo.Controller.PropertyChanged += OnControllerPropertyChanged;
            _demo.Tree.ExecutionLog.CollectionChanged += OnExecutionLogCollectionChanged;
            _demo.Tree.AgentLog.CollectionChanged += OnAgentLogCollectionChanged;

            _controllerBindingSource.DataSource = _demo.Controller;

            seedTextBox.DataBindings.Clear();
            seedTextBox.DataBindings.Add(nameof(TextBox.Text), _controllerBindingSource, nameof(ControllerViewModel.SeedPayload), false, DataSourceUpdateMode.OnPropertyChanged);

            workflowSurfaceControl.Session = _demo;

            ReloadExecutionLog();
            UpdateControllerState();
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
            runButton.Enabled = !isActive;
            stopButton.Enabled = isActive;
            statusValueLabel.Text = isActive ? "运行中" : "空闲";
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
