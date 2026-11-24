# 插槽同步的共享调度批次

> Core 在 `Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Scheduling/`：`WorkflowLayoutBatch`（合并 + 调度 + 暂停）与 `WorkflowLayoutStamp`（指纹）。
> 适配器的 `WorkflowSlotLayoutBehavior` 只保留平台信号与一次改写；合并、去重、指纹比较都在 Core。WinForms 的 `WorkflowSlotLayoutBehavior` 不走这条批次：它在 `Sync` 里把部件中心换算到坐标宿主后写 `Anchor`。

## 一、它解决的问题

插槽锚点的写回触发点分散在多个平台事件里（整树布局 pass、尺寸变化、模型属性变化、绑定上下文变化）。Core 按树合并这些请求：同一棵树的节点共用一个批次，适配器只提供「把 `Flush` 投递到本平台 UI 线程」这一个委托。

## 二、API 与不变量

`WorkflowLayoutBatch.For(owner, post)`：
- `owner` 通常是 `INodeComponent.Parent`（树），**同一棵树的节点必须传同一棵树**才会合并；节点还没有树时退回控件自身，只合并自己的请求。
- `post` 是 `Func<Action, bool>`：**必须异步**（内联执行会在调用方返回前清空队列，什么也合并不了）；平台拒绝入队时返回 `false`，Core 会把 `scheduled` 旗标放回去，下一次请求重排。

| 成员 | 契约 |
|---|---|
| `Request(key, work)` | 强制请求；同一 key 只留一条，后到的覆盖先到的 |
| `Request(key, stamp, work)` | 指纹与「上一次跑完时记录的指纹」相同就不入队；返回 `false` 表示无需工作 |
| `Request(key, stamp, Func<bool> work)` | 同上，但 work 的返回值决定这一趟是否**认证**指纹：`false` 表示没看全（容器未实例化），指纹不记，下一趟布局重试 |
| `Schedule()` | 一次请求潮只向平台投递一次 `Flush` |
| `Flush()` | **先清空队列再执行** —— 执行期间新来的请求属于下一轮；**无论队列是否为空都先放掉 `scheduled`**；单条失败不取消其余，第一条异常在队列跑完后重抛 |
| `Cancel(key)` | 移除排队项与指纹记录；适配器在 Detach/Unloaded 时调用 |

**批次不记录可见性状态**：没有「隐藏时暂停」，Core 也不维护可见性标志。节点的虚拟化可见性来自 `TreeHelper.VisibleItems`；表面/页面是否显示只有宿主知道。

`WorkflowLayoutStamp.Capture(node, width, height, slotCount, slotSignature)` 覆盖模型几何（`Anchor`/`Size`）、视图实测尺寸、插槽控件数与槽位/控件身份哈希。**它看不见「外框没变、内部重排」的情况**（例如固定尺寸卡片的文字回流）——这类变化必须走强制请求。

**认证规则**：`SlotLayoutPipelineCore.RequestMeasure(Layout)` 用 `CountSlots` / `ComputeSlotIdentity` 报出的数量作 expected；`MeasureParts` 在 `found >= expected` 且没有「可见但未测量」的部件时返回 `true`，`Flush` 只给返回 `true` 的趟记指纹。物化/回收与排版顺序颠倒的那一趟因此不认证，下一趟布局重试，锚点不会停在 `NaN`。隐藏端口（`ISlotPartVisibility.IsVisible == false`）是稳态，照常认证。

## 三、七家怎么接的

| 平台 | 同步路径 | 异步路径 | 备注 |
|---|---|---|---|
| Avalonia | `LayoutUpdated` / `BoundsProperty` → 指纹 + `Flush`（同帧） | DataContext / 模型属性 → 强制 + `Schedule`，`DispatcherPriority.Render` | 三条路都进同一批次 |
| WPF | `LayoutUpdated` + `SizeChanged` → 指纹 + `Flush` | 其余触发 → 强制 + `Schedule`，`DispatcherPriority.Render` | `IsVisibleChanged==true` 补一次强制请求 |
| WinUI | 节点与坐标宿的 `LayoutUpdated` + `SizeChanged` → 指纹 + `Flush` | 其余 → 强制 + `Schedule`，`DispatcherQueuePriority.Normal` | `TryEnqueue` 被拒返回 `false`，队列留到下一次 |
| MAUI | Windows 原生 `LayoutUpdated`、非 Windows 托管 `SizeChanged` → 指纹 + `Flush` | 其余 → 强制 + `Schedule`，16ms 一次性 `IDispatcherTimer` | 定时器每批次一个 |
| Jalium | `SizeChanged` → 指纹 + `Flush`（同帧） | 其余触发 → 强制 + `Schedule`，`DispatcherPriority.Render` | 没有整树布局信号，同步只靠 `SizeChanged` |
| WinForms | 表面的 `LayoutUpdated` → 指纹 + `Flush`（同帧） | DataContext / 部件名 / `ItemsRebuilt` → 强制 + `Schedule`，`surface.BeginInvoke` | 无 surface / 无句柄时 `Post` 返回 `false` |
| Razor | JS 侧 `initSlotLayout` 按 `requestAnimationFrame` + `lastKey` 合并 | 不适用 | 没有走 Core 批次；它的合并点是 `veloxdev.workflow.js` 的 `measure()` |

**`Flush` 放在平台的布局信号里同步执行**（`LayoutUpdated`；Jalium 是 `SizeChanged`、WinForms 是 surface 的 `LayoutUpdated`），保住同帧保证：布局 pass 在渲染循环上，投递到 `Render`/`Low` 的 `Flush` 不保证赶在本帧渲染前（见 [avalonia.md](adapters/avalonia.md) §二.6）。`Schedule` 只服务没有布局信号的触发。

## 四、坑

- **不要给节点换 `owner`**：`For` 按引用缓存；传控件自身会让每个节点各得一个批次，只剩「自己合并自己」，跨节点合并失效。节点没有 `Parent` 时才允许退回控件。
- **指纹只是省测量的门，不是正确性门**：需要「无论如何都跑」的路径（绑定上下文换绑、重新挂上视觉树、手动 `Refresh`）必须用 `Request(key, work)` 强制；拿指纹去挡它们会让池化视图复用旧锚点。
- **`post` 必须异步且返回真实的入队结果**：WinUI 的 `TryEnqueue`、WinForms 的 `BeginInvoke` 都可能拒绝；返回 `true` 却什么都没排，队列会永远停在已排程状态。
- **`Flush` 必须无条件先放掉 `scheduled`**：投递之后、执行之前被 `Cancel(key)` 清空队列的那一趟会在这里提前返回；旗标留着，这棵树的 `Schedule` 从此全是空操作，延迟重测整条路死掉（只剩布局指纹那条）。`WorkflowLayoutBatchTests.CancelBetweenScheduleAndFlush_DoesNotWedgeThePostQueue` 钉这一条。
- **`Flush` 先清队再执行**：不要改成「执行完再清」。同步过程中写 `slot.Anchor` 会触发下游属性通知，执行完再清会把那一轮新请求一起吞掉。
- **指纹里的插槽数必须和测量同一口径**：枚举项要数**已实例化的容器**（`ContainerFromIndex` 非空），不是 `ItemCount`。数逻辑项会得到一个「看着完整、实际一个容器都没生成」的指纹，那一趟被当成测过就再也不会重试，锚点停在 `NaN`，连线整条不画。七家现在同口径：WPF/Jalium/WinUI 数已实例化容器，Avalonia 同，MAUI 数 `BindingContext is ISlotComponent` 的后代，WinForms 数带 `ISlotComponent` DataContext 的视觉元素，Razor 数缓存部件；`found >= expected` 的认证规则是第二道保险。
- **`CountSlots` 的 named 分支只看「找没找到」**，不看 `IsMeasured`：隐藏端口是稳态。
- **测试锚点**：`Src/Test/Unit/Core/VeloxDev.Core.Test/WorkflowSystem/WorkflowLayoutBatchTests.cs`（合并、指纹、认证、投递失败、执行中再请求、空趟不锁死）与 `SlotLayoutPipelineCoreTests.cs`（未实例化必须重试、稳态不空转）；七家无头套件的 `SlotLayout*` / `SurfaceContract*` 用例验证锚点仍能写回。
