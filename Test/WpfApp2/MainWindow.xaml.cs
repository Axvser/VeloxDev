using Newtonsoft.Json;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using VeloxDev.Core.DynamicTheme;
using VeloxDev.Core.Interfaces.DynamicTheme;
using VeloxDev.WPF.TransitionSystem;
using WpfApp2.ViewModels;

namespace WpfApp2
{
    public class StrConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            return null; // 假设可以从字符串参数构造值
        }
    }

    public class SrcConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            return null; // 假设可以从资源Key参数构造值
        }
    }

    [ThemeConfig<StrConverter, Dark, Light>(nameof(Background), ["#1e1e1e"], ["#ffffff"])]
    [ThemeConfig<StrConverter, Dark, Light>(nameof(Foreground), ["#ffffff"], ["#1e1e1e"])]
    public partial class MainWindow : Window, IThemeObject
    {
        // 跨框架动态主题实现
        // --------------------------------------------------------------------------------------------------------------
        // 源生成器构建 ↓

        private static readonly IThemeValueConverter __velox__Str__Converter__0__ = (IThemeValueConverter)Activator.CreateInstance(typeof(StrConverter))!;

        private static readonly Dictionary<string, IThemeValueConverter> __velox__Str__Converters__ = new()
        {
            { nameof(Background), __velox__Str__Converter__0__ },
            { nameof(Foreground), __velox__Str__Converter__0__ },
        };

        public static readonly Dictionary<string, PropertyInfo> __velox_Theme__Props__ = new()
        {
            { nameof(Background), typeof(MainWindow).GetProperty(nameof(Background))! },
            { nameof(Foreground), typeof(MainWindow).GetProperty(nameof(Foreground))! },
        };

        public static readonly Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>> __velox__Def__ThemeCache__ = new()
        {
            {
                nameof(Background),
                new Dictionary<PropertyInfo, Dictionary<Type, object?>>()
                {
                    {
                        __velox_Theme__Props__[nameof(Background)],
                        new Dictionary<Type, object?>()
                        {
                            { typeof(Dark), __velox__Str__Converters__[nameof(Background)].Convert(typeof(Brush),nameof(Background),["#1e1e1e"]) },
                            { typeof(Light), __velox__Str__Converters__[nameof(Background)].Convert(typeof(Brush),nameof(Background),["#ffffff"]) },
                        }
                    }
                }
            },
            {
                nameof(Foreground),
                new Dictionary<PropertyInfo, Dictionary<Type, object?>>()
                {
                    {
                        __velox_Theme__Props__[nameof(Foreground)],
                        new Dictionary<Type, object?>()
                        {
                            { typeof(Dark), __velox__Str__Converters__[nameof(Foreground)].Convert(typeof(Brush),nameof(Foreground),["#ffffff"]) },
                            { typeof(Light), __velox__Str__Converters__[nameof(Foreground)].Convert(typeof(Brush),nameof(Foreground),["#1e1e1e"]) },
                        }
                    }
                }
            }
        };

        public Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>> __velox__Act__ThemeCache__ = [];

        public void ExecuteThemeChanging(Type? oldValue, Type? newValue)
        {
            OnThemeChanging(oldValue, newValue);
        }
        public void ExecuteThemeChanged(Type? oldValue, Type? newValue)
        {
            OnThemeChanged(oldValue, newValue);
        }
        partial void OnThemeChanging(Type? oldValue, Type? newValue);
        partial void OnThemeChanged(Type? oldValue, Type? newValue);

        public void EditThemeValue<T>(string propertyName, object? newValue) where T : ITheme
        {
            if (__velox__Act__ThemeCache__.TryGetValue(propertyName, out var propertyCache) &&
                propertyCache.TryGetValue(__velox_Theme__Props__[propertyName], out var typeCache))
            {
                typeCache[typeof(T)] = newValue;
            }
            else
            {
                if (!__velox__Act__ThemeCache__.TryGetValue(propertyName, out Dictionary<PropertyInfo, Dictionary<Type, object?>>? value))
                {
                    value = [];
                    __velox__Act__ThemeCache__[propertyName] = value;
                }

                value[__velox_Theme__Props__[propertyName]] = new Dictionary<Type, object?> { { typeof(T), newValue } };
            }
        }
        public void RestoreThemeValue<T>(string propertyName) where T : ITheme => __velox__Act__ThemeCache__.Remove(propertyName);

        public Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>> GetStaticCache() => __velox__Def__ThemeCache__;
        public Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>> GetActiveCache() => __velox__Act__ThemeCache__;

        public void InitializeTheme()
        {
            ThemeManager.Register(this);
            __velox_Theme__Props__[nameof(Background)].SetValue(this, __velox__Def__ThemeCache__[nameof(Background)][__velox_Theme__Props__[nameof(Background)]][ThemeManager.Current]);
            __velox_Theme__Props__[nameof(Foreground)].SetValue(this, __velox__Def__ThemeCache__[nameof(Foreground)][__velox_Theme__Props__[nameof(Foreground)]][ThemeManager.Current]);
        }

        // 源生成器构建 ↑
        // --------------------------------------------------------------------------------------------------------------

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
            ThemeManager.Transition<Light>(new TransitionEffect() { Duration = TimeSpan.FromSeconds(3) });
            string json = File.ReadAllText(@"E:\\tree.json");
            var result = JsonConvert.DeserializeObject<FactoryViewModel>(json, settings) ?? new FactoryViewModel();
            container.DataContext = result;
            fc = result;
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            fc?.UndoCommand.Execute(null);
        }
    }
}