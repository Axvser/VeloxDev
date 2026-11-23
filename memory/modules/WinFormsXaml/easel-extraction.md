# VeloxDev.WinForms.Xaml ↔ Easel 抽取

> Easel 独立仓：`E:\VisualStudio\Projects\Easel`。抽取前备份为 `Easel-backup-20261124`。

## 现状

- **运行时**：`Src/Easel` 是 VeloxDev `Src/Adapters/VeloxDev.WinForms/Xaml` 的完整副本；命名空间映射 `VeloxDev.WinForms.Xaml` → `Easel`、`...Xaml.Controls` → `Easel.Controls`、`...Xaml.Visuals` → `Easel.Visuals`、`...Visuals.Controls[.Shapes]` → `Easel.Visuals.Controls[.Shapes]`。Easel 运行时**不引用 VeloxDev.Core**；只有 net461 额外引 `System.ValueTuple`。
- **生成器**：`Src/Generators/Easel.Generator` 是 `VeloxDev.WinForms.Generator` 的副本，诊断 ID 为 `EASEL_XAML*` / `EASEL_XAMLP*`，unprefixed 类型按 Easel 命名空间解析。
- **测试/示例**：`Src/Test/Unit/Easel.Test`、`Examples/XamlGallery` 已复制并改名。Easel 示例去掉了 Workflow graph 与 VeloxDev MVVM 卡片及对应依赖；Easel 测试去掉了依赖过渡引擎的动画行为测试（那些留在 VeloxDev 适配器侧）。
- **动画契约**：`Easel/Animation/` 定义 `IEase` / `Eases` / `AnimationSpec` / `IVisualAnimator` / `AnimationRuntime.Animator`。`Easel.Visuals.VisualTransition` 只是门面，不实现引擎；没有注入适配器时 `AnimationRuntime.Animator` 是 `NullVisualAnimator`：`CanAnimate` 返回 false，启动动画（`VisualTransition` / 触发器动作）抛出明确的 `InvalidOperationException`。`TransitionAction` / `KeyFrameAnimation` 仍留在 `Easel.Visuals`，调用门面。
- **VeloxDev 适配器**：`Src/Adapters/VeloxDev.WinForms/XamlAnimation/EaselVisualAnimator.cs` 实现 `IVisualAnimator`，把 Easel DP 包成 `ITransitionProperty`，用 `Easel.BindingPriority.Animation` 写帧、收尾落 `LocalValue`，`EaseBridge` 把 `IEase` 接到 `IEaseCalculator`；模块初始化器在程序集加载时赋值 `AnimationRuntime.Animator`。
- **条件引用**：`VeloxDev.WinForms.csproj` 用 `MSBuildThisFileDirectory` 推导同级 Easel 路径；`Exists('$(EaselProject)')` 决定是否 `ProjectReference` 与是否编译 `XamlAnimation/**`。没有同级 Easel 时 VeloxDev 构建保持原样。
- **验证**：Easel 解决方案（运行时/生成器/测试/示例）构建与测试通过；示例 `--smoke`/`--capture` 正常；独立探针加载 VeloxDev.WinForms 后 `AnimationRuntime.Animator` 为 `EaselVisualAnimator`，Easel `Border.Width` 10→100 动画完成。

## Easel 新增能力（VeloxDev 的 Xaml 副本尚未同步）

- **3D 变换**：`Transform3D` 家族（`Rotate3DTransform` / `Scale3DTransform` / `Translate3DTransform` / `MatrixTransform3D` / `Transform3DGroup`）挂在 `Visual.Transform3D` 与 `VisualElement.Transform3DProperty` 上；`Transform3D.cs` 自带 `Matrix4x4` 与单应性 `Projection3D`，不依赖 `System.Numerics`。渲染先合成子树，再用逆单应性逐像素双线性采样贴回（边缘 2×2 覆盖），不做 N×N 仿射近似；单轴 >4096px 或目标 >16M 像素时退回分格仿射。命中、`AbsoluteBounds` 与失效走同一逆映射。`Depth<=0` 视为正交。`CacheAsBitmap` 缓存投影前的层，只改角度/深度/透明度时不重绘子树；改 `Transform3DProperty` 时由父级重绘，旧投影区域才不会留残影。
- **合成遮罩**：`Visual.OpacityMask` / `VisualElement.OpacityMaskProperty` 用 `Visuals/OpacityMaskCompositor.cs` 乘合成位图；`OpacityMaskMode.Alpha` 取笔刷 alpha，`OpacityMaskMode.Luminance` 取预乘遮罩的亮度×alpha。承载 `ContainerVisual` 的 `VisualPresenter` 或舞台要 `ClipToBounds`，旋转/变形才不会在包围盒外留下旧帧。
- **渐变文字**：`Brush.CreateNativeBrush` 把 Solid/Linear/Radial 笔刷交给 `Graphics.DrawString`；`TextBlock` / `Button` / `TextBox` / `ComboBox` / `TextVisual` 都走这条路径，文本不再压成首色。多行 TextBox 每行单独铺渐变。
- **系统主题**：`ThemeMode.System` 是运行时模式；`Theme.EffectiveMode` 解析为 Dark/Light/HighContrast。读取 `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme`（P/Invoke `RegGetValueW`，避免 Registry 包依赖），`SystemInformation.HighContrast` 优先；`SystemEvents.UserPreferenceChanged` 事件投递到选模式时捕获的 `SynchronizationContext`，再 `RefreshSystemTheme()` 换调色板并触发 `Changed`。
- **资源引用**：`{StaticResource}` 只解析一次；`{DynamicResource}` 持续跟随字典增删改，并在资源消失时清回 DP 默认值。同一目标属性上的多个资源引用按“后写接管”处理：后到的静态引用会停掉先前的动态订阅，后到的动态引用重新接管。实现是 `VisualElement` 的 `RegisterResourceBinding(targetToken, binding)`；生成器把 DP 引用（或 CLR 属性字符串）作为 token 传给 `ApplyStatic/BindDynamic`。DynamicResource 仍只支持视觉/附加属性，Style/Trigger Setter 的值仍按声明期解析。
- **GridSplitter**：按目标在 sash 的哪一侧决定 delta 符号（左/上不反转，右/下反转）；起始尺寸取 `Target.Bounds` 的已安排宽高，不用 `Width`/`Height`（Auto 时它们是 NaN）。横向/纵向都有回归测试。
- **XAML**：`RectangleF` / `Rectangle` 字面量（`XamlRuntime.ParseRectangleF/ParseRectangle`，生成器 `IsRuntimeConvertedType`）；对 `Foreground` 这类 `Color` 访问器背后的 Brush DP，`StaticResource` 改用 `SetValue`，渐变笔刷能落到文本前景。
- Easel 测试覆盖 3D 旋转/命中/失效、遮罩与渐变 XAML；VeloxDev 侧 `Src/Adapters/VeloxDev.WinForms/Xaml` 没有这些代码。

## Gallery（`Easel/Examples/XamlGallery`）

- 外壳是 `Views/GalleryVisualShell.xaml` + `GalleryShellViewModel`；页面内大标题是 **Modern WinForms Gallery**，原生窗口标题仍是 `Easel Demos`。目录根节点写作 `Demos`，`Scenarios` 组是 7 个整页（dashboard / settings / player / chat / gradients / composition / 3d），其余为组件组。
- 主题选择是四选一 ComboBox：Dark+ / Light+ / High contrast / Follow system。`ThemeMode.System` 解析 Windows 的 `AppsUseLightTheme`（高对比时用 HighContrast），并用 `SystemEvents.UserPreferenceChanged` 在选模式的那个线程上重读；`Theme.EffectiveMode` 给出实际调色板。
- 兜底主题是库内的 `Src/Easel/Themes/{DarkTheme,LightTheme}.xaml`，根是 WPF 式 `<ResourceDictionary>`：调色板 Brush、`CornerRadius`/`ControlHeight` 与四个排版样式（Heading/Subheading/Body/Caption）。字体设置两者相同但每个文件都完整写一遍；`Theme.CreateDark/Light` 从这些 XAML 资源读回 `ThemePalette`，`Theme.CreateResourceDictionary(mode)` 给应用一份副本。High contrast 不是库内兜底：`Demo/Themes/HighContrastTheme.xaml` 是自定义主题示例，`ThemeService` 把它并入视图并用 `ThemePalette.FromResourceDictionary` 设为当前调色板；运行时仍有 `ThemeMode.HighContrast` 的 C# 调色板。生成器现在支持 `<ResourceDictionary>` 根、`x:Key` 资源、keyed Style/Template 与 `MergedDictionaries`（编译类型或内联；不支持文件 URI `Source`）。
- 每张卡是 `Views/Cards/*.xaml`；code-behind 只做 `DataContext`、树节点、弹层、日历/日期、位图、滑块接线。`Catalog/Cards/<Group>Cards.cs` 按组拼卡，`Catalog/CardCatalog.cs` 的顺序就是目录顺序。Styling 组有 `DynamicResource`/`StaticResource` 对比卡（`style.dynamic-resource`）。
- 整页固定 540 宽；`PreviewScroll` 与页内 `ScrollViewer` 横向禁用，窗口默认取工作区尺寸。3D/遮罩在 `graphics.*` 与 `scenario.*` 卡里演示。
- 生成器把 Style/Trigger Setter 的 `{StaticResource}` 装成 `Get<object>`，由 `SetValue` 转成 DP 类型；写成 `Get<Color>` 会让 Brush 主题资源在 `Foreground` 上转换失败。

## 未完成

- VeloxDev 自身的 XAML 运行时尚未切到 Easel；当前适配器只对同时引用 Easel 的宿主生效。
- 发布形态仍是同级 `ProjectReference`；VeloxDev.WinForms 对 Easel 的包依赖（Easel NuGet）尚未建立。
