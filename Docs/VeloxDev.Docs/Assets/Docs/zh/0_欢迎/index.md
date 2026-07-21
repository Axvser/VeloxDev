# VeloxDev

VeloxDev 为 .NET 开发人员提供了构建交互式工作流编辑器所需的完整架构。

---

## 它解决什么问题？

在传统 .NET 应用中，构建**可视化工作流编辑器**需要自己实现：图的存储与遍历、节点拖拽与连线、属性编辑、撤销重做、序列化持久化、以及 AI 智能体交互。

VeloxDev 把这一切打包成一套**模块化的 .NET 库**——从核心图引擎到各 GUI 平台适配，一站式完成。

## 核心能力

| 模块 | 说明 |
|------|------|
| 🧩 **工作流引擎** | 基于图拓扑的编译执行系统（Tree → Node → Slot → Link） |
| ⚡ **MVVM 生成器** | `[VeloxProperty]` + `[VeloxCommand]` 编译时生成，零依赖 |
| 🎬 **动画系统** | 快照/属性/主题三种模式，14+ 原生类型插值器 |
| 🎨 **动态主题** | `[ThemeConfig]` 声明式 Light/Dark 切换，带动画过渡 |
| 🧵 **帧循环** | Unity 风格 `[MonoBehaviour]` 生命周期，多通道独立控制 |
| 🔄 **AOP** | `DispatchProxy` 运行时拦截，前置/后置/替代处理器 |
| 🤖 **AI 智能体** | MAF 框架将工作流组件映射为 LLM 工具定义，自然语言驱动 |
| 💾 **持久化** | 完整工作流图 ↔ JSON 序列化/反序列化 |

## 支持的平台

| 平台 | 包 | 工作流 UI | 主题/动画 |
|------|-----|-----------|----------|
| WPF | `VeloxDev.WPF` | ✅ 完整（附加属性 + 项模板） | ✅ |
| Avalonia | `VeloxDev.Avalonia` | ✅ 完整（附加属性 + 项模板） | ✅ |
| WinUI | `VeloxDev.WinUI` | ✅ 完整（附加属性 + 项模板） | ✅ |
| MAUI | `VeloxDev.MAUI` | ✅ 完整（附加属性 + 项模板） | ✅ |
| WinForms | `VeloxDev.WinForms` | ⚠️ Behavior（自绘控件，无附加属性） | ✅ |
| Razor / Blazor | `VeloxDev.Razor` | ⚠️ 基础（仅有动画/主题适配层） | ✅ |

## 包体系

```
VeloxDev.Core          ← 核心：工作流、MVVM、动画、主题、AOP、帧循环（零依赖）
VeloxDev.Core.Extension ← 扩展：AI 智能体 + 序列化
VeloxDev.WPF / Avalonia / ... ← 各平台适配层
```

> 💡 **推荐顺序**：先装 `VeloxDev.WPF`（或你的平台包），按需加装 `VeloxDev.Core.Extension`

## 相关链接

- [GitHub 仓库](https://github.com/Axvser/VeloxDev)
- [示例代码](https://github.com/Axvser/VeloxDev/tree/master/Examples)
- [NuGet · VeloxDev.Core](https://www.nuget.org/packages/VeloxDev.Core/)