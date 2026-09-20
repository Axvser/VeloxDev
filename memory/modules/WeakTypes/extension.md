# WeakTypes — 扩展

> 契约/行为的代码事实在 `architecture.md`，本文只写「要动手时走哪条路、哪条路看着能编译但是错的」。
> 代码：`Src/Core/VeloxDev.Core/WeakTypes/`（4 个 .cs），测试 `Src/Core/VeloxDev.Core.Test/WeakTypes/`（4 个文件，35 个 `[TestMethod]`）。

---

## 一、先选容器：四条判据（选错的代价是静默的）

| 你的问题 | 用 | 别用 |
|---|---|---|
| 长期存活的发布者 + 大量短命订阅者 | `WeakDelegate<TDelegate>` —— 但先读 §二·1，它**保证不了**订阅者可回收 | 普通 `event`（强引用链钉住订阅者） |
| FIFO 处理临时工作项，希望死项自己消失 | `WeakQueue<T>` | `Queue<T>`（钉住项） |
| LIFO 撤销栈，不希望栈钉住项 | `WeakStack<T>` | `Stack<T>` |
| 「每个目标一份附加数据」，目标死了数据跟着走 | `WeakCache<TTargetKey,TCacheKey>`（键弱、值随键活） | `Dictionary<TKey,TValue>`、`ConditionalWeakTable`（后者不可枚举，见下） |

`WeakCache` 与 `ConditionalWeakTable` 的差别就在**可枚举**这一条：`WeakCache` 用 `_targets` 索引换来了 `ForeachCache`（`WeakCache.cs:14-30`），代价是索引自己也要清扫（两条清扫路径，见 `architecture.md` §三）。要枚举就用它，不要枚举就直接用 BCL 的 CWT —— 别自己再包一层。

---

## 二、官方做法 vs 看着能编译、但是错的捷径

### 1. 「让订阅者可回收」：官方是退订，不是靠类型名

- **官方**：订阅者自己持有一个 delegate 变量，用完 `RemoveHandler(handler)`。类型 remarks 明说这条路：`a subscriber that needs to be collectable keeps its own reference to the delegate and unsubscribes`（`WeakDelegate.cs:12-13`）。
- **错的捷径 A**：以为 `WeakDelegate` 这个名字就意味着"订户随时会被回收"。组合委托是 `volatile` **强**字段（`WeakDelegate.cs:22`/`:88`），handler 一旦被拼进去就不可回收；测试 `Src/Core/VeloxDev.Core.Test/WeakTypes/WeakDelegateTests.cs:27-37` 断言的正是"订阅保活"。
- **错的捷径 B**：用 `AddHandler(handler, CanUpdateCache: false)` 当"永久可回收开关"。**重建一次就失效** —— `RebuildCache`（`:79-93`）只区分活/死，不记得当初怎么加的；任何 `CanUpdateCache:true` 的加删、缓存为 null 的 `GetInvocationList()`、`Clone()` 都会把它拼进强缓存。

### 2. 事件挂在**哪个实例**上，决定订阅者会不会被钉住

`static readonly` 的 owner + 长活订阅者 = 泄漏照旧（强缓存钉住 handler，owner 又永不释放）。官方是**每个视图/每次调用一个 owner 实例**：`Src/Core/VeloxDev.Core/TransitionSystem/TransitionEffect.cs:45-53` 全是实例字段，`skills/veloxdev-create-animation/SKILL.md:224` 把这条写成了规则（"An instance handler is safe exactly because the declaration is per view; on a `static readonly` one it would keep the view alive"）。反例就在测试里：`WeakDelegateTests.cs:10` 的 `Holder` 是 `static readonly`，正是为了让 `:27` 那条"保活"断言成立。

### 3. 迭代期间不要在回调/循环体里改容器 —— 锁救不了你

`lock` 对同一线程可重入，所以下面两种写法**能拿到锁**，然后当场抛 `InvalidOperationException`：

- `WeakCache.ForeachCache((k, v) => cache.AddOrUpdate(...))` —— 回调在锁内执行（`WeakCache.cs:16-29`），`_targets` 正在 `foreach`（`:19`）却被 `RemoveAll`/`Add` 改（`:57`/`:60`）；
- `foreach (var item in queue) queue.Enqueue(...)` —— `yield return` 在锁内且在 `_references` 的 `foreach` 里（`WeakQueue.cs:112-117`），`Enqueue` 会改同一个 `Queue`（`:40`）。

**官方**：先 `ToList()` 再遍历（`WeakCacheTests.cs:95` 之类的用法），或在遍历外收集要加/删的项。

### 4. 别调 `_perceptionThreshold`

它是 **public 字段**（`WeakCache.cs:12`），但：改它不受锁保护（`_counter` 私有且在锁内改）、改了也不影响 `ForeachCache` 的每次全清（`:18`）、而且它按清理后存活数自适应重算（`:52`/`:75-79`），外部写的值会在下一次清理后被覆盖。想改阈值就改字段初值那一行。

### 5. 别指望"确定性地回收"

任何容器都没有"已死"通知、没有回调、没有事件（4 个文件里 `event` 关键字 0 处，除了 `WeakDelegate` 的 `Delegate` 泛型约束）。要在确定时刻腾空间，只能显式 `Remove`（`WeakCache.cs:64`）/`Clear`（`WeakQueue.cs:26`）/`TrimExcess`。**所以别把正确性建立在 GC 时序上**（清扫永远是"事后清尸"，见 `architecture.md` §一末）。

---

## 三、新增一个容器（`WeakX<T>`）的步骤与联动清单

四个文件是同一套骨架，照抄最近的一个（`WeakQueue.cs` 或 `WeakStack.cs`，137/136 行，逐段同构）：

| 步骤 | 具体做什么 | 参照 |
|---|---|---|
| 1 | `sealed class WeakX<T> : IEnumerable<T> where T : class`，字段 = 强容器（队列/栈/字典）+ `object _lock` | `WeakQueue.cs:5-8` |
| 2 | `Count`：先锁外读底层 `Count == 0` 快速返回，否则进锁 `Prune()` 再返回；`IsEmpty => Count == 0` | `WeakQueue.cs:10-24` |
| 3 | 单条写入对 null 抛 `ArgumentNullException`；批量写入跳过 null 并返回实际入队数 | `WeakQueue.cs:34-61` |
| 4 | 读侧 `TryX(out T?)` 用 `while` 排空死条目，空了返回 false + `null` | `WeakQueue.cs:63-96` |
| 5 | `Prune()`：过滤存活 → `Clear()` → **保序**装回（栈要记得 `Reverse`） | `WeakQueue.cs:124-135`、`WeakStack.cs:124-136` |
| 6 | `TrimExcess()` = `Prune()` + 底层 `TrimExcess()`；`GetEnumerator()` 在锁内 `Prune()` 后 `yield return` | `WeakQueue.cs:98-120` |

**联动清单（漏一处通常不报错，只是少一个能力）**：

1. `Src/Core/VeloxDev.Core.Test/WeakTypes/` 加 `WeakXTests.cs` —— 现有 4 个文件的测试数分别是 8/10/10/7；**别在测试里断言回收**（字符串会被驻留），要断言就自己在 helper 方法里造对象 + `Release` 且不挂调试器（仓内没有现成先例，见 `architecture.md` §二末）。
2. 本模块记忆：`architecture.md` §一/§二的表。
3. **不必**动：七家适配器、`Src/Generators/`、`Src/Templates/` —— 本模块是纯 `System.*` 的，没有任何平台或生成器联动（这也是它与仓内其它模块最大的不同）。

---

## 四、"把 `WeakDelegate` 换成普通 `event`"要动哪几处

只有一处实现受影响，但同一文件里四处必须成套改：

| 位置 | 内容 |
|---|---|
| `Src/Core/VeloxDev.Core/TransitionSystem/TransitionEffect.cs:45-53` | 9 个 `WeakDelegate<EventHandler<TransitionEventArgs>>` 字段 |
| `:61-105` | 9 对 `add => _x.AddHandler(value); remove => _x.RemoveHandler(value);` |
| `:107-147` | 9 个 `InvokeXxx`，走 `_x.GetInvocationList()?.Invoke(sender, e)` |
| `:149` | `Clone()` 里 9 次 `_x.Clone()` |

`TransitionEffectCore<TPriorityCore>.Clone()` 是 `new`（非 virtual）且返回**基类型**，所以别顺手把它改成 virtual —— 那是另一个模块（`TransitionSystem`）的事，见 `memory/modules/TransitionSystem/`。
