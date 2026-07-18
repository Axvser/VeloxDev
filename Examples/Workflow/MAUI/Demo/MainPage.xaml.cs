using Demo.ViewModels;
using Demo.Workflow;

namespace Demo
{
    public partial class MainPage : ContentPage
    {
        private WorkflowDemoSession _demo = WorkflowDemoSession.Create();

        public MainPage()
        {
            InitializeComponent();
            BindingContext = this;
        }

        public WorkflowDemoSession DemoSession => _demo;

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _ = _demo.Tree.GetHelper().CloseAsync();
        }
    }
}
