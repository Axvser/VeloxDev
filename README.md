<div align="center">

# ⚡ VeloxDev

**Build modern, AI-controllable workflow editors on any .NET GUI — WPF, Avalonia, WinUI, MAUI, or WinForms.**

[![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core?color=4caf50&logo=nuget&label=VeloxDev.Core)](https://www.nuget.org/packages/VeloxDev.Core/)
[![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core.Extension?color=4caf50&logo=nuget&label=VeloxDev.Core.Extension)](https://www.nuget.org/packages/VeloxDev.Core.Extension/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE.txt)
[![GitHub](https://img.shields.io/badge/GitHub-Axvser%2FVeloxDev-181717?logo=github)](https://github.com/Axvser/VeloxDev)

---

### 📖 [Full documentation & guides → Wiki](https://axvser.github.io/VeloxDev/)

---

</div>

## ✨ What is VeloxDev?

VeloxDev gives .NET developers a complete foundation for building **interactive workflow editors** — the kind where users drag nodes, wire slots together, and watch data flow through a graph at runtime.

The workflow system is the core. Everything else exists to make workflows **more extensible**, **more polished**, and **AI-controllable**:

| Layer | What it provides | Adapter needed? |
|-------|-----------------|:---------------:|
| ⛓️ **Workflow** | Tree / Node / Slot / Link templates with full undo-redo, spatial indexing, and a serialization model | ❌ |
| 🤖 **Workflow Agent** | 30+ Function Calling tools — an AI can create nodes, wire slots, patch properties, and manage routing at runtime via natural language | ✔ Extension |
| 🪶 **MVVM** | Source Generator for observable properties and async, cancellable commands — the glue that keeps node ViewModels lightweight | ❌ |
| 🎞️ **Transition** | Cross-platform interpolation animation with easing & Fluent API — smooth visual feedback for workflow state changes | ✔ |
| 🎨 **Theme** | Runtime theme switching with animated transitions — instant visual identity for your editor | ✔ |
| 🌀 **AOP** | Compile-time aspect proxies — intercept node execution, add logging or validation without modifying business logic | ❌ |
| ⚙️ **MonoBehaviour** | Frame-driven lifecycle loop — tick-based node simulation or real-time graph execution | ❌ |
| 📦 **AOT Reflection** | Source-generated reflection preservation — keeps workflow introspection working after trimming and AOT compilation | ❌ |

---

## 📦 Installation

Pick the adapter for your GUI framework and you get everything — workflow, agent, animations, and theming wired up for that platform.

### Platform adapter packages *(recommended)*

| Platform | Package | NuGet |
|----------|---------|-------|
| WPF | `VeloxDev.WPF` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WPF?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WPF/) |
| Avalonia | `VeloxDev.Avalonia` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Avalonia?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Avalonia/) |
| WinUI | `VeloxDev.WinUI` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WinUI?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WinUI/) |
| MAUI | `VeloxDev.MAUI` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.MAUI?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.MAUI/) |
| WinForms | `VeloxDev.WinForms` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WinForms?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WinForms/) |

### Build a WPF workflow view suite with the CLI

Run these commands from an existing WPF project. Replace `MyApp` with the
project's root namespace:

```powershell
dotnet new install VeloxDev.WPF.Templates
dotnet add package VeloxDev.WPF

dotnet new wpf-v-slot -n SlotView -ns MyApp.Views -o Views
dotnet new wpf-v-node -n NodeView -ns MyApp.Views -o Views
dotnet new wpf-v-link -n LinkView -ns MyApp.Views -o Views
dotnet new wpf-v-selector -n TemplateSelector -ns MyApp.Views -o Views
dotnet new wpf-v-decorator -n GridDecorator -ns MyApp.Views -o Views
dotnet new wpf-v-tree -n TreeView -ns MyApp.Views -o Views

dotnet build
```

The template package contains the Node, Slot, Link, Tree, template selector,
and grid decorator views. Each view template generates its XAML and code-behind
files with the required VeloxDev workflow behaviors already connected.

The Avalonia, WPF, WinUI, and MAUI template suites expose the same style
options. Common short aliases include:

| Template | Style aliases |
|----------|---------------|
| Node | `-bg` background, `-fg` foreground, `-bb` border brush, `-bt` border thickness, `-cr` corner radius |
| Slot | `-bg` background, `-sc` standby color, `-bc` border color |
| Link | `-lc` line color, `-lt` line thickness |
| Tree | `-bg` background, `-bb` border brush, `-bt` border thickness, `-cr` corner radius |
| Grid decorator | `-bg` background, `-mic` minor color, `-mac` major color, `-ac` axis color, `-gs` spacing, `-mle` major interval |

All templates use `-ns` for the generated namespace.

### Core-only packages *(bring your own adapter)*

| Package | NuGet | Description |
|---------|-------|-------------|
| `VeloxDev.Core` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Core/) | Workflow abstractions, MVVM generators, and runtime models — zero third-party dependencies |
| `VeloxDev.Core.Extension` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core.Extension?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Core.Extension/) | MAF-based Workflow Agent tools and additional runtime extensions |

---

## 🚀 Quick Look

### Define a node

```csharp
// Declare a node — the Source Generator handles INotifyPropertyChanged,
// slot lifecycle, and command wiring automatically.
[WorkflowBuilder.Node<MyNodeHelper>]
public partial class MyNodeViewModel
{
    public MyNodeViewModel() => InitializeWorkflow();

    [AgentContext(AgentLanguages.English, "Input slot (receiver)")]
    [VeloxProperty] public partial MySlotViewModel InputSlot { get; set; }

    [AgentContext(AgentLanguages.English, "Output slot (sender)")]
    [VeloxProperty] public partial MySlotViewModel OutputSlot { get; set; }

    [AgentContext(AgentLanguages.English, "Display title shown in the node header")]
    [VeloxProperty] private string title = "My Node";
}
```

### Let an AI control the workflow at runtime

```csharp
// One fluent call wires up discovery, tools, and the agent session.
var scope = tree.AsAgentScope()
    .WithAutoDiscovery(assemblyName: "MyApp")
    .WithInteractionSafety(3)          // confirm before destructive ops; present choices via tool
    .WithSelectionHandler(ShowDialog)
    .WithConfirmationHandler(ShowDialog);

var agent = chatClient.AsAIAgent(
    instructions: scope.ProvideProgressiveContextPrompt(),
    tools: scope.ProvideTools());
```

The agent can then create nodes, wire slots, change routing credentials, and patch properties — all through natural-language instructions, with full undo/redo support.

---

## 🗂️ Repository Layout

```
VeloxDev/
├── Src/
│   ├── Core/
│   │   ├── VeloxDev.Core                   # Workflow abstractions, MVVM generators & runtime models
│   │   ├── VeloxDev.Core.Extension         # MAF-based Workflow Agent tools & runtime extensions
│   │   ├── VeloxDev.Core.Test              # Unit tests for VeloxDev.Core
│   │   └── VeloxDev.Core.Extension.Test    # Unit tests for VeloxDev.Core.Extension
│   ├── Adapters/
│   │   ├── VeloxDev.WPF                    # WPF platform adapter
│   │   ├── VeloxDev.Avalonia               # Avalonia platform adapter
│   │   ├── VeloxDev.WinUI                  # WinUI 3 platform adapter
│   │   ├── VeloxDev.MAUI                   # .NET MAUI platform adapter
│   │   └── VeloxDev.WinForms               # WinForms platform adapter
│   ├── Generators/
│   │   └── VeloxDev.Core.Generator         # Roslyn Source Generators (netstandard2.0)
│   └── Templates/                          # dotnet new item templates for GUI adapters
├── Examples/
│   ├── Workflow/      WPF · Avalonia · WinUI · WinForms · MAUI · Common(Lib)
│   ├── MVVM/          WPF · Avalonia
│   ├── Transition/    WPF · Avalonia · WinUI · WinForms · MAUI
│   ├── Theme/         WPF · Avalonia
│   ├── AOP/           WPF · Avalonia
│   ├── AOTReflection/
│   └── MonoBehaviour/ WPF
└── Docs/
    ├── VeloxDev.Docs           # Documentation site (Blazor WebAssembly)
    ├── VeloxDev.Docs.Browser   # Browser-hosted docs entry point
    └── VeloxDev.Docs.Desktop   # Desktop-hosted docs entry point
```

---

## 📄 License

Released under the [MIT License](LICENSE.txt). © 2025 Axvser
