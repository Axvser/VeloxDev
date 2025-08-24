using Newtonsoft.Json;
using System.IO;
using System.Windows;
using VeloxDev.Core.DynamicTheme;
using VeloxDev.Core.Interfaces.DynamicTheme;
using VeloxDev.Core.TransitionSystem;
using VeloxDev.WPF.PlatformAdapters;
using WpfApp2.ViewModels;

namespace WpfApp2
{
    public class Glass : ITheme
    {

    }

    [ThemeConfig<BrushConverter, Dark, Light, Glass>(nameof(Background), ["#1e1e1e"], ["#00ffff"], ["msrc"])]
    [ThemeConfig<ObjectConverter, Dark, Light, Glass>(nameof(Foreground), ["#ffffff"], ["#1e1e1e"], ["#AAFFFFFF"])]
    public partial class MainWindow : Window
    {
        private readonly JsonSerializerSettings settings = new()
        {
            TypeNameHandling = TypeNameHandling.Auto, // 允许接口与抽象类
            NullValueHandling = NullValueHandling.Include, // 包含空值
            Formatting = Formatting.Indented, // 格式对齐
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore, // 忽略循环引用
            PreserveReferencesHandling = PreserveReferencesHandling.Objects, // 保留对象引用
        };

        private readonly FactoryViewModel? fc;
        public MainWindow()
        {
            InitializeComponent();
            InitializeTheme();
            ThemeManager.SetPlatformInterpolator(new Interpolator());
            ThemeManager.StartModel = StartModel.Reflect;
            string json = File.ReadAllText(@"E:\\tree.json");
            var result = JsonConvert.DeserializeObject<FactoryViewModel>(json, settings) ?? new FactoryViewModel();
            container.DataContext = result;
            fc = result;
        }

        private void Window_KeyDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            fc?.UndoCommand.Execute(null);
            if (ThemeManager.Current == typeof(Dark))
                ThemeManager.Transition<Glass>(new TransitionEffect() { FPS = 120, Duration = TimeSpan.FromSeconds(0.6) });
            else if (ThemeManager.Current == typeof(Glass))
                ThemeManager.Transition<Light>(new TransitionEffect() { FPS = 60, Duration = TimeSpan.FromSeconds(0.6) });
            else if (ThemeManager.Current == typeof(Light))
                ThemeManager.Transition<Dark>(new TransitionEffect() { FPS = 15, Duration = TimeSpan.FromSeconds(0.6) });
        }

        private void Window_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Transition<MainWindow>.Create()
                .Property(x => x.Background,System.Windows.Media.Brushes.Red)
                .Effect(new TransitionEffect() { FPS = 20, Duration = TimeSpan.FromSeconds(0.6) })
                .Execute(this);
        }
    }
}