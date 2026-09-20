# WeakTypes — 架构

> 代码：`Src/Core/VeloxDev.Core/WeakTypes/`（4 个 .cs，命名空间 `VeloxDev.WeakTypes`）。
> 测试：`Src/Core/VeloxDev.Core.Test/WeakTypes/`（4 个文件，35 个 `[TestMethod]`：WeakDelegate 8 / Queue 10 / Stack 10 / Cache 7）。
> **无平台轴** —— 4 个文件只 `using System.*`（`WeakReference<T>`、`ConditionalWeakTable`、`Queue`/`Stack`、`System.Collections`），不引用任何 GUI，所以本模块没有 `adapters/`。
> 公开文档**不在本仓库里**（`Docs/` 那套文档站点，`.gitignore:461` 排除，`git ls-files Docs/` 为空），它与本仓库各自独立、不随本模块的改动一起记账。

本文只写「读完这 4 个文件才知道的东西」。类型清单、成员表、继承树请看 IDE。

---

## 一、四个容器弱化了什么（以及没弱化什么）

四者都**不是**「内容随手就能被回收」的容器 —— 每个的「弱」都只落在一侧，另一侧照旧是强引用。这张表是本模块的主轴：

| 容器 | 弱的是 | 强引用/被谁钉住 | 谁清扫、什么时候 | 生产消费者 |
|---|---|---|---|---|
| `WeakDelegate<TDelegate>` | 只有账本 `List<WeakReference<Delegate>>`（`WeakDelegate.cs:23`） | **`_combinedDelegate`（`:22`，`volatile`）把组合出来的每个 handler 都钉住**；它由 `Delegate.Combine` 拼成（`:88`） | 只在 `RebuildCache()`（`:79-93`）里，即 `AddHandler`/`RemoveHandler` 且 `CanUpdateCache:true`、缓存为 null 时的 `GetInvocationList()`、`Clone()` | `Src/Core/VeloxDev.Core/TransitionSystem/TransitionEffect.cs:45-53`（9 个事件字段）—— **全仓唯一** |
| `WeakCache<TTargetKey,TCacheKey>` | 键：`ConditionalWeakTable<TTargetKey,TCacheKey>`（`WeakCache.cs:7`） | **值**（`TCacheKey`）由 CWT 保活至键死；另有 `_targets`（`:8`）作可枚举索引，它才是 `ForeachCache` 真正遍历的东西 | 两处：`ForeachCache` 每次调用都全清（`:18`）；`AddOrUpdate` 按计数器阈值定期清（`:48-53`） | **零**（只有测试，见 §二） |
| `WeakQueue<T>` | 值：`Queue<WeakReference<T>>`（`WeakQueue.cs:7`） | 无强引用侧 | 访问即清扫（`Count`/`TryDequeue`/`TryPeek`/`TrimExcess`/`GetEnumerator`） | **零** |
| `WeakStack<T>` | 值：`Stack<WeakReference<T>>`（`WeakStack.cs:7`） | 无强引用侧 | 同上（`TryPop` 代替 `TryDequeue`） | **零** |

**共同前提：清扫永远是"事后清尸"。** 弱引用只在 GC 之后才可能变 null，所以四种容器都不可能"提前"知道某个条目已经没用了；每一条清扫路径都是「读的时候顺便把已经死的删掉」。

**`WeakCache` 为什么需要第二个索引**：`ForeachCache`（`:14-30`）遍历的是 `_targets` 而不是 CWT。`ConditionalWeakTable` 在 `netstandard2.0` 上不可枚举，而 `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:5` 把 `netstandard2.0` 列在第一位目标里，所以索引不是冗余，是必需 —— **代码里没有一行注释解释这件事**，只能从 TFM 推。

---

## 二、谁在用它（本模块最承重的一条）

| 容器 | 生产调用点 | 结论 |
|---|---|---|
| `WeakDelegate<TDelegate>` | `Src/Core/VeloxDev.Core/TransitionSystem/TransitionEffect.cs:45-53`（9 个字段）+ 该文件的访问器 `:61-105`、`InvokeXxx` `:107-147`、`Clone()` `:149` | 唯一的生产消费者，但用得很深：9 个事件全走它 |
| `WeakCache` / `WeakQueue` / `WeakStack` | **0 处**（除自身文件与测试） | 见下 |

`WeakCache`/`WeakQueue`/`WeakStack` 在全仓（`Src`、`Examples`、`Templates`、根目录，含 `.cs`/`.razor`/`.xaml`/`.md`）**没有任何生产构造点**；`VeloxDev.WeakTypes` 这个 `using` 只出现在 `TransitionEffect.cs:4` 与 4 个测试文件里。

**但它们不是死代码**，读的时候不要按死代码处理：

- 它们在 `VeloxDev.Core` 包里以 public API 发布（`<Version>9.0.0</Version>`，`Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:14`），删改是破坏性变更；
- 反面：**改动 `WeakDelegate` 的真实爆炸半径只有 `TransitionEffect.cs` 一个文件**，而改动另外三个的爆炸半径是「所有包使用者」。

### 测试实际覆盖到哪

| 事实 | 依据 |
|---|---|
| 测试**从未**断言"目标已死 → 条目被清掉" | 4 个测试文件里 `GC.` 只出现 7 次（`WeakCacheTests.cs:87` 是 `GC.KeepAlive`；`WeakDelegateTests.cs:31-33` 是 `Collect`×2 + `WaitForPendingFinalizers`；`:112`/`:118` 是 `GetAllocatedBytesForCurrentThread`；`:120` 是 `KeepAlive`） |
| Queue/Stack/Cache 测试全用 `string` 字面量或显式保活的 `object` | Queue/Stack 一律 `Enqueue("hello")` 之类；`WeakCacheTests.cs:76-88` 用 20 个 `new object()` 并 `GC.KeepAlive(keys)` 收尾 |
| 字符串字面量被驻留 → 永不回收 | 上面那条的后果 —— 测试喂进容器的全是字面量（`Enqueue("hello")` 之类），字面量被驻留，等于永远有一条强引用 |
| 唯一断言回收方向的测试断言的是**相反**方向 | `Src/Core/VeloxDev.Core.Test/WeakTypes/WeakDelegateTests.cs:27-37`：`GC.Collect()` 后 `Assert.IsTrue(weak.IsAlive)` |
| 唯一的性能断言 | `WeakDelegateTests.cs:102-122`：100 对 `GetInvocationList()?.Invoke()` 前后 `GC.GetAllocatedBytesForCurrentThread()` 必须相等（走 `DynamicInvoke` 的 `Invoke(object?[])` 会自己分配，掩盖测量） |

所以想验证"弱"这一侧，只能自己写：在 helper 方法里造对象、`Release` 之后静默 GC，且**不挂调试器**（调试器会延长对象寿命，回收不可复现），**仓内没有现成先例**。

---

## 三、四种清扫触发，逐个说到成员

### `WeakDelegate` —— 清扫与"重建"是同一次操作

`CleanupCollectedHandlers()`（`:95-104`）**只被 `RebuildCache()` 调用**（`:81`），没有别的入口。于是：

- `AddHandler`/`RemoveHandler` 的 `CanUpdateCache` 默认 true → 立刻重建 → 立刻清扫；
- `GetInvocationList()` 只在 `_combinedDelegate == null` 时进锁重建（`:58-66`）→ 缓存非空时它一次也不清扫；
- `Clone()`（`:106-121`）逐个取活 handler 塞进新对象（`CanUpdateCache: false`），最后 `value._combinedDelegate = value.GetInvocationList();`（`:118`）补上缓存。

`CanUpdateCache: false` 的语义要按代码读：handler **照样进弱账本**（`:31`），只是这次不重建；只要此后发生任何一次重建，它就会被 `Delegate.Combine` 拼进强缓存，从此不再可回收。**它是延迟，不是豁免**（详见 `extension.md`）。

### `WeakCache` —— 计数阈值 + 每次遍历全清

```
AddOrUpdate: if (_counter > _perceptionThreshold) { _targets.RemoveAll(dead); _counter = 0; _perceptionThreshold = GetNextCleanupThreshold(_targets.Count); }   // :48-53
GetNextCleanupThreshold: (count == 0 ? 4 : count * 2) * 0.9 取整   // :75-79
```

- 初值 `_perceptionThreshold = 4`（`:12`），**是一个 public 字段**，外部可随时改，而 `_counter` 私有 —— 所以外部能把清理周期压到每 1 次插入一次，或抬到永不触发。
- 阈值自适应但**按清理后的存活数**算，且 `* 0.9` 是整数截断：`_targets.Count == 20` 时下一次阈值是 `36`。
- `ForeachCache` 每次调用都 `RemoveAll` 死键（`:18`），**但不重置 `_counter` 也不改 `_perceptionThreshold`** —— 两条清理路径的账本不共享。

### `WeakQueue` / `WeakStack` —— 访问即清扫，`Prune()` 是 O(n) 重建

`Prune()`（`WeakQueue.cs:124-135`、`WeakStack.cs:124-136`）把存活项 `Where(...).ToList()` 后 `Clear()` 再逐个装回去 —— **每次触发都分配一次 List**。触发点：

| 成员 | 行为 |
|---|---|
| `Count`（`WeakQueue.cs:10-22`） | 先锁外读 `_references.Count == 0` 快速返回 0（`:14`），否则进锁 `Prune()` 再返回 |
| `TryDequeue`/`TryPop`、`TryPeek` | 用 `while` 循环把队首/栈顶的死条目**逐个丢弃**直到碰到活的（`:63-96`），不是整体 Prune |
| `TrimExcess` | `Prune()` + `_references.TrimExcess()` |
| `GetEnumerator` | `Prune()` 后在**锁内** `yield return`（`WeakQueue.cs:107-120`，Stack 同构 `:107-120`） |

两处 `Reverse()` 的理由**不同**，改的时候别合并：`WeakStack.PushRange` 先反转入参（`WeakStack.cs:51`）是为了让「后传进来的在栈顶」，而 `Prune` 里反转存活列表（`:129`）是为了重建后栈序不变。

---

## 四、线程安全边界

| 成员 | 锁 | 锁外读 | 备注 |
|---|---|---|---|
| `WeakDelegate.GetInvocationList()` | 命中缓存时**完全不进锁** | `_combinedDelegate`（`volatile`，`:22`/`:58`） | 全模块唯一的无锁快路径，`InvokeXxx` 每帧走这里 |
| `WeakDelegate` 其余成员 | 单把 `_lock`（`:24`） | 无 | `Clone()` 在自己的锁内调用**新对象**的 `GetInvocationList()`（`:118`），两把不同的锁，不构成死锁 |
| `WeakQueue.Count` | 有 | `_references.Count`（`:14`，非 volatile） | 快速路径把"空"直接判定为 0；"非空"进锁重算，所以只会多做一次 Prune，不会漏报 |
| `WeakQueue`/`WeakStack` 其余成员 | 单把 `_lock` | 无 | 唯一在锁外做的写前校验是 `Enqueue`/`Push` 的 null 检查 |
| `WeakCache` 全部成员 | 单把 `_lock` | 无 | `_perceptionThreshold` 的读写在锁内，但字段 public → 外部改它时**不受锁保护** |

**迭代期间改容器会抛。** `lock` 对同一线程可重入，所以下面的写法能拿到锁、然后当场抛 `InvalidOperationException`（集合已修改）：

- `WeakCache.ForeachCache` 的回调里调 `AddOrUpdate`/`Remove` → `_targets` 在 `foreach` 途中被改（`:19` 的 `foreach` vs `:57`/`:60` 的 `RemoveAll`/`Add`）；
- `WeakQueue`/`WeakStack` 的 `foreach` 里调 `Enqueue`/`Push`/`Clear` → `_references` 在 `foreach` 途中被改（`WeakQueue.cs:112` vs `:40`/`:30`）。

---

## 五、反直觉（带依据）

1. **`WeakDelegate` 的名字只描述账本，不描述行为。** 类型自己的 remarks 写得很清楚：`Handlers are kept alive`（`WeakDelegate.cs:7`），并解释为什么必须如此（`effect.Update += (_,_) => …` 这种写法造出的委托没有别的引用，真弱存会静默失效）。**类名比行为更容易误导** —— `WeakDelegate` 这个叫法只对账本成立。
2. **`CanUpdateCache` 的参数名首字母大写**（`WeakDelegate.cs:26`/`:36`），是全模块唯一破坏 .NET 参数命名惯例的地方（仓内其它公开 API 用 `canMutualTask` 这类小驼峰）。
3. **`RemoveHandler` 会删掉所有匹配项**，不是第一个：倒序扫描且**不在删除后 break**（`:40-47`），所以同一个 handler 加两次、删一次就都没了。
4. **`AddHandler(null)` 静默返回**（`:30`），但 `WeakQueue.Enqueue(null)` / `WeakStack.Push(null)` 抛 `ArgumentNullException`（`WeakQueue.cs:36`、`WeakStack.cs:37`）。
5. **批量版容忍 null，单条版不容忍**：`EnqueueRange`/`PushRange` 跳过 null 并只把**实际入队的个数**当返回值（`WeakQueue.cs:51-58`）—— 传入 3 个含 1 个 null 返回 2。
6. **`WeakCache.AddOrUpdate` 的"更新"是删除 + 重加**（`:54-60`，因为 CWT 的 `Add` 对已存在键会抛），这也意味着更新会**把该键挪到 `_targets` 末尾**。
7. **`Invoke(object?[])` 走 `DynamicInvoke` → 自身会分配**（`:74-77`）。签名已知时必须用 `GetInvocationList()?.Invoke(sender, e)`，这是 `TransitionEffect.cs:107-147` 的写法，也是 §二那条零分配测试测量的路径。
8. **四者都是 `sealed`**，且都直接 `new()` 内部字段而非注入 —— 没有可替换的锁、比较器或 GC 钩子。

---

## 六、我要改 X，先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 事件订阅的可见性/顺序/是否保活 | `WeakDelegate.cs:26-49`（加删）、`:79-93`（重建与拼装顺序） |
| 每帧调用路径的开销 | `WeakDelegate.cs:56-67`（无锁快路径）、`:74-77`（会分配的 `Invoke`） |
| 缓存/队列/栈的清扫时机与频率 | `WeakCache.cs:44-63`（阈值触发）、`WeakQueue.cs:124-135`（访问即清扫） |
| 过渡动画的 9 个事件 | `Src/Core/VeloxDev.Core/TransitionSystem/TransitionEffect.cs:45-53`、`:61-105`、`:149` |
