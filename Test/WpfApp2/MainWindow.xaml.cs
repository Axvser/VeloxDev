using Newtonsoft.Json;
using System.IO;
using System.Windows;
using System.Windows.Media;
using VeloxDev.Core.DynamicTheme;
using VeloxDev.Core.Interfaces.DynamicTheme;
using VeloxDev.WPF.PlatformAdapters;
using WpfApp2.ViewModels;

namespace WpfApp2
{
    public class Glass : ITheme
    {

    }

    [ThemeConfig<ObjectConverter, Dark, Light, Glass>(nameof(Background), ["#1e1e1e"], ["#00ffff"], ["#ff0000"])]
    [ThemeConfig<ObjectConverter, Dark, Light, Glass>(nameof(Foreground), ["#ffffff"], ["#1e1e1e"], ["#AAFFFFFF"])]
    [ThemeConfig<ObjectConverter, Dark, Light, Glass>(nameof(Width), ["800"], ["400"], ["1000"])]
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
            string json = File.ReadAllText(@"E:\\tree.json");
            var result = JsonConvert.DeserializeObject<FactoryViewModel>(json, settings) ?? new FactoryViewModel();
            container.DataContext = result;
            fc = result;
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            fc?.UndoCommand.Execute(null);
            ThemeManager.Transition<Glass>(new TransitionEffect() { FPS = 60, Duration = TimeSpan.FromSeconds(3) });
        }
    }
}