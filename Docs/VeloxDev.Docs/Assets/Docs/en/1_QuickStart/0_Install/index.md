# Install

Choose the NuGet package matching your GUI framework.

## Adapter Packages (One per platform)

| Platform | Package | Command |
|----------|---------|---------|
| WPF | `VeloxDev.WPF` | `dotnet add package VeloxDev.WPF` |
| Avalonia | `VeloxDev.Avalonia` | `dotnet add package VeloxDev.Avalonia` |
| WinUI | `VeloxDev.WinUI` | `dotnet add package VeloxDev.WinUI` |
| MAUI | `VeloxDev.MAUI` | `dotnet add package VeloxDev.MAUI` |
| WinForms | `VeloxDev.WinForms` | `dotnet add package VeloxDev.WinForms` |
| Razor / Blazor | `VeloxDev.Razor` | `dotnet add package VeloxDev.Razor` |

## Core Library (Optional)

Framework-independent, for agent and serialization capabilities:

```shell
dotnet add package VeloxDev.Core.Extension
```

## Related Links

- [GitHub repository](https://github.com/Axvser/VeloxDev)
- [Examples Directory](https://github.com/Axvser/VeloxDev/tree/master/Examples)
- [NuGet · VeloxDev.Core](https://www.nuget.org/packages/VeloxDev.Core/)