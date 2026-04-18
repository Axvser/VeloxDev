using Demo.ViewModels;
using Demo.Workflow;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Demo
{
    public partial class MainPage : ContentPage
    {
        private WorkflowDemoSession _demo = WorkflowDemoSession.Create();

        public MainPage()
        {
            InitializeComponent();
            BindingContext = this;
            LoadDemo(WorkflowDemoSession.Create());
        }

        public WorkflowDemoSession DemoSession => _demo;
        public ControllerViewModel Controller => _demo.Controller;
        public TreeViewModel Tree => _demo.Tree;
        public ObservableCollection<string> ExecutionLog => _demo.Tree.ExecutionLog;

        private async void RunWorkflowAsync(object? sender, EventArgs e)
            => await ExecuteAsync(() => Controller.OpenWorkflowCommand.ExecuteAsync(null), "运行工作流失败");

        private async void StopWorkflowAsync(object? sender, EventArgs e)
            => await ExecuteAsync(() => Controller.CloseWorkflowCommand.ExecuteAsync(null), "停止工作流失败");

        private async void ReloadDemoAsync(object? sender, EventArgs e)
            => await ExecuteAsync(ReloadDemoInternalAsync, "重置示例失败");

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _ = _demo.Tree.GetHelper().CloseAsync();
        }

        private async Task ReloadDemoInternalAsync()
        {
            await _demo.Tree.GetHelper().CloseAsync();
            LoadDemo(WorkflowDemoSession.Create());
        }

        private void LoadDemo(WorkflowDemoSession session)
        {
            if (_demo is not null)
                _demo.Tree.AgentLog.CollectionChanged -= OnAgentLogChanged;
            _demo = session;
            _demo.Tree.AgentLog.CollectionChanged += OnAgentLogChanged;
            OnPropertyChanged(nameof(DemoSession));
            OnPropertyChanged(nameof(Controller));
            OnPropertyChanged(nameof(Tree));
            OnPropertyChanged(nameof(ExecutionLog));
        }

        private async void OnAgentLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            await Task.Yield();
            if (_demo.Tree.AgentLog is { Count: > 0 } log)
                AgentLogScroller.ScrollTo(log.Count - 1, position: ScrollToPosition.End, animate: false);
        }

        private void OnSendToAgent(object? sender, EventArgs e)
        {
            var text = AgentInput?.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            _demo.Tree.AskCommand.Execute(text);
            AgentInput!.Text = string.Empty;
        }

        private void OnAgentInputCompleted(object? sender, EventArgs e)
        {
            OnSendToAgent(sender, e);
        }

        private async Task ExecuteAsync(Func<Task> action, string title)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync(title, ex.Message, "OK");
            }
        }
    }
}
