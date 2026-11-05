# 插槽同步的共享调度批次

> Core 在 `Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Scheduling/`：`WorkflowLayoutBatch`（合并 + 调度 + 暂停）与 `WorkflowLayoutStamp`（指纹）。
> 适配器的 `WorkflowSlotLayoutBehavior` 只保留平台信号与一次改写；合并、去重、指纹比较都在 Core。

## 一、它解决的问题

插槽锚点的写回触发点分散在多个平台事件里（整树布局 pass、尺寸变化、模型属性变化、绑定上下文变化）。原实现每个节点各自订整树 `LayoutUpdated` 并直接 `Sync`，于是：

- 宿主每帧布局（播放、时间轴、动画）都会让画布上**每个**节点重跑一次测量，哪怕它没动；
- 同一帧里布局 + 尺寸 + 属性三条路各跑一次，同一个节点被同步多次；
- 合并状态（`SyncPending`）是每个适配器自己一份，七家行为不一致。

2026-11-06 起这些统一进 Core。同一棵树的节点共用一个批次，适配器只提供「把 `Flush` 投递到本平台 UI 线程」这一个委托。

## 二、API 与不变量

`WorkflowLayoutBatch.For(owner, post)`：
- `owner` 通常是 `IWorkflowNodeViewModel.Parent`（树），**同一棵树的节点必须传同一棵树**才会合并；节点还没有树时退回控件自身，只合并自己的请求。
- `post` 是 `Func<Action, bool>`：**必须异步**（内联执行会在调用方返回前清空队列，什么也合并不了）；平台拒绝入队时返回 `false`，Core 会把 `scheduled` 旗标放回去，下一次请求重排。

| 成员 | 契约 |
|---|---|
| `Request(key, work)` | 强制请求；同一 key 只留一条，后到的覆盖先到的 |
| `Request(key, stamp, work)` | 指纹与「上一次跑完时记录的指纹」相同就不入队；返回 `false` 表示无需工作 |
| `Schedule()` | 一次请求潮只向平台投递一次 `Flush` |
| `Flush()` | **先清空队列再执行** —— 执行期间新来的请求属于下一轮；单条失败不取消其余，第一条异常在队列跑完后重抛 |
| `Cancel(key)` | 移除排队项与指纹记录；适配器在 Detach/Unloaded 时调用 |

**批次不记录任何可见性状态**（2026-11-06 定）：R2 的「隐藏时暂停」没有实现，也不由 Core 维护可见性标志。需要时先确定事实源——节点的虚拟化可见性来自 `TreeHelper.VisibleItems`（Core 已有），表面/页面是否显示只有宿主知道，不是 Core 能识别的事实。

`WorkflowLayoutStamp.Capture(node, width, height, slotCount)` 只看模型几何（`Anchor`/`Size`）、视图实测尺寸与插槽控件数。**它看不见「外框没变、内部重排」的情况**（例如固定尺寸卡片的文字回流）——这类变化必须走强制请求；`LayoutUpdated` 这条整树信号是唯一可能漏掉它的路径，这是刻意的取舍：用一次漏同步换掉「宿主每帧 × 全部节点」的测量。

## 三、七家怎么接的

| 平台 | 同步路径 | 异步路径 | 备注 |
|---|---|---|---|
| Avalonia | `LayoutUpdated` → 指纹 + `Flush`（同帧） | `BoundsProperty` / DataContext / 模型属性 → 强制 + `Schedule`，`DispatcherPriority.Render` | 三条路都进同一批次；`Bounds` 触发的强制请求会被同帧 `Flush` 吸收 |
| WPF | `LayoutUpdated` + `SizeChanged` → 指纹 + `Flush` | 其余触发 → 强制 + `Schedule`，`DispatcherPriority.Render` | `IsVisibleChanged==true` 仍补一次强制请求 |
| WinUI | 节点与坐标宿的 `LayoutUpdated` + `SizeChanged` → 指纹 + `Flush` | 其余 → 强制 + `Schedule`，`DispatcherQueuePriority.Normal` | `TryEnqueue` 被拒返回 `false`，队列留到下一次 |
| MAUI | Windows 原生 `LayoutUpdated`、非 Windows 托管 `SizeChanged` → 指纹 + `Flush` | 其余 → 强制 + `Schedule`，16ms 一次性 `IDispatcherTimer` | 定时器从「每控件一个」变成「每批次一个」 |
| Jalium | 无 `LayoutUpdated`，全部走异步 | 强制 + `Schedule`，`DispatcherPriority.Render` | 只有合并，没有指纹门 |
| WinForms | 无布局 pass 可挂，全部走异步 | 强制 + `Schedule`，`Form.BeginInvoke`（无 Form 时退回控件） | `BeginInvoke` 抛 `InvalidOperationException` 时返回 `false` |
| Razor | JS 侧 `initSlotLayout` 已经按 `requestAnimationFrame` + `lastKey` 合并 | 不适用 | 没有走 Core 批次；它的合并点是 `veloxdev.workflow.js` 的 `measure()` |

**`Flush` 放在 `LayoutUpdated` 里同步执行**，是为了保住原来那条同帧保证：布局 pass 在渲染循环上，投递到 `Render`/`Low` 的 `Flush` 不保证赶在本帧渲染前（见 [avalonia.md](adapters/avalonia.md) §二.6）。`Schedule` 只服务那些没有布局 pass 的触发。

## 四、坑

- **不要给节点换 `owner`**：`For` 按引用缓存；传控件自身会让每个节点各得一个批次，只剩「自己合并自己」，跨节点合并失效。节点没有 `Parent` 时才允许退回控件。
- **指纹只是省测量的门，不是正确性门**：需要「无论如何都跑」的路径（绑定上下文换绑、重新挂上视觉树、手动 `Refresh`）必须用 `Request(key, work)` 强制；拿指纹去挡它们会让池化视图复用旧锚点。
- **`post` 必须异步且返回真实的入队结果**：WinUI 的 `TryEnqueue`、WinForms 的 `BeginInvoke` 都可能拒绝；返回 `true` 却什么都没排，队列会永远停在已排程状态。
- **`Flush` 先清队再执行**：不要改成「执行完再清」。同步过程中写 `slot.Anchor` 会触发下游属性通知，执行完再清会把那一轮新请求一起吞掉。
- **指纹里的插槽控件数要算上枚举容器**：只算具名插槽时，枚举插槽增删在固定外框下不会改变任何指纹字段，锚点会停在旧值。
- **测试锚点**：`Src/Core/VeloxDev.Core.Test/WorkflowSystem/WorkflowLayoutBatchTests.cs`（合并、指纹、投递失败、执行中再请求）；七家无头套件的 `SlotLayout*` / `SurfaceContract*` 用例验证锚点仍能写回。
