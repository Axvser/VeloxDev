# 🚀 VeloxDev

[![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core?color=green&logo=nuget&label=VeloxDev.Core)](https://www.nuget.org/packages/VeloxDev.Core/)
[![GitHub](https://img.shields.io/badge/GitHub-Axvser%2FVeloxDev-blue?logo=github)](https://github.com/Axvser/VeloxDev)

> **VeloxDev** 是一组面向现代 .NET 的开发工具与基础设施，围绕 `Source Generator`、`跨平台抽象` 与 `可扩展运行时模型` 设计。它将多个常见但彼此分散的能力收敛到统一体系中：MVVM 代码生成、可扩展 Workflow 模板、跨 UI 框架动画抽象、动态 Theme 切换、MonoBehaviour 式帧循环、AOT / AOP 生成支持，以及通用的 Agent 反射基础设施。

---

## 📚 目录

- [⬇️ 获取](#-获取)
- [✨ 功能概览](#-功能概览)
- [🏗️ VeloxDev.Core](#️-veloxdevcore)
- [🪶 MVVM Toolkit](#-mvvm-toolkit)
- [⛓️ Workflow](#️-workflow)
- [🤖 Agent Infrastructure](#-agent-infrastructure)
- [🎞️ 插值动画](#️-插值动画)
- [🌀 AOP 编程](#-aop-编程)
- [🎨 ThemeManager](#-thememanager)
- [⚙️ MonoBehaviour](#️-monobehaviour)
- [📦 AOT Reflection](#-aot-reflection)
- [📖 详细文档索引](#-详细文档索引)

---

## ⬇️ 获取

> ① 若您喜欢拆箱即用的体验，从下述包列表选装即可

|  框架  | 项目 | NuGet | 依赖第三方库 | 备注 |
|--------|------|-------|--------------|------|
| WPF | VeloxDev.WPF | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WPF?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WPF/) | ❌ | 适配包 |
| Avalonia | VeloxDev.Avalonia | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Avalonia?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Avalonia/) | ❌ | 适配包 |
| WinUI | VeloxDev.WinUI | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WinUI?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WinUI/) | ❌ | 适配包 |
| MAUI | VeloxDev.MAUI | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.MAUI?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.MAUI/) | ❌ | 适配包 |
| WinForms | VeloxDev.WinForms | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WinForms?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WinForms/) | ❌ | 适配包 |
| .NET | VeloxDev.Core.Extension | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core.Extension?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Core.Extension/) | ✔ | 功能扩展包 |

> ② 若您或者您的项目满足下述条件，可仅安装核心库
>
> 1. 您更希望直接使用共享抽象、生成能力与基础设施，或自行实现适配层
>
> 2. 您暂时不依赖 `动画运行时`、`主题渐变切换` 和 `View 侧交互支持` 这些通常需要适配层协作的功能

|  框架  | 项目 | NuGet |  依赖第三方库  | 备注 |
|--------|------|-------|----------------|------|
| .NET | VeloxDev.Core | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Core/)| ❌ | 核心抽象与生成能力 |

---

## ✨ 功能概览

> 每个功能都有对应的跨平台 Demo（见 `Examples` 目录），覆盖 WPF / Avalonia / WinUI / MAUI / WinForms。

| 功能 | 描述 | 需要适配层 | 详细文档 |
|------|------|:---------:|---------|
| 🪶 **MVVM** | Source Generator 生成通知属性与异步命令 | ❌ | [→ 文档](Examples/MVVM/README.md) |
| ⛓️ **Workflow** | 工作流树、节点、插槽、连线模板与交互辅助 | ❌ | [→ 文档](Examples/Workflow/README.md) |
| 🤖 **Agent Infrastructure** | 零依赖反射工具：属性读写、方法调用、命令发现执行、语义上下文 | ❌ | [→ Core 文档](Src/Core/VeloxDev.Core/README.md) |
| 🤖 **Workflow Agent** | 基于 MAF 的 30+ Function Calling 运行时接管工具集 | ✔ Extension | [→ Extension 文档](Src/Core/VeloxDev.Core.Extension/README.md) |
| 🎞️ **Transition** | 跨平台的状态插值动画：缓动、循环、Fluent API | ✔ | [→ 文档](Examples/Transition/README.md) |
| 🌀 **AOP** | 编译期注入的切面代理，支持前后置钩子与方法替换 | ❌ | [→ 文档](Examples/AOP/README.md) |
| 🎨 **Theme** | 主题注册、缓存、渐变切换 | ✔ | [→ 文档](Examples/Theme/README.md) |
| ⚙️ **MonoBehaviour** | 按帧驱动的生命周期循环机制 | ❌ | [→ 文档](Examples/MonoBehaviour/README.md) |
| 📦 **AOT Reflection** | AOT / 裁剪场景中的反射保留代码生成 | ❌ | [→ 文档](Examples/AOTReflection/README.md) |

---

### 🏗️ VeloxDev.Core

`VeloxDev.Core` 是整个 VeloxDev 的基础层，负责承载共享抽象、生成入口与跨模块可复用基础设施。

它当前主要包含：

* `MVVM`：`[VeloxProperty]` 与 `[VeloxCommand]`，用于生成通知属性、命令以及命令生命周期控制
* `Workflow`：工作流树 / 节点 / 插槽 / 连线的模板特性与通用交互模型
* `AI`：通用的 Agent 反射基础设施——属性读写、方法调用、命令发现执行、语义上下文读取、类型解析（零第三方依赖）
* `Transition`：插值动画、缓动、时间调度与状态快照抽象
* `Theme`：主题对象注册、主题缓存、即时切换与渐变切换
* `MonoBehaviour`：按帧更新的生命周期模型
* `AOT Reflection`：面向 AOT 和裁剪环境的反射保留生成
* `AOP`：在支持的目标框架上提供切面代理标记能力

可以把它理解为 VeloxDev 的“能力底座”：

- 对上，为业务代码提供统一的特性、接口与基础运行模型
- 对下，为各平台适配层提供可复用的动画、主题与线程抽象

其中 `Transition` 与 `Theme` 的完整运行通常需要平台适配层配合；而 `MVVM`、`Workflow`、`AI`、`MonoBehaviour`、`AOT Reflection` 等能力则可直接建立在核心库之上。

`VeloxDev.Core.AI` 模块提供的通用反射能力适用于任何 .NET 项目中的 Agent 场景，不限于工作流框架；如果需要让 Agent 通过 Microsoft Agent Framework (MAF) 的 Function Calling 对工作流进行运行时接管，请安装 `VeloxDev.Core.Extension`。

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
            DeleteCommand.Interrupt();   // 取消当前执行中的 Task
            DeleteCommand.Clear();       // 取消包含排队 Task 在内的所有 Task
            
            DeleteCommand.Lock();        // 锁定命令 - 阻止新的任务开始
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

#### Workflow Agent 支持

VeloxDev 的 Workflow 体系原生支持 AI Agent 接管，构建在核心层的通用 Agent 反射基础设施之上。

核心层（`VeloxDev.Core`）提供：
- `IWorkflowIdentifiable` — 组件运行时唯一标识
- `[AgentContext]` / `[AgentCommandParameter]` — 面向 Agent 的语义上下文特性
- `AgentContextReader` / `AgentPropertyAccessor` / `AgentMethodInvoker` / `AgentCommandDiscoverer` / `AgentTypeResolver` — 通用反射能力（详见 [Agent Infrastructure](#-agent-infrastructure)）

扩展层（`VeloxDev.Core.Extension`）提供完整的运行时接管能力：
- 基于 **Microsoft Agent Framework (MAF)** 的 59+ 个 Function Calling 工具
- 覆盖查询、变更、连接、执行、撤销重做、类型自省、状态快照、批量操作、克隆、图遍历、自动布局、Slot 集合管理、连线管理、分析验证、复合操作等全部场景
- 三层安全机制保护框架内部属性不被误修改
- **预加载上下文**：注册的组件类型的 `[AgentContext]` 描述（含默认值）直接嵌入 Prompt，Agent 无需额外工具调用即可获知默认尺寸、属性语义等

> 详细的架构说明、原理介绍与完整 API 列表请参阅 [`VeloxDev.Core.Extension` README](Src/Core/VeloxDev.Core.Extension/README.md)。

---

### 🤖 Agent Infrastructure

`VeloxDev.Core.AI` 提供了一组**零第三方依赖**的通用反射工具，适用于任何 .NET 项目中的 Agent 场景——不限于工作流框架。

**核心能力一览：**

| 类 | 一句话说明 |
|---|---|
| `AgentPropertyAccessor` | 反射属性读写，支持批量 Patch、类型转换、黑名单 |
| `AgentMethodInvoker` | 反射方法调用，支持重载解析、参数默认值 |
| `AgentCommandDiscoverer` | ICommand 发现与执行 |
| `AgentContextReader` | 读取 `[AgentContext]` 语义上下文 |
| `AgentTypeResolver` | 跨程序集类型解析 |

**快速体验：**

```csharp
// 发现属性 → 设置属性 → 发现命令 → 执行命令
var props = AgentPropertyAccessor.DiscoverProperties(myViewModel, AgentLanguages.Chinese);
AgentPropertyAccessor.SetPropertyValue(myViewModel, "Title", "新标题");

var commands = AgentCommandDiscoverer.DiscoverCommands(myViewModel, AgentLanguages.Chinese);
AgentCommandDiscoverer.Execute(myViewModel, "Delete");
```

> 📖 完整 API 说明、标注特性（`[AgentContext]` / `[AgentCommandParameter]`）与 MAF 工具集包装请参阅 [`VeloxDev.Core` README](Src/Core/VeloxDev.Core/README.md) 和 [`VeloxDev.Core.Extension` README](Src/Core/VeloxDev.Core.Extension/README.md)。

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

---

## 📖 详细文档索引

根据你的目标选择对应文档：

| 目标 | 文档 |
|------|------|
| **快速上手某个功能** | 见上方各功能段落中的 Demo 链接 |
| **了解核心库能力** | [`VeloxDev.Core` README](Src/Core/VeloxDev.Core/README.md) |
| **了解扩展包（Agent MAF 工具 / JSON 序列化）** | [`VeloxDev.Core.Extension` README](Src/Core/VeloxDev.Core.Extension/README.md) |
| **了解 Source Generator 覆盖范围** | [`VeloxDev.Core.Generator` README](Src/Generators/VeloxDev.Core.Generator/README.md) |
| **深入 Workflow 架构与传播模型** | [`Workflow` README](Examples/Workflow/README.md) |
| **深入 TransitionSystem 架构与自定义适配层** | [`Transition` README](Examples/Transition/README.md) |
| **MVVM 属性/命令/集合通知完整 API** | [`MVVM` README](Examples/MVVM/README.md) |
| **AOP 代理钩子完整 API** | [`AOP` README](Examples/AOP/README.md) |
| **Theme 动态主题完整 API** | [`Theme` README](Examples/Theme/README.md) |
| **MonoBehaviour 帧循环完整 API** | [`MonoBehaviour` README](Examples/MonoBehaviour/README.md) |
| **AOT Reflection 完整 API** | [`AOTReflection` README](Examples/AOTReflection/README.md) |