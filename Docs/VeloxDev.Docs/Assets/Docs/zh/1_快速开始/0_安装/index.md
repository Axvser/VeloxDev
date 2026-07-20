# 安装

根据 GUI 框架选择对应的 NuGet 包：

## 适配包（每个平台一个）

| 平台 | 包名 | 命令 |
|------|------|------|
| WPF | `VeloxDev.WPF` | `dotnet add package VeloxDev.WPF` |
| Avalonia | `VeloxDev.Avalonia` | `dotnet add package VeloxDev.Avalonia` |
| WinUI | `VeloxDev.WinUI` | `dotnet add package VeloxDev.WinUI` |
| MAUI | `VeloxDev.MAUI` | `dotnet add package VeloxDev.MAUI` |
| WinForms | `VeloxDev.WinForms` | `dotnet add package VeloxDev.WinForms` |
| Razor / Blazor | `VeloxDev.Razor` | `dotnet add package VeloxDev.Razor` |

## 核心库（选装）

框架无关，如需智能体和序列化功能则额外安装：

```shell
dotnet add package VeloxDev.Core.Extension
```
