# Headless adapter tests

本目录是**验证**，不是产品代码：不打包、不进任何 `PackageReference`。六个工程（共享夹具 + 五套套件）
已登记在 [`VeloxDev.slnx`](../../../VeloxDev.slnx) 的 `/Verification/Headless/` 下。与
[`Examples/Transition/AUTO TEST`](../../../Examples/Transition/AUTO%20TEST/AGENTS.md) 的分工：

| 套件 | 需要什么 | 证明什么 |
| --- | --- | --- |
| `AUTO TEST` | 交互式桌面 + 真实窗口 | 七家 demo 的**端到端**行为（UI Automation / Playwright） |
| **本目录** | 一个普通测试进程 | 适配器在**没有窗口系统**时仍遵守的契约 |

本目录补的是后者的空白：`AUTO TEST` 在 CI 里跑不了，而适配器有 39.5k 行平台代码，
此前没有任何自动化回归。这里的测试不打开窗口，因此可以在普通 CI runner 上跑。

## 今天覆盖什么

| 套件 | 平台 | 怎么做到无头 |
| --- | --- | --- |
| `VeloxDev.WPF.Headless.Tests` | WPF（23 条） | `[STATestClass]` + 代码建可视树 + 手动 `Dispatcher.PushFrame`；不 `Show()` 窗口 |
| `VeloxDev.Avalonia.Headless.Tests` | Avalonia（23 条） | `Avalonia.Headless` 的 `HeadlessUnitTestSession` + 无头 `Window` |
| `VeloxDev.WinForms.Headless.Tests` | WinForms（24 条） | `[STATestClass]` + `CreateControl()` 但不 `Show()`；`Application.DoEvents()` 泵消息 |
| `VeloxDev.Razor.Headless.Tests` | Razor/Blazor（16 条） | `bunit` 渲染组件；`IsEnabled=false` 时不 import JS 模块，直接驱动 `[JSInvokable]` 回调 |
| `VeloxDev.Jalium.Headless.Tests` | Jalium（13 条） | 沿用 Jalium 自己的无显示测试模型；`SetLoadedState` 模拟 `Window.Show` 的加载转换 |

五套套件驱动**同一棵** `TestGraph`（`VeloxDev.AdapterTestKit`），断言同一组表面契约：

1. `IsEnabled` 为 false 时 `Refresh` 是 no-op（WinForms 的 `Refresh` 不看 `IsEnabled`，该条只在前三家断言）；
2. attach 后命名部件被解析，画布变换与网格装饰器拿到相同的内容偏移；
3. 滚动写入 `TreeHelper.Viewport` 与 `Layout.ViewportOffset`，两者同源；
4. `Layout.ViewportOffset` 在 attach 后被恢复到滚动条；
5. 虚拟化的 `VisibleItems` 随视口进出节点；
6. `ViewPool` 为每个可见项建一个视图；
7. detach 后表面不再写模型（WinForms 走 `SetIsEnabled(false)` 清滚动口，不断言这一条）；
8. Avalonia 额外用真实无头输入验证 Ctrl+滚轮的缩放方向与 `[0.1, 10]` 夹取。

每个套件另有 `SurfaceContractTests`：按名字钉住七个角色与每个公共附着点/参数/静态入口。

## 怎么跑

```powershell
# 一次跑完本目录下所有套件
./Src/Verification/HeadlessAdapters/run-headless-tests.ps1

# 行覆盖率报告 + 地板闸门
./Src/Verification/HeadlessAdapters/check-coverage.ps1

# 或单独跑一家
dotnet test Src/Verification/HeadlessAdapters/VeloxDev.WPF.Headless.Tests/VeloxDev.WPF.Headless.Tests.csproj
dotnet test Src/Verification/HeadlessAdapters/VeloxDev.Avalonia.Headless.Tests/VeloxDev.Avalonia.Headless.Tests.csproj
dotnet test Src/Verification/HeadlessAdapters/VeloxDev.WinForms.Headless.Tests/VeloxDev.WinForms.Headless.Tests.csproj
dotnet test Src/Verification/HeadlessAdapters/VeloxDev.Razor.Headless.Tests/VeloxDev.Razor.Headless.Tests.csproj
dotnet test Src/Verification/HeadlessAdapters/VeloxDev.Jalium.Headless.Tests/VeloxDev.Jalium.Headless.Tests.csproj
```

## 覆盖率口径

- **契约 100%**：每个套件的 `SurfaceContractTests` 按名字钉住七个角色与每个公共附着点；改名/挪走会红。
- **行覆盖**：`check-coverage.ps1` 按适配器打印并对照地板（2026-10-23 基线）：

  | 适配器 | 行覆盖（合并采样器套件） | 地板 |
  | --- | --- | --- |
  | WPF | 1322 / 2947 = 44.9% | 44% |
  | Avalonia | 1605 / 2905 = 55.2% | 55% |
  | WinForms | 1526 / 4327 = 35.3% | 35% |
  | Razor | 747 / 1782 = 41.9% | 41% |
  | Jalium | 1006 / 2826 = 35.6% | 35% |

  地板只许上调，不许为了让红变绿往下调。字面 100% 行覆盖不是这里的验收线：多 TFM 的 `#if` 分支、
  WinUI/MAUI 的窗口路径、Jalium 的 GPU 路径、Razor 的 `.js` 都不在一次测试运行的统计里。
  采样器的覆盖来自既有的 `Examples/Transition/AUTO TEST/Samplers` 无头套件，按行号并集合并。

  已补：槽锚点、节点拖拽、转换器、小地图、事件转接、`Transition<T>` 的全部 `.Property` 重载、
  WinForms 自绘、Razor 网格/展示。下一批主要剩 `WorkflowSurfaceBehavior` 的交互分支、非 WPF/Avalonia 的
  小地图、WinForms 的 `NodeAttachment` / `LinkAttachment` / `RulerOverlayForm`。每补一类，把对应地板往上调一档。

## 加入 `VeloxDev.slnx` 的代价

它们现在会随根目录的 `dotnet test` 一起解析。代价是根命令**不再后端-only**：

- WPF 套件需要 Windows + `UseWPF`；
- Avalonia 套件需要 `Avalonia.Headless`，跨平台可跑；
- 将来加入的 WinUI/MAUI 套件还会要求对应的工作负载。

如果仍需要一条「任何机器都能跑」的后端命令，应该维护一个只含 Core 的解决方案过滤器或
`VeloxDev.Backend.slnx`，而不是把这些项目移出解决方案。

## 其余两家为什么没有无头套件

不是遗漏，是平台没有可用的无头宿主：

| 平台 | 状态 | 出路 |
| --- | --- | --- |
| WinUI | ❌ 没有真正无头模式 | 官方要求 WinUI 应用自己当测试宿主（`[UITestMethod]` + 窗口），需要 MSTest 4.5 + MTP |
| MAUI | ❌ 没有一方无窗口宿主 | 控制树夹具（如 MauiReactor `TemplateHost`）或带窗口的自托管宿主 |

这两家目前仍由 `AUTO TEST` 覆盖真实窗口那一条路；等自托管宿主落地后再按平台退役。

**不要为了「七家都绿」把不可无头的那几家硬塞进测试进程**：它们会拉进 WinUI/MAUI SDK，
让一个本可在普通 runner 上跑 2 秒的套件变成需要交互式桌面的套件 —— 这正是 `AUTO TEST`
被分出去的原因。

## 加一家可以直接测的平台

1. 新建 `VeloxDev.<GUI>.Headless.Tests`，引用 `VeloxDev.AdapterTestKit` 与对应适配器；
2. 复制一份 `WpfHeadlessSurface` / `AvaloniaHeadlessSurface`，按平台 API 换掉：
   命名解析、滚动容器、dispatcher 泵、模板选择器；
3. 断言保持与现有套件同形 —— 这样两家不一致时，失败信息直接指出是哪一家；
4. 把新项目加进 `run-headless-tests.ps1` 的 `$projects`。

**共享夹具只放与 GUI 无关的东西**（`TestGraph`）。平台差异留在各自的
`Infrastructure/` 下，不要为了复用把 WPF/Avalonia 的类型塞进 TestKit。
