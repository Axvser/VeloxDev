# 🚀 VeloxDev

> 一个高度模块化的工具集，融合 **MVVM、AOP、Workflow、动画系统** 与 **AOT 支持**，旨在多个.NET的UI框架中采用一致API完成常见编程任务

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

---

## ⬇️ 获取

| 框架 | 适配层 | NuGet |
|--------|------|-------|
| WPF | VeloxDev.WPF | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WPF?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WPF/) |
| Avalonia | VeloxDev.Avalonia | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Avalonia?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Avalonia/) |
| WinUI | VeloxDev.WinUI | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WinUI?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WinUI/) |
| MAUI | VeloxDev.MAUI | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.MAUI?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.MAUI/) |

---

## ✨ 概览

VeloxDev.Core 专为 **多UI框架API一致性** 而设计

* 🪶 MVVM 生成支持
* 🔁 灵活的拖拽式 **Workflow 构建系统**
* 🎞️ 统一的 **插值动画 API**
* 🌀 可插拔的 **AOP 调用拦截机制**
* 🎨 主题系统统一化管理与动态切换
* ⚙️ MonoBehaviour 为实例提供一个基于帧的循环刷新机制
* 📦 生成使AOT支持特定类反射的初始化代码

---

## 🧩 模块结构

### 🏗️ VeloxDev.Core

核心抽象层，可快速衍生出适配不同 UI 框架的子工具集，例如 VeloxDev.WPF / VeloxDev.Avalonia
- 一些核心功能已经有抽象实现，每次升级 VeloxDev.Core，其子工具集都可直接受益
- 广泛地运用 Source Generator 优化编码体验
- 抽象层保证了API在不同UI框架间的一致性

---

### 🪶 MVVM Toolkit

[![GitHub](https://img.shields.io/badge/GitHub-Demo_Avalonia-cyan?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/Avalonia/MVVM/Demo)

[![GitHub](https://img.shields.io/badge/GitHub-Demo_WPF-cyan?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/WPF/MVVM/Demo)

轻量 MVVM 工具，支持：

* 可观测属性（`[VeloxProperty]`）
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

[![GitHub](https://img.shields.io/badge/GitHub-Demo_Avalonia-cyan?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/Avalonia/Workflow/Demo)

[![GitHub](https://img.shields.io/badge/GitHub-Demo_WPF-cyan?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/WPF/Workflow/Demo)

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

[![GitHub](https://img.shields.io/badge/GitHub-Demo_Avalonia-cyan?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/Avalonia/Transition/Demo)  

[![GitHub](https://img.shields.io/badge/GitHub-Demo_WPF-cyan?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/WPF/Transition/Demo)

简洁的插值系统 ( 需要UI框架适配层 )

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

[![GitHub](https://img.shields.io/badge/GitHub-Demo_Avalonia-cyan?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/Avalonia/AOP/Demo)  

[![GitHub](https://img.shields.io/badge/GitHub-Demo_WPF-cyan?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/WPF/AOP/Demo)

以特性方式声明切面编程：

```csharp
[AspectOriented]
public void Execute() { ... }
```

在编译时注入代理逻辑，支持前后置钩子与方法替换

---

### 🎨 ThemeManager

[![GitHub](https://img.shields.io/badge/GitHub-Demo_Avalonia-cyan?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/Avalonia/Theme/Demo)  

[![GitHub](https://img.shields.io/badge/GitHub-Demo_WPF-cyan?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/WPF/Theme/Demo)

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

[![GitHub](https://img.shields.io/badge/GitHub-Demo_Avalonia-cyan?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/Avalonia/Mono/Demo)  

[![GitHub](https://img.shields.io/badge/GitHub-Demo_WPF-cyan?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/WPF/Mono/Demo)

类似游戏引擎的帧循环任务：( 注意只是API层面，它们本质是多个独立的 Task )

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
        partial void ExitMonoBehaviour()
        {
            
        }
    }
```

---

### 📦 AOT Reflection

[![GitHub](https://img.shields.io/badge/GitHub-Demo_Avalonia-cyan?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/Avalonia/AOT/Demo)  

[![GitHub](https://img.shields.io/badge/GitHub-Demo_WPF-cyan?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/WPF/AOT/Demo)

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
