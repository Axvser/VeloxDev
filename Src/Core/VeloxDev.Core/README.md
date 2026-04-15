# VeloxDev

`VeloxDev` 是 VeloxDev 的基础包，负责提供整个框架共享的抽象、生成能力与可复用基础设施。

它主要面向两类场景：

- 你希望直接使用 VeloxDev 中与平台无关的能力
- 你希望基于核心抽象实现自己的平台适配层或扩展模块

## 包含的能力

- `MVVM`：基于特性的通知属性与命令生成
- `Workflow`：工作流树、节点、插槽、连线及其辅助模板
- `AI Workflow Context`：面向 Agent 的工作流语义上下文收集与文档生成
- `TransitionSystem`：动画状态、属性路径、插值器、调度与播放抽象
- `DynamicTheme`：主题注册、缓存、切换与渐变切换抽象
- `MonoBehaviour`：按帧驱动的生命周期模型
- `AOT Reflection`：面向 AOT / 裁剪场景的反射保留支持
- `AOP`：支持目标框架下的切面代理基础能力

## 什么时候只安装 `VeloxDev`

适合以下情况：

- 你只需要 `MVVM`、`Workflow`、`MonoBehaviour`、`AOT Reflection` 等平台无关能力
- 你希望理解并直接构建在核心抽象之上
- 你打算自行实现 UI 平台适配层

## 什么时候还需要适配层包

如果你要直接使用下列能力的完整运行时效果，通常还需要对应平台适配层：

- `TransitionSystem`
- 带动画的主题切换
- 与具体 UI 平台相关的视图交互能力

对应适配包包括：

- `VeloxDev.WPF`
- `VeloxDev.Avalonia`
- `VeloxDev.WinUI`
- `VeloxDev.MAUI`
- `VeloxDev.WinForms`

## 仓库与示例

- GitHub: https://github.com/Axvser/VeloxDev
- Wiki: https://axvser.github.io/VeloxDev.Wiki/
- Examples: 请参考仓库中的 `Examples` 目录

## Workflow Agent Context

`VeloxDev.Core` 现已提供工作流框架层的 Agent 语义上下文能力

> 您可以通过 `AgentContextAttribute` 为Agent提供上下文，支持按语言划分（需要安装 `VeloxDev.Core.Extension`）

```csharp
[AgentContext(AgentLanguages.Chinese, "派生的Node组件之一，作为任务执行者")]
[WorkflowBuilder.Node
    <HttpHelper<NodeViewModel>>
    (workSemaphore: 5)]
public partial class NodeViewModel
{
    public NodeViewModel() => InitializeWorkflow();

    [AgentContext(AgentLanguages.Chinese, "输入口")]
    [VeloxProperty] public partial SlotViewModel InputSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "输出口")]
    [VeloxProperty] public partial SlotViewModel OutputSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "标题")]
    [VeloxProperty] private string title = "Workflow Step";
}
```
