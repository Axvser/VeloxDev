<div align="center">

# ⚡ VeloxDev

**A modern .NET infrastructure toolkit — Source Generators, cross-platform abstractions, and extensible runtime models.**

[![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core?color=4caf50&logo=nuget&label=VeloxDev.Core)](https://www.nuget.org/packages/VeloxDev.Core/)
[![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core.Extension?color=4caf50&logo=nuget&label=VeloxDev.Core.Extension)](https://www.nuget.org/packages/VeloxDev.Core.Extension/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE.txt)
[![GitHub](https://img.shields.io/badge/GitHub-Axvser%2FVeloxDev-181717?logo=github)](https://github.com/Axvser/VeloxDev)

---

### 📖 [Full documentation & guides → Wiki](https://axvser.github.io/VeloxDev/)

---

</div>

## ✨ What is VeloxDev?

VeloxDev brings together a set of carefully designed capabilities that are often scattered across separate libraries:

| Feature | Description | Adapter needed? |
|---------|-------------|:---------------:|
| 🪶 **MVVM** | Source Generator for observable properties & async commands | ❌ |
| ⛓️ **Workflow** | Drag-and-drop workflow tree / node / slot / wire templates | ❌ |
| 🤖 **Agent Infrastructure** | Zero-dependency reflection utilities: property access, method invocation, command discovery, semantic context | ❌ |
| 🤖 **Workflow Agent** | 30+ Function Calling tools for AI runtime control via MAF | ✔ Extension |
| 🎞️ **Transition** | Cross-platform interpolation animation with easing & Fluent API | ✔ |
| 🌀 **AOP** | Compile-time aspect proxies: pre/post hooks and method replacement | ❌ |
| 🎨 **Theme** | Theme registration, caching, instant & animated switching | ✔ |
| ⚙️ **MonoBehaviour** | Frame-driven lifecycle loop (Update / Start / OnDestroy …) | ❌ |
| 📦 **AOT Reflection** | Source-generated reflection preservation for AOT & trimming | ❌ |

---

## 📦 Installation

### Platform adapter packages *(batteries included)*

| Platform | Package | NuGet |
|----------|---------|-------|
| WPF | `VeloxDev.WPF` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WPF?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WPF/) |
| Avalonia | `VeloxDev.Avalonia` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Avalonia?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Avalonia/) |
| WinUI | `VeloxDev.WinUI` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WinUI?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WinUI/) |
| MAUI | `VeloxDev.MAUI` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.MAUI?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.MAUI/) |
| WinForms | `VeloxDev.WinForms` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WinForms?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WinForms/) |

### Core-only packages *(build your own adapter)*

| Package | NuGet | Description |
|---------|-------|-------------|
| `VeloxDev.Core` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Core/) | Shared abstractions, generators & runtime models — no third-party dependencies |
| `VeloxDev.Core.Extension` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core.Extension?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Core.Extension/) | MAF-based Workflow Agent tools & additional runtime extensions |

---

## 🚀 Quick Look

```csharp
// Observable property + async command — generated entirely by Source Generator
public sealed partial class MyViewModel
{
    [VeloxProperty]
    private string title = string.Empty;

    partial void OnTitleChanged(string oldValue, string newValue) { /* react */ }

    [VeloxCommand]
    private async Task Save(object? parameter, CancellationToken ct)
    {
        await DoSaveAsync(ct);
    }

    private void ControlCommands()
    {
        SaveCommand.Execute(null);   // run
        SaveCommand.Interrupt();     // cancel current task
        SaveCommand.Clear();         // cancel all queued tasks
        SaveCommand.Lock();          // prevent new executions
        SaveCommand.UnLock();
    }
}
```

---

## 🗂️ Repository Layout

```
VeloxDev/
├── Src/
│   ├── Core/
│   │   ├── VeloxDev.Core                   # Shared abstractions, runtime models & generators
│   │   ├── VeloxDev.Core.Extension         # MAF-based Workflow Agent tools & runtime extensions
│   │   ├── VeloxDev.Core.Test              # Unit tests for VeloxDev.Core
│   │   └── VeloxDev.Core.Extension.Test    # Unit tests for VeloxDev.Core.Extension
│   ├── Adapters/
│   │   ├── VeloxDev.WPF                    # WPF platform adapter
│   │   ├── VeloxDev.Avalonia               # Avalonia platform adapter
│   │   ├── VeloxDev.WinUI                  # WinUI 3 platform adapter
│   │   ├── VeloxDev.MAUI                   # .NET MAUI platform adapter
│   │   └── VeloxDev.WinForms               # WinForms platform adapter
│   └── Generators/
│       └── VeloxDev.Core.Generator         # Roslyn Source Generators (netstandard2.0)
├── Examples/
│   ├── MVVM/          WPF · Avalonia
│   ├── AOP/           WPF · Avalonia
│   ├── AOTReflection/
│   ├── Workflow/      WPF · Avalonia · WinUI · WinForms · MAUI · Common(Lib)
│   ├── Transition/    WPF · Avalonia · WinUI · WinForms · MAUI
│   ├── Theme/         WPF · Avalonia
│   └── MonoBehaviour/ WPF
└── Docs/
    ├── VeloxDev.Docs           # Documentation site (Blazor WebAssembly)
    ├── VeloxDev.Docs.Browser   # Browser-hosted docs entry point
    └── VeloxDev.Docs.Desktop   # Desktop-hosted docs entry point
```

---

## 📄 License

Released under the [MIT License](LICENSE.txt). © 2025 Axvser
