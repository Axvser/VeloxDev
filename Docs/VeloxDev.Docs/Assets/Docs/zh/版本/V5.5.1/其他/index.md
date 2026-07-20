# 其他改进

---

## 1. AgentHelper 默认模型切换

> **文件：** `Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper/AgentHelper.cs`

将默认 AI 模型从 `"qwen-plus"` 切换为 **`"deepseek-v4-flash"`**。

```diff
- }).GetChatClient(string.IsNullOrWhiteSpace(Model) ? "qwen-plus" : Model)
+ }).GetChatClient(string.IsNullOrWhiteSpace(Model) ? "deepseek-v4-flash" : Model)
```

此变更使 Workflow 示例项目默认接入 DeepSeek 模型，提升响应速度和推理能力。

---

## 2. 项目配置清理

### VeloxDev.Core.csproj

| 变更                             | 说明                                               |
| -------------------------------- | -------------------------------------------------- |
| 版本号                           | `5.4.0` → `5.5.1`                             |
| 生成器依赖                       | `VeloxDev.Core.Generator` `5.4.0` → `5.5.1` |
| **移除** `UserSecretsId` | 清除`db10839f-...` 配置项                        |

> `UserSecretsId` 在 NuGet 包项目中通常不需要，移除后避免不必要的 secret 警告。

### VeloxDev.Core.Generator.csproj

| 变更   | 说明                   |
| ------ | ---------------------- |
| 版本号 | `5.4.0` → `5.5.1` |

### Examples/Workflow/Common/Lib/Lib.csproj

| 变更                              | 说明                     |
| --------------------------------- | ------------------------ |
| `Microsoft.Bcl.AsyncInterfaces` | `10.0.6` → `10.0.7` |

---

## 3. 文件变更汇总

| 文件                                                                     | 变更类型  | 行数变化 |
| ------------------------------------------------------------------------ | --------- | -------- |
| `Examples/Workflow/Common/Lib/Lib.csproj`                              | ✅ 修改   | +1 / -1  |
| `Examples/Workflow/Common/Lib/.../AgentHelper.cs`                      | ✅ 修改   | +1 / -1  |
| `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj`                          | ✅ 修改   | +3 / -4  |
| `Src/Generators/.../VeloxDev.Core.Generator.csproj`                    | ✅ 修改   | +1 / -1  |
| `Src/Core/VeloxDev.Core/DynamicTheme/ThemeCache.cs`                    | 🆕 新增   | —       |
| `Src/Core/VeloxDev.Core.Test/.../MonoBehaviourManagerTests.cs`         | 🆕 新增   | —       |
| `Src/Core/VeloxDev.Core.Extension/Agent/MCP/McpScope.cs`               | 🆕 新增   | —       |
| `Src/Core/VeloxDev.Core.Extension/Agent/MCP/McpServerConfiguration.cs` | 🆕 新增   | —       |
| `Src/Core/VeloxDev.Core.Extension/Agent/MCP/McpServerRunMode.cs`       | 🆕 新增   | —       |
| `Src/Core/VeloxDev.Core.Extension/.../AgentConfirmationResult.cs`      | 🗑️ 清空 | -3       |

---

## 4. 未跟踪的新文件

以下文件尚未纳入版本控制：

| 文件                                                                  | 说明                       |
| --------------------------------------------------------------------- | -------------------------- |
| `Src/Core/VeloxDev.Core/DynamicTheme/ThemeCache.cs`                 | 主题集中式缓存类           |
| `Src/Core/VeloxDev.Core.Test/TimeLine/MonoBehaviourManagerTests.cs` | AsyncLoop 覆盖机制单元测试 |