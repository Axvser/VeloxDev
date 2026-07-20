# V5.5.1 版本更新

> **生成日期：** 2026-06-29
> **变更统计：** 9 个文件 | +652 行 / -188 行

---

## 📋 版本概述

本次更新涵盖 **6 大改进方向**，涉及运行时核心库、源码生成器、示例项目和全新的 MCP 集成支持。

| 方向                                                                      | 涉及文件                                | 变更量      | 重要性 |
| ------------------------------------------------------------------------- | --------------------------------------- | ----------- | ------ |
| [通道级 AsyncLoop 覆盖](01-MonoBehaviourManager通道级AsyncLoop覆盖/index.md) | `MonoBehaviourManager.cs`             | +49 / -3    | ⭐⭐⭐⭐ |
| [主题系统 ThemeCache 重构](02-主题系统ThemeCache重构/index.md)               | `Theme.cs`, `ThemeCache.cs`（新增） | +170 / -171 | ⭐⭐ |
| [MVVM 生成器多框架适配](03-MVVM生成器多框架适配/index.md)                    | `Analizer.cs`, `MVVMWriter.cs`      | +162 / -21  | ⭐⭐⭐⭐⭐ |
| [新增 MCP 支持](04-新增MCP支持/index.md)                                     | `McpScope.cs` 等 5 个文件（新增）     | +265 / -3   | ⭐⭐⭐⭐ |
| [AgentHelper 默认模型切换](05-其他改进/index.md)                             | `AgentHelper.cs`                      | +1 / -1     | ⭐     |

---

## 📖 详细目录

| #  | 章节                                                                                           | 说明                                                           |
| -- | ---------------------------------------------------------------------------------------------- | -------------------------------------------------------------- |
| 01 | [MonoBehaviourManager 通道级 AsyncLoop 覆盖](01-MonoBehaviourManager通道级AsyncLoop覆盖/index.md) | 每个通道可独立选择 async/await 或原生 Thread 驱动帧循环        |
| 02 | [主题系统 ThemeCache 重构](02-主题系统ThemeCache重构/index.md)                                    | 集中式缓存替代每个类的静态字典字段，支持继承链                 |
| 03 | [MVVM 生成器多框架适配](03-MVVM生成器多框架适配/index.md)                                         | 自动检测 CommunityToolkit.Mvvm/Prism/ReactiveUI/Caliburn.Micro |
| 04 | [新增 MCP 支持](04-新增MCP支持/index.md)                                                          | Model Context Protocol 服务发现与工具加载                      |
| 05 | [其他改进](05-其他改进/index.md)                                                                  | DeepSeek 默认模型、UserSecretsId 移除、依赖更新                |

---

## 📦 发布包版本

| NuGet 包                    | 旧版本 | 新版本          |
| --------------------------- | ------ | --------------- |
| `VeloxDev.Core`           | 5.4.0  | **5.5.1** |
| `VeloxDev.Core.Generator` | 5.4.0  | **5.5.1** |

---

*报告由系统自动生成于 2026-06-29*