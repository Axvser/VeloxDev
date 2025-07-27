# VeloxDev

[![GitHub](https://img.shields.io/badge/GitHub-Repository-blue?logo=github)](https://github.com/Axvser/VeloxDev)  

当您在 .NET 平台使用诸如 WPF / Avalonia 等框架开发带 UI 程序时，此项目可为一些功能提供更简单、更统一的代码实现

> 举个例子，VeloxDev 为 WPF / Avalonia 等框架提供了统一的 Fluent API 用以构建插值过渡，您将以几乎为零的学习成本掌握如何使用 C# 代码在多个平台加载插值过渡而无需关注 XAML

---

# VeloxDev.Core

[![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Core/)

> VeloxDev.Core 是 VeloxDev 核心，包含一切必要的抽象并对其中跨平台不变的部分进行了抽象类实现。实际使用时无需安装此项目，而是安装相应平台的 VeloxDev.×××

> 同时，你也可以使用 VeloxDev.Core 对指定框架构建自定义的工具包

# Core
  - ⌈ MVVM Toolkit ⌋ , 自动化属性生成与命令生成 ✔
  - ⌈ Workflow ⌋ ，拖拽式工作流构建器 ✔
  - ⌈ Transition ⌋ , 使用Fluent API构建过渡效果 ✔
  - ⌈ AspectOriented ⌋ , 动态拦截/编辑属性、方法调用 ✔
  - ⌈ MonoBehaviour ⌋ , 实时帧刷新行为 ✔

# Product

> 若您不想手动封装 VeloxDev.Core，可以直接选择下方列出的封装

### VeloxDev.WPF [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WPF?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WPF/)


### VeloxDev.Avalonia [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Avalonia?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Avalonia/)


### VeloxDev.MAUI  [![NuGet](https://img.shields.io/nuget/v/VeloxDev.MAUI?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.MAUI/)

---

# API

> 使用完全一致的 API 在 WPF / Avalonia / MAUI 等框架中加载动画、实施AOP编程 …… 

## Ⅰ MVVM Toolkit

> 标记 [ VeloxProperty ] 与 [ VeloxCommand ] 以更快构建 ViewModel

> 注意 : 这将生成 MVVM 接口实现，与其它 MVVM 工具混用可能导致生成内容重复

```csharp
    public sealed partial class SlotContext
    {
        [VeloxProperty]
        private string name = string.Empty;

        partial void OnNameChanged(string oldValue,string newValue)
        {
            DeleteCommand.Notify(); // 通知命令可执行态的改变
        }

        [VeloxCommand]
        private Task Delete(object? parameter, CancellationToken ct)
        {
            // …… 此处执行你的命令逻辑
            // 可在 VeloxCommand 的参数中选择是否手动验证命令可执行性
            // 可在 VeloxCommand 的参数中选择排队执行或并发执行
            return Task.CompletedTask;
        }

        private void Test()
        {
            var state = DeleteCommand.IsExecuting; // 查询是否有执行中的 Task
            DeleteCommand.Execute(null); // 执行
            DeleteCommand.Cancel();      // 取消当前执行中的 Task
            DeleteCommand.Interrupt();   // 取消包含排队 Task 在内的所有 Task
        }
    }
```

## Ⅱ Workflow

> 思维导图、流程控制、电路模拟 …… 等诸多场景都会要求有可拖拽的流程编辑器。VeloxDev 提供了纯 MVVM 模式的功能实现。（ 其实功能已经有了，但是这种系统还需要大量测试、优化，文档再等俩天，会有的 ）


## Ⅰ 过渡

> WPF / Avalonia / MAUI 虽然各自使用不同的属性系统,但最终都会以标准CLR属性暴露给用户,基于这一特点,我们可以使用下述API来实现跨平台一致的动画创建

- 线程安全 ✔
- 缓动函数 ✔
- 生命周期 ✔

```csharp
                var effect1 = new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    LoopTime = 1,
                    EaseCalculator = Eases.Cubic.InOut,
                };

                effect1.Completed += (s, e) =>
                {
                    MessageBox.Show("Animation Completed");
                };

                var animation = Transition<Window>.Create()
                    .Property(w => w.Background, Brushes.Violet)
                    .Effect(effect1)
                    .Then()
                    .Property(w => w.Background, Brushes.Lime)
                    .Effect(effect1)
                    .Execute(this);

                // Transition<Window>.Execute(this);
```

## Ⅱ AOP编程

> 对于公开可读写的属性或者公开可调用的方法,我们借助源生成器的力量即可对其动态代理,接着,这些属性或方法将能被我们拦截

> 但因其本质是对 DispatchProxy 的封装,此功能仅在 .NET5 + 项目可用

```csharp
    public partial class Factory
    {
        [AspectOriented]
        public string UID { get; set; } = "default";

        [AspectOriented]
        public void Do()
        {
            
        }

        [AspectOriented]
        private int id = 3;

        // 也可以给字段标记，但这么做就必须实现对应的可读可写属性
        public int Id
        {
            get => id;
            set => id = value;
        }

        // 这里是一些编辑代理逻辑的示例，你在初始化类型后可对实例的Proxy进行编辑
        private void SetProxy()
        {
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

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Proxy.UID = "newUID"; // 通过代理设置 UID 属性
            Proxy.Do(); // 通过代理调用 Do 方法
        }
    }
```

## Ⅲ MonoBehaviour

> 自动创建、维护一个实时循环任务，可以修改其 FPS 以控制刷新频率，示例中的 MonoBehaviour 不传入参数代表使用默认的 60 FPS

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