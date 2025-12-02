# 🚀 VeloxDev

> 在多个.NET的UI框架中采用一致API完成常见编程任务

---

## 📚 目录

* [✨ 概览](#-概览)
* [🧩 模块结构](#-模块结构)

  * [🏗️ VeloxDev.Core](#veloxdevcore)
  * [🪶 MVVM Toolkit](#mvvm-toolkit)
  * [⛓️ Workflow](#workflow)
  * [🎞️ 插值动画](#-插值动画)
  * [🌀 AOP 编程](#-aop-编程)
  * [🎨 ThemeManager](#-thememanager)
  * [⚙️ MonoBehaviour](#-monobehaviour)
  * [📦 AOT Reflection](#-aot-reflection)

* [🔄 更新](#-更新)

---

## ⬇️ 获取

> ① 若您喜欢拆箱即用的体验，从下述包列表选装即可

|  框架  | 项目 | NuGet | 依赖第三方库 | 备注 |
|--------|------|-------|--------------|------|
| WPF | VeloxDev.WPF | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WPF?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WPF/) | ❌ | 适配包 |
| Avalonia | VeloxDev.Avalonia | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Avalonia?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Avalonia/) | ❌ | 适配包 |
| WinUI | VeloxDev.WinUI | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WinUI?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WinUI/) | ❌ | 适配包 |
| MAUI | VeloxDev.MAUI | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.MAUI?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.MAUI/) | ❌ | 适配包 |
| .NET | VeloxDev.Core.Extension | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core.Extension?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Core.Extension/) | ✔ | 功能扩展包 |

> ② 若您或者您的项目满足下述条件，推荐直接安装核心库 
>
> 1. 您更喜欢了解抽象层的结构设计与实现，并亲自实现适配层
>
> 2. 您对于 Source Generator 如何辅助实现核心功能这一课题感兴趣
>
> 3. 您的项目并不依赖任何UI框架，但希望使用 VeloxDev.Core 提供的通用功能
>
> 4. 您的项目依赖于UI框架，但您不打算使用 `动画` 、`主题渐变切换` 和 `Views交互代码生成` 这些必须有适配层支持的功能

|  框架  | 项目 | NuGet |  依赖第三方库  | 备注 |
|--------|------|-------|----------------|------|
| .NET | VeloxDev.Core | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Core/)| ❌ | 核心库 |

---

## ✨ 概览

| 功能特性 | 描述 | 是否需要适配层 | 说明 |
|---------|------|---------------|-----------|
| 🪶 MVVM  | 自动生成NotifyProperty与Command | ❌ | |
| 🔁 Workflow  | 可视化拖拽式工作流设计器 | ❌ | |
| 🎞️ Transition | 跨平台的动画抽象层，支持缓动函数 | ✔ | 需要为不同平台实现具体的插值器、主线程检测器、调度器等 |
| 🌀 AOP | 面向切面编程的拦截框架 | ❌ | 需要目标框架 ≥ .NET5 |
| 🎨 Theme | 动态主题切换和样式管理 | ✔ | 需要适配不同平台的样式/资源系统 |
| ⚙️ MonoBehaviour | 按帧同步的循环刷新机制 | ❌ | |
| 📦 AOT - Reflect | 在AOT编译项目中生成反射调用代码 | ❌ | |

---

## 🧩 模块结构

### 🏗️ VeloxDev.Core

核心抽象层，可快速衍生出适配不同 UI 框架的子工具集，例如 VeloxDev.WPF / VeloxDev.Avalonia
- 一些核心功能已经有抽象实现，每次升级 VeloxDev.Core，其子工具集都可直接受益
- 广泛地运用 Source Generator 优化编码体验
- 抽象层保证了API在不同UI框架间的一致性

---

### 🪶 MVVM Toolkit

[![GitHub](https://img.shields.io/badge/GitHub-Demo-blue?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/MVVM)

轻量 MVVM 工具，支持：

* 通知属性（`[VeloxProperty]`）
* 命令（`[VeloxCommand]`）

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
            // 可在 VeloxCommand 的参数中选择 Command名称，默认 “Auto”
            // 可在 VeloxCommand 的参数中选择是否手动验证命令可执行性
            // 可在 VeloxCommand 的参数中选择信号量以启用并发
            return Task.CompletedTask;
        }

        private void Test()
        {
            // 下述三个方法均有 Async 版本
            DeleteCommand.Execute(null); // 执行
            DeleteCommand.Cancel();      // 取消当前执行中的 Task
            DeleteCommand.Interrupt();   // 取消包含排队 Task 在内的所有 Task
            
            DeleteCommand.Lock();        // 锁定命令以阻止所有任务继续
            DeleteCommand.UnLock();      // 解锁命令
        }
    }
```

---

### ⛓️ Workflow

[![GitHub](https://img.shields.io/badge/GitHub-Demo-blue?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/Workflow)

#### Workflow 生成支持

通过 **Source Generator** 自动生成拖拽式工作流 ViewModel 模板，它直接支持：
- 任务散播
- 节点挂载
- 节点拖动
- 任务并发、排队
- 操作取消
- 丰富的扩展点

---

### 🎞️ 插值动画

[![GitHub](https://img.shields.io/badge/GitHub-Demo-blue?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/Transition)

您可在多个UI框架中体验到下述动画能力

* 缓动支持（线性、缓入缓出、弹性、反弹等）
* 循环支持
* 回复支持
* Fluent API
* ThemeManager 联动支持渐变的主题切换

```csharp
                var effect1 = new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    LoopTime = 1,
                    FPS = 144,
                    Ease = Eases.Cubic.InOut,
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

---

### 🌀 AOP 编程

[![GitHub](https://img.shields.io/badge/GitHub-Demo-blue?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/AOP)

以特性方式声明切面编程：

```csharp
[AspectOriented]
public void Execute() { ... }
```

在编译时注入代理逻辑，支持前后置钩子与方法替换

---

### 🎨 ThemeManager

[![GitHub](https://img.shields.io/badge/GitHub-Demo-blue?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/Theme)

统一的主题控制模块

* 内置 Dark / Light 主题
* 可选插值动画
* 可一行特性声明多套主题
* 主题可以自定义

```csharp
    [ThemeConfig<ObjectConverter, Dark, Light>(nameof(Background), ["#1e1e1e"], ["#00ffff"])]
    [ThemeConfig<ObjectConverter, Dark, Light>(nameof(Foreground), ["#ffffff"], ["#1e1e1e"])]
    [ThemeConfig<ObjectConverter, Dark, Light>(nameof(Width), ["800"], ["400"])]
    public partial class MainWindow : Window
```

---

### ⚙️ MonoBehaviour

[![GitHub](https://img.shields.io/badge/GitHub-Demo-blue?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/MonoBehaviour)

类似游戏引擎的帧循环任务

```csharp
[MonoBehaviour]
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (s, e) =>
        {
            InitializeMonoBehaviour();  // 每个实例都需要执行
            MonoBehaviourManager.Start();  // 全局执行一次
        };
    }

    partial void Update(FrameEventArgs e)
    {
        // 每帧更新逻辑
        UpdatePerformanceDisplay(e);
        
        // 业务逻辑处理
        if (e.DeltaTime > 100)
        {
            Debug.WriteLine("帧率过低警告");
        }
    }

    partial void Awake()
    {
        // 组件初始化逻辑
        Debug.WriteLine("组件已唤醒");
    }

    partial void Start()
    {
        // 启动逻辑
        Debug.WriteLine("组件已启动");
    }
}
```

---

### 📦 AOT Reflection

[![GitHub](https://img.shields.io/badge/GitHub-Demo-blue?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/AOTReflection)

#### 💡 设计目标

在裁剪敏感的环境中保留特定类的反射资源

#### 🧱 声明方式

```csharp
using VeloxDev.Core.AOT;

[AOTReflection(Properties: true)]
public class Player
{
    public string Name { get; set; }
    public int Score;
}
```

#### ⚙️ 自动生成结果

程序启动前调用生成器自动生成的 `VeloxDev.Core.AOTReflection.Init()`：

```csharp
public static void Init()
{
    _ = typeof(global::Player).GetTypeInfo();
    _ = typeof(global::Player).GetConstructors(...);
    _ = typeof(global::Player).GetProperties(...);
}
```

### 🔄 更新

<details>
<summary>Version 3.3.0</summary>

## Transition 架构优化

> 具体事项
> 
> 1. UIThreadInspector 在运行过程中占用更少内存，提供更安全的属性更新机制 （ 特别是对于 WinUI ）
> 
> 2. StateSnapshot 链式动画执行过程重构，提供更符合直觉的行为
> 
> 3. 适配层现已不再需要实现 InterpolatorOutputCore 的 Update 函数
>
> 4. TransitionCore 提供更好的 Exit 机制，并且允许获取动画的 Scheduler

</details>