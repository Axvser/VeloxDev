# VeloxDev.WinForms.Xaml ↔ Easel 抽取

> Easel 独立仓：`E:\VisualStudio\Projects\Easel`。抽取前备份为 `Easel-backup-20261124`。

## 现状

- **运行时**：`Src/Easel` 是 VeloxDev `Src/Adapters/VeloxDev.WinForms/Xaml` 的完整副本；命名空间映射 `VeloxDev.WinForms.Xaml` → `Easel`、`...Xaml.Controls` → `Easel.Controls`、`...Xaml.Visuals` → `Easel.Visuals`、`...Visuals.Controls[.Shapes]` → `Easel.Visuals.Controls[.Shapes]`。Easel 运行时**不引用 VeloxDev.Core**；只有 net461 额外引 `System.ValueTuple`。
- **生成器**：`Src/Generators/Easel.Generator` 是 `VeloxDev.WinForms.Generator` 的副本，诊断 ID 为 `EASEL_XAML*` / `EASEL_XAMLP*`，unprefixed 类型按 Easel 命名空间解析。
- **测试/示例**：`Src/Test/Unit/Easel.Test`、`Examples/XamlGallery` 已复制并改名。Easel 示例去掉了 Workflow graph 与 VeloxDev MVVM 卡片及对应依赖；Easel 测试去掉了依赖过渡引擎的动画行为测试（那些留在 VeloxDev 适配器侧）。
- **动画契约**：`Easel/Animation/` 定义 `IEase` / `Eases` / `AnimationSpec` / `IVisualAnimator` / `AnimationRuntime.Animator`。`Easel.Visuals.VisualTransition` 只是门面，不实现引擎；没有注入适配器时 `AnimationRuntime` 的 `NullVisualAnimator.CanAnimate` 返回 false，`VisualTransition` 抛出明确的 `NotSupportedException`。`TransitionAction` / `KeyFrameAnimation` 仍留在 `Easel.Visuals`，调用门面。
- **VeloxDev 适配器**：`Src/Adapters/VeloxDev.WinForms/XamlAnimation/EaselVisualAnimator.cs` 实现 `IVisualAnimator`，把 Easel DP 包成 `ITransitionProperty`，用 `Easel.BindingPriority.Animation` 写帧、收尾落 `LocalValue`，`EaseBridge` 把 `IEase` 接到 `IEaseCalculator`；模块初始化器在程序集加载时赋值 `AnimationRuntime.Animator`。
- **条件引用**：`VeloxDev.WinForms.csproj` 用 `MSBuildThisFileDirectory` 推导同级 Easel 路径；`Exists('$(EaselProject)')` 决定是否 `ProjectReference` 与是否编译 `XamlAnimation/**`。没有同级 Easel 时 VeloxDev 构建保持原样。
- **验证**：Easel 解决方案（运行时/生成器/测试/示例）构建通过，Easel.Test 348 通过；示例 `--smoke`/`--capture` 正常；独立探针加载 VeloxDev.WinForms 后 `AnimationRuntime.Animator` 为 `EaselVisualAnimator`，Easel `Border.Width` 10→100 动画完成。

## 未完成

- VeloxDev 自身的 XAML 运行时尚未切到 Easel；当前适配器只对同时引用 Easel 的宿主生效。
- 发布形态仍是同级 `ProjectReference`；VeloxDev.WinForms 对 Easel 的包依赖（Easel NuGet）尚未建立。
