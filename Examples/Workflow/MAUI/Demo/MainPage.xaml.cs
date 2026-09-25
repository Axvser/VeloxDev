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

            // 显式挂一次，不等 Session="{Binding DemoSession}" 那条绑定。
            // InitializeComponent 建视图时 BindingContext 还是 null，绑定在那时求值成 null；
            // 之后把 BindingContext 设成本页也没有把它重新拉起来 —— 结果是启动进去画布空着、
            // 节点总数为 0，必须点一次「Load Workflow Demo」（那条路径走的是代码赋值）才出图。
            // DemoSession 没有变更通知，绑定本来也不会有第二次机会，所以这里直接给视图。
            WorkflowSurface.Session = _demo;
        }

        public WorkflowDemoSession DemoSession => _demo;

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _ = _demo.Tree.GetHelper().CloseAsync();
        }
    }
}
