# VeloxDev.Core.Generator

[![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core.Generator?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Core.Generator/) · [← 返回主页](../../../README.md)

> VeloxDev 的 Source Generator 集合——编译时自动生成 MVVM、Workflow、AOP、MonoBehaviour、Theme、AOT Reflection 等模块的样板代码，让开发者只需关注声明与业务逻辑。

---

## 生成能力概览

以下生成器在所有引用 VeloxDev 的项目中自动运行：

| 生成器 | 触发特性 | 生成内容 | 详细文档 |
|--------|---------|---------|---------|
| **VeloxProperty** | `[VeloxProperty]` | `INotifyPropertyChanged` 属性、集合变更分发 | [MVVM README](../../../Examples/MVVM/README.md) |
| **VeloxCommand** | `[VeloxCommand]` | 异步命令、并发控制、CanExecute 验证、生命周期事件 | [MVVM README](../../../Examples/MVVM/README.md) |
| **Workflow ViewModel** | `[WorkflowBuilder.ViewModel.Tree/Node/Slot/Link]` | Helper 属性、命令、行为包装、属性变更钩子、继承支持 | [Workflow README](../../../Examples/Workflow/README.md) |
| **ThemeManager** | `[ThemeConfig<...>]` | `InitializeTheme()`、主题值缓存、切换钩子 | [Theme README](../../../Examples/Theme/README.md) |
| **AspectOriented** | `[AspectOriented]` | 代理类 `T_ProxyNamespace_Aop`、属性/方法拦截桩 | [AOP README](../../../Examples/AOP/README.md) |
| **MonoBehaviour** | `[MonoBehaviour]` | 生命周期调度（`Update`/`FixedUpdate`/`Awake`/`Start`） | [MonoBehaviour README](../../../Examples/MonoBehaviour/README.md) |
| **AOT Reflection** | `[AOTReflection]` | `Init()` 方法，保留构造函数/属性/方法/字段元数据 | [AOTReflection README](../../../Examples/AOTReflection/README.md) |

## 补充生成器

以下生成器依托具体框架的适配层，非核心功能，随版本升级逐步支持：

| 生成器 | 说明 |
|--------|------|
| **Workflow View** | 为特定 UI 框架生成工作流视图层代码 |

---

## 典型使用方式

生成器通过 NuGet 传递依赖自动启用，无需手动配置。安装任意 VeloxDev 包后，在代码中添加对应特性即可触发生成：

```csharp
// MVVM：字段 → 通知属性 + 命令
public partial class MyViewModel
{
    [VeloxProperty] private string _name = "";

    [VeloxCommand(canValidate: true, semaphore: 3)]
    private async Task SaveAsync(object? parameter, CancellationToken ct) { }
    private partial bool CanExecuteSaveCommand(object? parameter) => !string.IsNullOrEmpty(_name);
}

// Workflow：四类组件声明
[WorkflowBuilder.ViewModel.Node<MyNodeHelper>(workSemaphore: 5)]
public partial class MyNode { public MyNode() => InitializeWorkflow(); }

// Theme：声明式主题配置
[ThemeConfig<BrushConverter, Light, Dark>(nameof(Background), ["#fff"], ["#1e1e1e"])]
public partial class MainWindow : Window { }

// MonoBehaviour：帧循环
[MonoBehaviour]
public partial class PhysicsComponent
{
    public PhysicsComponent() => InitializeMonoBehaviour();
    partial void Update(FrameEventArgs e) { }
}

// AOP：切面代理
public partial class Service
{
    [AspectOriented]
    public void Process() { }
}

// AOT Reflection：反射元数据保留
[AOTReflection(Properties: true, Methods: true)]
public class DataModel { public string Name { get; set; } }
```

> 💡 生成的代码可在 Visual Studio 的 **Solution Explorer → Dependencies → Analyzers → VeloxDev.Core.Generator** 中查看。
