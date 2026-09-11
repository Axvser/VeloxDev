namespace Demo
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            // 窗口显式给尺寸（设备无关单位），而不是让平台取默认值：默认窗口按工作区的一个比例开，
            // 比这一页的内容矮 —— 实测下那排按钮会被压成零高，采样器把手条会被挤到窗口外面，而"排在窗口外的
            // 控件"正是验收侧判定"没人够得着"的那条。演示台（SamplerBench）又在这页里实打实占了一行，
            // 所以尺寸在这里定死，与 WPF 那侧把窗口开成 1000×800 是同一件事。
            return new Window(new AppShell()) { Width = 1300, Height = 900 };
        }
    }
}