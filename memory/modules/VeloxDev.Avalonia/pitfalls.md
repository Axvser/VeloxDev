# VeloxDev.Avalonia — 坑

> 与 [architecture.md](architecture.md) / [extension.md](extension.md) 配套。
> 依据一律是树里的 `文件:行`，读记忆的 agent 能当场核。

---

## 一、attached 属性的处理函数在 **XAML 装载途中**就会跑

**症状**：demo 启动即崩，`System.InvalidOperationException: Could not find parent name scope.`，栈是

```
ControlExtensions.FindControl[T] → WorkflowSurfaceBehavior.ResolveNamedControls(:481)
  → Refresh(:136) → SetLinkMenuKey(:134)
  → Demo.WorkflowView.!XamlIlPopulate(...)   WorkflowView.axaml:21
```

**机制**：Avalonia 的 XAML 加载器把 attached 属性**一条一条**写上去，而**父命名作用域要等装载完成才挂上**。这些属性里只有 `LinkMenuKey` 有变更处理函数（`Attached/Workflow/WorkflowSurfaceBehavior.cs:89` 的 `AddClassHandler`），而它被写在**最后一条**（7 家模板与全部 demo 都是这个顺序）—— 于是它触发重查时，前面的 `ScrollViewerName` / `CanvasName` / `GridDecoratorName` / `MinimapOverlayName` / `PointerPressSourceName` **已经全都写上了**，`ResolveNamedControls` 于是真的去 `FindControl`，而那时作用域还没有。

**为什么别的那几个属性没事**：它们没有处理函数，所以装载途中根本不会重查 —— 这不是设计，是巧合。

**换一下属性顺序就会从 `IsEnabled` 那条路同样炸**（`:86` 的 `OnIsEnabledChanged` → `Attach` → `Refresh`）。所以修法在**解析那一层**，不是某一个处理函数上：`FindNamed<T>`（`:539` 附近）对「还没有命名作用域」返回 `null`。

**返回 null 不是吞异常**：控件挂上可视化树（`AttachedToVisualTree`，`:463`）与数据上下文变化（`:475`）两条路都会再 `Refresh` 一次，那时作用域已经在。所以「还没有」会自己变成「有了」。

**为什么用 try/catch 而不是探测某个 API**：那条路不依赖 Avalonia 11 与 12 之间的差异，而**两家版本本来就不同源**（见下）。

---

## 二、demo 与适配器的 Avalonia 版本不同源（未修）

| | 钉的版本 |
| --- | --- |
| `Src/Adapters/VeloxDev.Avalonia/VeloxDev.Avalonia.csproj:15` | **11.1.0** |
| `Examples/Workflow/Avalonia/Demo/Demo.csproj:17-20` | **12.0.1** |

NuGet 按最高版本解析，所以适配器是**用 11 编译、跑在 12 上**的 —— `Examples/` 里跑的是这条组合，而 `architecture.md:98` 与 `extension.md:73` 记的平台锚点只有适配器那一处。**升级 Avalonia 时要连同 demo 一起看**，否则「实测」的那条结论是在另一个大版本上量的。

这条**不是**上面那个崩溃的原因（顺序问题在 11 上同样存在），但它让「适配器支持哪些 Avalonia」这件事在仓库里没有唯一答案。
