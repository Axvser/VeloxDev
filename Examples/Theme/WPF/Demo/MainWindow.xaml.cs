using System.Windows;
using VeloxDev.Core.DynamicTheme;
using VeloxDev.WPF.PlatformAdapters;

namespace Demo
{
    /* 我们建议您将主题相关的操作单独定义在一个分部中，这样，当您处理交互逻辑时，不会受到无关代码的打扰 */
    /* 注意：当您使用Rider时，这么做可能会出现无法识别到生成内容的问题，不影响编译，但是可能只有重启Rider才能恢复识别 */

    //------------------------------------------------------------------------------------------------------------------
    // User Part ↓

    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            LoadTheme();
        }

        private void ChangeTheme(object sender, RoutedEventArgs e)
        {
            ReverseThemeWithAnimation();
        }
    }

    //------------------------------------------------------------------------------------------------------------------
    // Theme Part ↓

    /* BrushConverter等其它Converter均由平台适配层（如VeloxDev.WPF）提供，可以将字符或其它形式的构造参数转换为具体的值 */
    /* ThemeConfig至少包含一个Converter和两个Theme（如Dark/Light），最多同时具备一个Converter外加7个Theme */
    [ThemeConfig<BrushConverter, Light, Dark>(nameof(Background), ["#ffffff"], ["#1e1e1e"])]
    [ThemeConfig<BrushConverter, Light, Dark>(nameof(Foreground), ["#1e1e1e"], ["#ffffff"])]
    public partial class MainWindow
    {
        private void LoadTheme()
        {
            InitializeTheme(); // 这句话必须调用,且必须晚于InitializeComponent()

            // [ 全局生效 ]
            // 如果您不使用带过渡效果的主题切换，那么可以不配置插值器，否则，这句话是必须调用的
            ThemeManager.SetPlatformInterpolator(new Interpolator());

            // [ 全局生效 ]
            // 当主题发生变化，您希望动画的起始状态是从缓存获取呢？还是反射获取当前状态作为起始呢？
            ThemeManager.StartModel = StartModel.Cache;
        }

        /// <summary>
        /// 主题切换具备回调
        /// </summary>
        /// <param name="oldValue">切换前的值</param>
        /// <param name="newValue">切换后的值</param>
        partial void OnThemeChanged(Type? oldValue, Type? newValue)
        {
            MessageBox.Show($"Theme changed from {oldValue?.Name} to {newValue?.Name}");
        }

        /// <summary>
        /// 这种主题切换会加载渐变动画
        /// </summary>
        private static void ReverseThemeWithAnimation()
        {
            var condition = ThemeManager.Current == typeof(Dark);
            if (condition)
            {
                ThemeManager.Transition<Light>(TransitionEffects.Theme);
            }
            else
            {
                ThemeManager.Transition<Dark>(TransitionEffects.Theme);
            }
        }

        /// <summary>
        /// 这种主题切换没有渐变动画
        /// </summary>
        private static void ReverseThemeWithOutAnimation()
        {
            var condition = ThemeManager.Current == typeof(Dark);
            if (condition)
            {
                ThemeManager.Jump<Light>();
            }
            else
            {
                ThemeManager.Jump<Dark>();
            }
        }

        /// <summary>
        /// 提供一组获取、编辑主题资源包的扩展，这些方法是自动生成的，例如此处它们都是MainWindow的方法
        /// </summary>
        private void ThemeValueEx()
        {
            // 动态编辑主题资源值
            EditThemeValue<Light>(nameof(Background), new object?[] { "#ffffff" });
            // 可以恢复为初始状态
            RestoreThemeValue<Light>(nameof(Foreground));

            // 获取静态资源
            var staticCache = GetStaticCache();
            // 获取动态资源
            var dynamicCache = GetActiveCache();

            /* 此处的“资源”是一个自动生成的复杂结构
               只有被修改过的属性才会存储在动态资源中，否则资源内不会存储东西，切换主题时，动态内容将覆盖静态内容
               Dictionary<string,Dictionary<PropertyInfo,Dictionary<Type,object?>>>
               
               从左往右
               string       -> name of property
               PropertyInfo -> target to use theme change
               Type         -> theme
               object?      -> value of property at the theme
               
               它提供了完全访问主题资源的能力
             */
        }
    }
}