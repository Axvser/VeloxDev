using Newtonsoft.Json;
using System.IO;
using System.Windows;
using VeloxDev.Core.DynamicTheme;
using VeloxDev.Core.TransitionSystem;
using VeloxDev.WPF.PlatformAdapters;
using WpfApp2.ViewModels;

namespace WpfApp2
{
    [ThemeConfig<ObjectConverter, Dark, Light>(nameof(Background), ["#1e1e1e"], ["#00ffff"])]
    [ThemeConfig<ObjectConverter, Dark, Light>(nameof(Foreground), ["#ffffff"], ["#1e1e1e"])]
    [ThemeConfig<ObjectConverter, Dark, Light>(nameof(Width), ["800"], ["400"])]
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
            ThemeManager.Transition<Light>(new TransitionEffect() 
            { 
                FPS = 120, 
                Duration = TimeSpan.FromSeconds(3),
                EaseCalculator = Eases.Circ.InOut }
            );
        }
    }
}