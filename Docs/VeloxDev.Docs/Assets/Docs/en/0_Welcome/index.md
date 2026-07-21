# VeloxDev

VeloxDev provides .NET developers with the complete architecture needed to build interactive workflow editors.

---

## What Problem Does It Solve?

Building a **visual workflow editor** in .NET traditionally requires implementing: graph storage and traversal, node drag-and-drop and wiring, property editing, undo/redo, serialization persistence, and AI agent interaction.

VeloxDev packages all of this into a set of **modular .NET libraries** — from the core graph engine to GUI platform adapters, all in one integrated system.

## Core Modules

| Module | Description |
|--------|-------------|
| 🧩 **Workflow Engine** | Graph-topology-based compilation & execution (Tree → Node → Slot → Link) |
| ⚡ **MVVM Generator** | `[VeloxProperty]` + `[VeloxCommand]` compile-time generation, zero dependencies |
| 🎬 **Animation System** | Snapshot / Property / Theme modes with 14+ native interpolators |
| 🎨 **Dynamic Theme** | Declarative Light/Dark switching with `[ThemeConfig]` and animated transitions |
| 🧵 **Frame Loop** | Unity-style `[MonoBehaviour]` lifecycle with multi-channel control |
| 🔄 **AOP** | `DispatchProxy` runtime interception with pre/post/replacement handlers |
| 🤖 **AI Agent** | MAF framework mapping workflow components to LLM tool definitions for natural language control |
| 💾 **Persistence** | Full workflow graph ↔ JSON serialization/deserialization |

## Supported Platforms

| Platform | Package | Workflow UI | Theme/Animation |
|----------|---------|-------------|-----------------|
| WPF | `VeloxDev.WPF` | ✅ Full (attached properties + item templates) | ✅ |
| Avalonia | `VeloxDev.Avalonia` | ✅ Full (attached properties + item templates) | ✅ |
| WinUI | `VeloxDev.WinUI` | ✅ Full (attached properties + item templates) | ✅ |
| MAUI | `VeloxDev.MAUI` | ✅ Full (attached properties + item templates) | ✅ |
| WinForms | `VeloxDev.WinForms` | ⚠️ Behaviors (self-drawn, no attached properties) | ✅ |
| Razor / Blazor | `VeloxDev.Razor` | ⚠️ Basic (platform adapters only) | ✅ |

## Package Architecture

```
VeloxDev.Core          ← Core: Workflow, MVVM, Animation, Theme, AOP, FrameLoop (zero dependencies)
VeloxDev.Core.Extension ← Extensions: AI Agent + Serialization
VeloxDev.WPF / Avalonia / ... ← Platform adapters
```

> 💡 **Getting Started**: Install `VeloxDev.WPF` (or your platform package), then add `VeloxDev.Core.Extension` as needed.

## Links

- [GitHub Repository](https://github.com/Axvser/VeloxDev)
- [Examples Directory](https://github.com/Axvser/VeloxDev/tree/master/Examples)
- [NuGet · VeloxDev.Core](https://www.nuget.org/packages/VeloxDev.Core/)