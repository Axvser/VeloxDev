# Other improvements

---

## 1. AgentHelper Default Model Switching

> `Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper/AgentHelper.cs`

Change the default AI model from `"qwen-plus"` to **`"deepseek-v4-flash"`**.

```diff
- }).GetChatClient(string.IsNullOrWhiteSpace(Model) ? "qwen-plus" : Model)
+ }).GetChatClient(string.IsNullOrWhiteSpace(Model) ? "deepseek-v4-flash" : Model)
```

This change makes the Workflow example project default to the DeepSeek model, improving response speed and reasoning capability.

---

## 2. Project Configuration Cleanup

### VeloxDev.Core.csproj

| Change | Description |
| --- | --- |
| Version | `5.4.0` → `5.5.1` |
| Generator Dependency | `VeloxDev.Core.Generator` `5.4.0` → `5.5.1` |
| **Removed** `UserSecretsId` | Clear `db10839f-...` configuration item | `UserSecretsId` is usually not needed in NuGet package projects; removing it avoids unnecessary secret warnings.

### VeloxDev.Core.Generator.csproj

| Change   | Description         |
| -------- | ------------------- |
| Version  | `5.4.0` → `5.5.1` |

### Examples/Workflow/Common/Lib/Lib.csproj

| Change | Description |
| --------------------------------- | ------------------------ |
| `Microsoft.Bcl.AsyncInterfaces` | `10.0.6` → `10.0.7` |

---

## 3. File Change Summary

| File | Change Type | Line Changes |
| ---- | ----------- | ------------ |
| `Examples/Workflow/Common/Lib/Lib.csproj` | ✅ Modified | +1 / -1 |
| `Examples/Workflow/Common/Lib/.../AgentHelper.cs` | ✅ Modified | +1 / -1 |
| `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj` | ✅ Modified | +3 / -4 |
| `Src/Generators/.../VeloxDev.Core.Generator.csproj` | ✅ Modified | +1 / -1 |
| `Src/Core/VeloxDev.Core/DynamicTheme/ThemeCache.cs` | 🆕 Added | — |
| `Src/Core/VeloxDev.Core.Test/.../MonoBehaviourManagerTests.cs` | 🆕 Added | — |
| `Src/Core/VeloxDev.Core.Extension/Agent/MCP/McpScope.cs` | 🆕 Added | — |
| `Src/Core/VeloxDev.Core.Extension/Agent/MCP/McpServerConfiguration.cs` | 🆕 Added | — |
| `Src/Core/VeloxDev.Core.Extension/Agent/MCP/McpServerRunMode.cs` | 🆕 Added | — |
| `Src/Core/VeloxDev.Core.Extension/.../AgentConfirmationResult.cs` | 🗑️ Cleared | -3 |

---

## 4. New untracked files

The following files are not under version control:

| 文件                                                                  | 说明                       |
| --------------------------------------------------------------------- | -------------------------- |
| `Src/Core/VeloxDev.Core/DynamicTheme/ThemeCache.cs`                 | Theme centralized cache class           |
| `Src/Core/VeloxDev.Core.Test/TimeLine/MonoBehaviourManagerTests.cs` | AsyncLoop override mechanism unit test |