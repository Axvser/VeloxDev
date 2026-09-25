namespace Demo
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            // 这个 demo 的画布、侧栏与整套卡片都是写死的暗色（画布 #0B1120、卡面 #161B22），
            // 但 MAUI 的原生控件（Entry / Picker / Editor）跟随系统主题：浅色系统下它们会在暗卡面里
            // 露出浅色底与浅色边框。Avalonia 版没有这个问题 —— 那里 FluentTheme 的 TextBox 可以直接
            // 覆盖 Background/BorderBrush；MAUI 的 Entry 没有边框与圆角属性，只能连同原生主题一起定死。
            UserAppTheme = AppTheme.Dark;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }
    }
}