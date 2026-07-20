@"
# Install

Choose the NuGet package matching your GUI framework.

## Adapter Packages (One per platform)

| Platform | Package | NuGet |
|----------|---------|-------|
| WPF | `VeloxDev.WPF` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WPF?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WPF/) |
| Avalonia | `VeloxDev.Avalonia` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Avalonia?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Avalonia/) |
| WinUI | `VeloxDev.WinUI` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WinUI?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WinUI/) |
| MAUI | `VeloxDev.MAUI` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.MAUI?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.MAUI/) |
| WinForms | `VeloxDev.WinForms` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WinForms?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WinForms/) |
| Razor | `VeloxDev.Razor` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Razor?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Razor/) |

## Core Library

Framework‑independent, optional. Upgrades can be installed separately.

| Package | NuGet | Description |
|---------|-------|-------------|
| `VeloxDev.Core` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Core/) | Workflow abstractions, MVVM source generators, runtime models — zero third‑party dependencies |
| `VeloxDev.Core.Extension` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core.Extension?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Core.Extension/) | MAF‑based Workflow Agent tools, persistence, and runtime extensions |

## Related Links

- [GitHub repository](https://github.com/Axvser/VeloxDev)
- [Examples Directory](https://github.com/Axvser/VeloxDev/tree/master/Examples)
- [NuGet · VeloxDev.Core](https://www.nuget.org/packages/VeloxDev.Core/)
"@ | Set-Content "D:\VeloxDev\Docs\VeloxDev.Docs\Assets\Docs\en\QuickStart\Install\index.md" -Encoding UTF8