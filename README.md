﻿# 🚀 VeloxDev

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

* [🔄 更新](#-更新)

---

## ⬇️ 获取

> 大部分功能由 VeloxDev.Core 直接支持 

|  框架  | 项目 | NuGet |  是否必须  |
|--------|------|-------|------------|
| .NET | VeloxDev.Core | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Core/)| ✔ |
| WPF | VeloxDev.WPF | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WPF?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WPF/) | ❌ |
| Avalonia | VeloxDev.Avalonia | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Avalonia?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Avalonia/) | ❌ |
| WinUI | VeloxDev.WinUI | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WinUI?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WinUI/) | ❌ |
| MAUI | VeloxDev.MAUI | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.MAUI?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.MAUI/) | ❌ |

> 小部分功能需要UI框架适配层支持

|  组件 | 描述 |
|-------|------|
| 插值动画 | 兼容指定平台指定类型的插值计算，为 Fluent API 构建动画提供支持 |
| 主题渐变切换 | 如果仅仅是跳转主题，直接用核心层即可，但是，加载带渐变效果的主题则必须有插值动画支持 |
| View代码生成 | 例如，处理 Workflow 的 View 时，一些交互可以由源代码生成器自动完成，作为一种扩展功能，它随版本更新会逐步支持 |


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

[![GitHub](https://img.shields.io/badge/GitHub-Demo-blue?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/MVVM)

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
<summary>Version 3.0.0</summary>

##### Roslyn 改进

- 修复不支持嵌套类定义的问题
- 修复NotifyProperty不支持可空类型的问题

##### Workflow 重构

- 改用 [ ViewModel ← Helper ] + View 结构，ViewModel 更轻量、更易于扩展与定制
- 新增对 Redo 操作的支持
- 移除 SlotCapacity 机制，转用 SlotChannel ，可定义更复杂的连接
- 优化 SlotState 机制，使其作为 [ Flags ] ，可表达更复杂的状态

</details>

<details>
<summary>Version 3.1.0</summary>

## VeloxCommand 改进

- 合并 ConcurrentVeloxCommand 与 VeloxCommand，统一为 VeloxCommand
- 更清晰的API语义
- 更成熟的并发与排队机制
- 更丰富的回调支持

## Event

| 名称 | 类型 | 说明 |
|---------|------|------|
| `ExecutionStarted` | `EventHandler<object?>` | 执行开始时触发 |
| `ExecutionCompleted` | `EventHandler<object?>` | 执行完成时触发 |
| `ExecutionFailed` | `EventHandler<(object? Parameter, Exception Exception)>` | 执行失败时触发 |
| `TaskEnqueued` | `EventHandler<object?>` | 任务入队时触发 |
| `TaskDequeued` | `EventHandler<object?>` | 任务出队时触发 |

## Method

| 方法名称 | 参数 | 返回值 | 说明 |
|---------|------|--------|------|
| `Lock()` | 无 | `void / Task` | 锁定命令，阻止新任务进入 |
| `UnLock()` | 无 | `void / Task` | 解锁命令，允许新任务进入 |
| `Notify()` | 无 | `void / Task` | 通知命令重新评估其执行状态 |
| `Clear()` | 无 | `void / Task` | 清除当前执行中的任务+等待队列中的任务 |
| `Interrupt()` | 无 | `void / Task` | 中断当前执行中的任务 |
| `Continue()` | 无 | `void / Task` | 继续执行等待队列中的任务 |
| `ChangeSemaphore(int semaphore)` | `semaphore`: 新的信号量值 | `void / Task` | 修改命令的信号量计数以控制并发任务 |

</details>
