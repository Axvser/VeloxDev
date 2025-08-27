﻿# VeloxDev

[![GitHub](https://img.shields.io/badge/GitHub-Repository-blue?logo=github)](https://github.com/Axvser/VeloxDev)  

当您在 .NET 平台使用 WPF / Avalonia 等框架时，此项目可为一些功能提供简单、统一的代码实现

> 举个例子，VeloxDev 为 WPF / Avalonia 等框架提供了统一的 Fluent API 用以构建插值过渡，您将以几乎为零的学习成本掌握如何使用 C# 代码在多个平台加载插值过渡而无需关注 XAML

---

# VeloxDev.Core

[![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Core/)

> 定义一组核心接口与抽象类，并辅以源代码生成器加速项目开发

# Core
  - ⌈ MVVM Toolkit ⌋ , 自动化属性生成与命令生成 ✔
  - ⌈ Workflow ⌋ ，拖拽式工作流构建器 ✔
  - ⌈ Transition ⌋ , 使用Fluent API构建过渡效果 ✔ （ 依赖平台特定适配层 ）
  - ⌈ ThemeManager ⌋ , 仅需一个特性标记即可实现主题切换 ✔ （ 依赖平台特定适配层 ）
  - ⌈ AspectOriented ⌋ , 动态拦截/编辑属性、方法调用 ✔ （ 限 .NET5 + ）
  - ⌈ MonoBehaviour ⌋ , 实时帧刷新行为 ✔

# Product

> 通常不直接使用 VeloxDev.Core，因为动画相关的功能需要一些平台适配工作，但如果您不打算使用动画功能组，的确可以直接使用 VeloxDev.Core

> 我们已经封装了几个适配层，对 WPF / Avalonia 的支持比较完善，您可直接使用它们，或者参考其源码来实现属于您自己的平台适配 ( 差异主要集中在动画插值计算与UI线程调度，AI通常可以胜任其中的大部分工作 ) 

### VeloxDev.WPF [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WPF?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WPF/)

### VeloxDev.Avalonia [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Avalonia?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Avalonia/)

### VeloxDev.MAUI  [![NuGet](https://img.shields.io/nuget/v/VeloxDev.MAUI?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.MAUI/)

---

# API

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

---

## Ⅱ Workflow

> 思维导图、流程控制、电路模拟 …… 等诸多场景都会要求有可拖拽的流程编辑器。VeloxDev 提供了纯 MVVM 模式的功能实现，并且支持源代码生成

![](https://s3.bmp.ovh/imgs/2025/07/28/824c68b88eb1f5ec.png)

### 工作流核心接口表格

| **接口** | **关键属性** | **关键命令** | **核心功能** |
|----------|--------------|--------------|--------------|
| **IWorkflowContext** | `IsEnabled` - 启用状态<br>`UID` - 唯一标识<br>`Name` - 显示名称 | `UndoCommand` - 撤销操作 | 基础行为控制 |
| **IWorkflowLink** | `Sender` - 发送端槽位<br>`Processor` - 接收端槽位 | `DeleteCommand` - 删除连接 | 连接关系管理 |
| **IWorkflowNode** | `Parent` - 所属工作流树<br>`Anchor` - 节点坐标<br>`Size` - 节点尺寸<br>`Slots` - 槽位集合 | `CreateSlotCommand` - 创建槽位<br>`DeleteCommand` - 删除节点<br>`BroadcastCommand` - 广播消息<br>`ExecuteCommand` - 执行命令 | 节点核心功能 |
| **IWorkflowSlot** | `Targets` - 目标节点集合<br>`Sources` - 源节点集合<br>`Capacity` - 槽位能力类型<br>`State` - 当前状态<br>`Offset` - 槽位偏移坐标 | `ConnectingCommand` - 开始连接<br>`ConnectedCommand` - 完成连接 | 槽位连接管理 |
| **IWorkflowTree** | `VirtualLink` - 虚拟连接线<br>`Nodes` - 节点集合<br>`Links` - 连接线集合 | `CreateNodeCommand` - 创建节点<br>`SetMouseCommand` - 设置鼠标状态<br>`SetSenderCommand` - 设置发送端<br>`SetProcessorCommand` - 设置接收端 | 工作流树控制 |

### 核心实现类表格

| **类名** | **关键特性** | **运算符/方法** | **核心能力** |
|----------|--------------|-----------------|--------------|
| **Anchor** | 二维坐标管理 (Left/Top/Layer) | `+`/`-` 运算符<br>`Equals`/`GetHashCode` | 空间定位能力 |
| **Size** | 尺寸管理 (Width/Height) | `+`/`-` 运算符<br>NaN值特殊处理 | 自适应尺寸支持 |
| **LinkContext** | 连接状态自动检测<br>双向关系维护 | `DeleteCommand` - 连接删除<br>`OnSenderChanged` - 状态同步 | 连接生命周期管理 |
| **SlotContext** | 动态坐标偏移<br>连接状态跟踪 | `ConnectingCommand` - 启动连接<br>`ConnectedCommand` - 完成连接 | 智能连接调度 |

## 源代码生成特性

> Context 生成是通用的，我们在此处用表格列出生成支持，但是需要注意，若你启用这些生成，最好不要与其它MVVM工具混用，否则可能出现生成冲突，该库自身提供的 MVVM 功能是推荐的

> View 因其在各个框架间的实现差异大，我们会延后在各个框架的适配包中实现其专属的生成器以辅助您实现用户交互

| 特性 | 应用对象 | 参数 | 描述 |
|------|----------|------|------|
| `Workflow.Context.Tree` | 工作流树类 | `slotType`, `linkType` | 自定义槽位和连接线类型 |
| `Workflow.Context.Node` | 工作流节点类 | 无 | 标记自定义节点类型 |
| `Workflow.Context.Slot` | 工作流槽位类 | 无 | 标记自定义槽位类型 |
| `Workflow.Context.Link` | 工作流连接线类 | 无 | 标记自定义连接线类型 |

---

## Ⅲ 过渡

> WPF / Avalonia / MAUI 虽然各自使用不同的属性系统,但最终都会以标准CLR属性暴露给用户,基于这一特点,我们可以使用下述API来实现跨平台一致的动画创建

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

## Ⅳ AOP编程

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

## Ⅴ ThemeManager

> 极致简约的主题切换上下文维护，使用特性标记即可实现

> 首个泛型参数是一个值转换器，后面的泛型参数是主题类型，最多写入7个主题类型，每个主题使用一个object?[]传递构造参数

```csharp
    [ThemeConfig<ObjectConverter, Dark, Light>(nameof(Background), ["#1e1e1e"], ["#00ffff"])]
    [ThemeConfig<ObjectConverter, Dark, Light>(nameof(Foreground), ["#ffffff"], ["#1e1e1e"])]
    [ThemeConfig<ObjectConverter, Dark, Light>(nameof(Width), ["800"], ["400"])]
    public partial class MainWindow : Window
```

> 标记了特性后，类已经自动实现 IThemeObject 接口，你可使用下述方法来加载其主题功能

```csharp
    // [ 每个实例都需要 ] -> 初始化主题功能
    InitializeTheme();
    // [ 全局状态 ] -> 当前主题类型，只读，若要修改请在所有元素初始化主题前使用SetCurrent()修改
    ThemeManager.Current = typeof(Dark);
    // [ 全局选项 ] -> 主题切换时，初始值从 [ 反射 / 缓存 ] 获取
    ThemeManager.StartModel = StartModel.Reflect;
    // [ 全局仅执行一次 ] -> 设置平台插值器（ 由 VeloxDev.WPF / VeloxDev.MAUI … 等封装提供 ）
    ThemeManager.SetPlatformInterpolator(new Interpolator());
```

> 可以使用下述API来切换主题，注意需要特定平台的适配

```csharp
    // 渐变切换，支持缓动/FPS/持续时间参数，建议在界面元素少时使用
    ThemeManager.Transition<Light>(new TransitionEffect() { FPS = 60, Duration = TimeSpan.FromSeconds(3) });
    // 瞬时切换，适合界面元素较多时使用
    ThemeManager.Jump<Light>();
```

> 此外，生成内容还包括一些方法

```csharp
        // 主题切换的回调,可选择性地实现
        partial void OnThemeChanging(Type oldTheme, Type newTheme);
        partial void OnThemeChanged(Type oldTheme, Type newTheme);

        // 动态编辑主题资源值
        public void EditThemeValue<T>(string propertyName, object? newValue) where T : ITheme;
        public void RestoreThemeValue<T>(string propertyName) where T : ITheme;

        // 获取资源值字典
        public Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>> GetStaticCache();
        public Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>> GetActiveCache();
```

> ITheme 接口可用于自定义主题

```csharp
    public class Dark : ITheme
    {

    }
```

> IThemeValueConverter 接口可用于自定义主题转换器，例如，您希望从资源字典转换为主题资源，使其可以静态或动态地切换主题，那么自定义这个转换器是推荐的做法

```csharp
    public class ObjectConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            // 实现转换逻辑
            return value;
        }
    }
```

## Ⅵ MonoBehaviour

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