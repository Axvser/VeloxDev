# VeloxDev

[![GitHub](https://img.shields.io/badge/GitHub-Repository-blue?logo=github)](https://github.com/Axvser/VeloxDev)  

当您在 .NET 平台使用诸如 WPF / Avalonia 等框架开发带 UI 程序时，此项目可为一些功能提供更简单、更统一的代码实现

> 举个例子，VeloxDev 为 WPF / Avalonia 等框架提供了统一的 Fluent API 用以构建插值过渡，您将以几乎为零的学习成本掌握如何使用 C# 代码在多个平台加载插值过渡而无需关注 XAML

---

# VeloxDev.Core

[![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Core/)

> VeloxDev.Core 是 VeloxDev 框架核心，包含一切必要的抽象，对于其中跨平台不变的部分进行了抽象类实现。实际使用时无需安装此项目，而是安装相应平台的 VeloxDev.×××

> 如果您希望使用 VeloxDev.Core 对指定框架构建自定义的库，可以参考 VeloxDev 在 github 的 Wiki

# Core
  - ⌈ TransitionSystem ⌋ , 使用Fluent API构建过渡效果 ✔
  - ⌈ AspectOriented ⌋ , 动态拦截/编辑属性、方法调用 ✔
  - ⌈ MonoBehaviour ⌋ , 实时帧刷新行为 ✔
  - ⌈ Visual Workflow Builder ⌋ ，拖拽式工作流构建器 ❌ 【 预计 V2 实装此项 】

# VeloxDev.×××

> 注意，由于作者难以一人掌握多个平台的特性，源生成功能并不总是完全的，您在选择下述产品时需要仔细查看

### VeloxDev.WPF [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WPF?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WPF/)

- Generator ✔


### VeloxDev.Avalonia [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Avalonia?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Avalonia/)

- Generator ✔


### VeloxDev.MAUI  [![NuGet](https://img.shields.io/nuget/v/VeloxDev.MAUI?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.MAUI/)

- Generator
  - AOP Interfaces ✔ 
  - Proxy Property ❌
  - MonoBehaviour ❌

---

# API

> VeloxDev.××× 是作者基于 VeloxDev.Core 实现的简易封装 , 可在 WPF / Avalonia / MAUI 使用统一的 API 去实现属性值过渡、AOP编程等功能

## Ⅰ 过渡

> WPF / Avalonia / MAUI 虽然各自使用不同的属性系统,但最终都会以标准CLR属性暴露给用户,基于这一特点,我们可以使用下述API来实现跨平台一致的动画创建

- 线程安全 ✔
- 缓动函数 ✔
- 生命周期 ✔
- 源生成器依赖 ❌

```csharp
            var transition = Transition.Create(this)
                .Await(TimeSpan.FromSeconds(3))// (可选) 等待 3s 后执行第一段动画
                .Property(x => x.Background, Brushes.Red)
                .Property(x => x.Opacity, 0.5d)
                .Effect(TransitionEffects.Theme) // 效果参数
                .Then() // 执行下一段动画 > (可选) AwaitThen()以延迟启动下一段动画
                .Property(x => x.Background, Brushes.Cyan)
                .Property(x => x.Opacity, 1d)
                .Effect((p) =>
                {
                    p.Duration = TimeSpan.FromSeconds(1);

                    p.EaseCalculator = Eases.Sine.InOut; 
                    // 此处为系统内缓动封装，你也可从接口派生自定义缓动函数

                    p.Awaked += (s, e) =>
                    {

                    };
                    p.Update += (s, e) =>
                    {

                    };
                }); // 使用自定义的效果参数

            transition.Start(); // (可选) 为 Start 传入其它同类型的实例以改变动画生效目标 
```

## Ⅱ AOP编程

> 对于公开可读写的属性或者公开可调用的方法,我们借助源生成器的力量即可对其动态代理,接着,这些属性或方法将能被我们拦截

- 源生成器依赖的写法
  - WPF ✔
  - Avalonia ✔
  - MAUI ❌

```csharp
    public class ObservableAttribute : Attribute
    {

    }

    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            Proxy.SetProxy(ProxyMembers.Setter, nameof(UID), // Setter 的 AOP
                null,
                (calls, result) =>
                {
                    var oldValue = UID;
                    var newValue = calls[0].ToString(); // 对于 Setter器，必定有一个参数 value
                    UID = newValue;
                    return Tuple.Create(oldValue, newValue); // 返回新值与旧值用于日志记录
                },
                (calls, result) =>
                {
                    var value = result as Tuple<string, string?>; // 接收上一个节点的返回值
                    MessageBox.Show($"值已更新 {value.Item1} → {value.Item2}"); // 编写日志
                    return null;
                });

            Proxy.SetProxy(ProxyMembers.Getter, nameof(Id), // Getter 的 AOP
                null,
                null,
                null);

            Proxy.SetProxy(ProxyMembers.Method,nameof(Do), // Method 的 AOP
                null,
                null,
                (calls, result) =>
                {
                    MessageBox.Show($"Do方法已执行过"); // 编写日志
                    return null;
                });
        }

        [AspectOriented]
        public string UID { get; set; } = "default";

        [AspectOriented]
        public void Do()
        {
            Proxy.UID = "newValue"; // 通过代理访问 UID 属性的 Setter
        }

        [Observable] // 若字段的某个特性包含 Observable 字样，则 AOP 接口会要求你实现其公开可读写属性 Id
        [AspectOriented]
        private int id = 3;

        public int Id
        {
            get => id;
            set => id = value;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Proxy.Do(); // 通过代理调用 Do 方法
        }
    }
```

- 若您选择MAUI，目前仅支持代理接口的动态生成，需要您在接口生成后执行如下操作
  - WPF ✔
  - Avalonia ✔
  - MAUI ✔

```csharp
    public partial class MainPage : ContentPage, MainPage_MauiTest_Aop // 此处必须显式实现动态生成的AOP接口
    {
        [AspectOriented]
        public void Do()
        {

        }

        public MainPage()
        {
            InitializeComponent();

            var proxy = this.CreateProxy<MainPage_MauiTest_Aop>(); // 此处必须显式指定AOP接口

            proxy.SetProxy(ProxyMembers.Method, nameof(Do), null, null, (s, e) =>
            {
                Background = Brush.Violet;
                return null;
            });

            proxy.Do(); // 通过代理调用 Do 方法
        }
    }
```

## Ⅲ MonoBehaviour

> 自动创建、维护一个实时循环任务，可以修改其 FPS 以控制刷新频率，示例中的 MonoBehaviour 不传入参数代表使用默认的 60 FPS

- 支持平台
  - WPF ✔
  - Avalonia ✔
  - MAUI ❌

```csharp
    [MonoBehaviour] // 默认 MonoBehaviour(60) 也就是 60 FPS
    public partial class MainWindow : Window
    {
        // 默认关闭,可以设置CanMonoBehaviour为true或false来开启或关闭 MonoBehaviour 功能
        
        partial void Start()
        {

        }
        partial void Update()
        {

        }
        partial void LateUpdate()
        {

        }
        partial void ExistMonoBehaviour()
        {
            
        }
    }
```