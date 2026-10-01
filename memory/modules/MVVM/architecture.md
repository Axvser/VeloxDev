# MVVM — 架构

> 运行期：`Src/Core/VeloxDev.Core/MVVM/`（4 个 .cs），契约在 `Src/Core/VeloxDev.Core/Interfaces/MVVM/`（只有 `IVeloxCommand.cs`）。
> 生成器：`Src/Generators/VeloxDev.Core.Generator/`。**下文不带路径的文件名都指这个目录**：`Writers/MVVMWriter.cs`、`Writers/CommandWriter.cs`、`Base/Analizer.cs`（配置读取与代码模板在它的 `MVVM*` 部分）。它们**不在本模块目录下**，别在 `MVVM/` 里找。
> 本文只写「读完这些文件才知道的东西」。类型清单、成员表、继承树请看 IDE。

---

## 一、这个模块是什么，不解决什么

**是什么。** 两件互不依赖的事被放在了同一个命名空间：

| 半边 | 归谁 | 本质 |
|---|---|---|
| 命令的**运行期语义** | `VeloxCommand.cs`（`VeloxCommand` + `CommandEventArgs` + `CommandEventType`） | 一个比 `ICommand` 重得多的实现：**上限并发 + 排队 + 强制锁 + 8 个生命周期事件 + 可取消的异步执行** |
| 集合属性的**订阅兜底** | `ObservableCollectionTracker.cs` | 一个静态去重订阅器，用来补「字段初始化器绕过 setter」这个洞 |
| 两者的**声明方式** | 生成器（`MVVMWriter` / `CommandWriter`）+ 两个特性 | `[VeloxProperty]` / `[VeloxCommand]` —— 都只是**给生成器看的信号**，运行期不认识它们 |

**不解决什么（这些边界常常被误以为在模块内）：**

| 不在模块内 | 实际归谁 |
|---|---|
| 属性变更通知的实现本身 | 生成器**只在没人提供时才自举**：基类链上没有 `PropertyChanged` 事件、宿主框架也没给，它就自己补事件 + `INotifyPropertyChanged` / `INotifyPropertyChanging` 接口 + `OnPropertyChanged(string)` / `OnPropertyChanging(string)` 虚方法（`Writers/MVVMWriter.cs:198-312` 决策、`:856-877` 落笔）。**所以不继承任何基类也能编译** —— 产物依据：`Src/Core/VeloxDev.Core.Extension/obj/Debug/netstandard2.0/generated/VeloxDev.Core.Generator/VeloxDev.Generators.MVVM/McpStatusViewModel_VeloxDev_AI_MCP_MVVM.g.cs`，该类 `Symbol.BaseType` 是 `object`，产物里自带了事件与两个虚方法。基类已提供时生成器只发 `OnPropertyChanging(...)` / `OnPropertyChanged(...)` 的**调用**（`Examples/MVVM/WPF/Demo/ObservableViewModelBase.cs` 就是这种情形） |
| 视图模型之外的「命令参数校验」 | 只有 `canValidate: true` 时才生成 `private partial bool CanExecute{名}Command(object? parameter);`（`CommandWriter.cs:167`）。该声明带 `private` 修饰符且返回非 `void` ⇒ **必须写实现体，不写就是 CS8795 编译失败**（2026-10-01 最小复现验证过）。`canValidate: false` 时传的是 `canExecute: _ => true`（`CommandWriter.cs:180`），**不是 `null`** |
| 集合变更的**语义**（谁加了谁） | `ObservableCollectionTracker` 只负责「订上」；语义在生成器发的 `OnItemAddedTo{名}` 等 `partial void` 里（`Analizer.cs:814-817`） |
| 平台适配 | **零适配器**。`VeloxCommand` 实现的是 `System.Windows.Input.ICommand`，XAML 绑定不需要任何平台代码 —— 这是它跟 `TransitionSystem` / `WorkflowSystem` 最大的结构差异（那两个有 7 家 `adapters/`） |
| 跨线程编组 | 不解决。`VeloxCommand` 全用 `ConfigureAwait(false)`（`:26`、`:148`、`:184` 等），**不还原同步上下文**；事件回调在哪个线程发就看你从哪调 `Execute` |

---

## 二、VeloxCommand 相对 `ICommand` 多了什么，以及状态机

**多出来的四件事**（都不在 `ICommand` 里）：

1. **并发上限**：构造时给 `semaphore`（默认 1）。超过上限的调用**不丢弃**，而是进 `_pendingQueue` 排队。
2. **队列控制**：`Lock` / `UnLock` / `Interrupt` / `Clear` / `Continue` / `ChangeSemaphore`（各带一个 async 孪生，`:251-261` 的同步版全是 `_ = XxxAsync()`）。
3. **取消**：内部给每个 item 配 `CancellationTokenSource`，`Interrupt` / `Clear` 靠它打断正在跑的命令。
4. **8 个生命周期事件**（`CommandEventType`，`:20-32`），每个带 `CommandEventArgs`。

**一次 `Execute` 的完整状态机**（`ExecuteAsync` `:276` → `ExecuteCoreAsync` `:334` → `OnExecutionCompletedAsync` `:368`）：

```
Execute(p)
  └ Created                                   :284   ← 无论后面发生什么, 这个先发
     └ _stateLock.WaitAsync()                 :292   ← 唯一门, 锁内只推进状态、一个事件都不发
        ├ 被 Lock 挡下                     → 记 forceLocked
        ├ _active.Count < _maxConcurrency  → 进 _active, 记 startNow
        └ 否则                             → 进 _pendingQueue, 记 enqueued
        Release()                             :312
     ── 以下全在锁外 ──                            ← RaiseCommandEvent 调的是用户 handler,
        ├ forceLocked → Cts.Cancel + Canceled :318 + 释放 Cts :320 → return   (不进 _active)
        ├ enqueued    → Enqueued              :324
        └ startNow    → `_ = ExecuteCoreAsync(item)` :328
        Notify()                              :331
ExecuteCoreAsync(item)   (fire-and-forget, `_ = ...`)
  ├ Started                                   :336
  ├ await _command(...)      ← 用 item.Cts 还是静态 _defct 由 _isCtsNeeded 决定
  ├ Completed                                 :348
  ├ catch OperationCanceledException → Canceled  :352
  ├ catch Exception                  → Failed    :356
  └ finally → OnExecutionCompletedAsync, 然后 item.TakeCts()?.Dispose()  :364  ← 唯一释放点
OnExecutionCompletedAsync(item)
  ├ _active.Remove(item)                      :374
  ├ Exited                                    :380
  └ TryStartPendingAsync()                    ← 出队并启动, 发 Dequeued; 末句 RaiseCanExecuteChanged :595
```

**四件只在读代码时才会发现的事：**

- **`CanExecute` 与节流无关**。它只读谓词与 `_isForceLocked`（`:241-242`），**不看 `_active.Count`**。所以「队列满了」不会让按钮变灰 —— 想把「忙」反映到 UI，只能自己调 `Lock()`（它才写 `_isForceLocked`）。
- **`Notify()` 在 `ExecuteAsync` 的末尾**（`:331`，不再是 `finally`）：`Execute` 返回前 `CanExecuteChanged` 一定发过一次，**哪怕这条命令只是进了队列**。
- **同一个 item 可以收到两次 `Canceled`**：`InterruptAsync` 主动 `Cancel()` 后自己发一条（`:456`），被中断的命令随后在 `catch (OperationCanceledException)` 里**又发一条**（`:352`）。`ClearAsync` 亦然（`:499` + `:514`）。⇒ **被 `Clear` 清掉的一次「正在跑 + 两个排队」会产生 4 条 `Canceled`**（两个排队项各 1，正在跑的那个 2）。写 handler 按 `CommandEventType` 计数时要注意。
- **`ExecuteCoreAsync` 是 fire-and-forget**（`:328`、`:592`）：`Execute`/`ExecuteAsync` 返回时命令**可能还没跑**（排队中）或**刚跑完**。要等结果只能用 `Completed` / `Exited` 事件，不能 await `ExecuteAsync` 的返回 —— 它只等到「入队成功」。

---

## 三、谁拥有状态（唯一写者）

| 状态 | 门 | 唯一写者 |
|---|---|---|
| `_active`（正在跑的） | `_stateLock` | `ExecuteAsync` `:300` 加、`OnExecutionCompletedAsync` `:373` 减、`InterruptAsync` `:438` / `ClearAsync` `:477` 清空 |
| `_pendingQueue` | `_stateLock` | `ExecuteAsync` `:305` 入队、`TryStartPendingAsync` `:579` 出队、`ClearAsync` `:481` 全部出队 |
| `_isForceLocked` | `_stateLock` | `LockCoreAsync` `:400-401`（`LockAsync` 与 `Interrupt`/`Clear` 都经它）/ `UnLockAsync` `:416` |
| `_maxConcurrency` | `_stateLock` | `ChangeSemaphoreAsync` `:558`；构造时 `:159` 校验 `semaphore >= 1`，否则抛 |
| `item.Cts` | `TakeCts()` 的 `Interlocked.Exchange` | `ExecuteAsync` 构造时 `:281`（`internal set`，`CommandEventArgs` `:619`）；取出即置 null |
| `_isCtsNeeded` | **不可变** | 只在构造/工厂里写（`:76`、`:108`、`:127`、`:146`） |
| 代理/订阅表 | 各自锁 | 见 §五 |

**`_stateLock` 是 `SemaphoreSlim(1,1)`（`:155`），持锁期间绝不调用用户代码**。这条以前只是**写在文档里的愿望** —— 2026-10-01 之前实际有四处违反（`ExecuteAsync` 的 `Canceled`/`Enqueued`，以及 `_ = ExecuteCoreAsync(item)` 在第一个 `await` 前同步发出的 `Started`；还有 `ClearAsync` 排空循环里的 `Dequeued`）。现在 `ExecuteAsync`/`ClearAsync` 都改成 `TryStartPendingAsync` 那套：**锁内只收集，出锁后才发事件、才启动 `ExecuteCoreAsync`**。

**为什么必须拆**：`RaiseCommandEvent` 调的是**用户 handler**。handler 里同步再取同一把锁（`LockAsync().GetAwaiter().GetResult()`）在旧代码里是**真死锁** —— `SemaphoreSlim` 不可重入，而锁的持有者正是当前线程。这正是 `VeloxCommandLockInvariantTests` 钉住的场景。

---

## 四、`ObservableCollectionTracker` 服务谁

**只有一个调用者群体：生成器吐出来的代码。** 全仓库（`Src/`、`Src/Adapters/`）对它的引用**恰好三处，全是 `Analizer.cs` 里的模板字符串**：

| 位置 | 生成的语句 | 在哪个成员里 |
|---|---|---|
| `Analizer.cs:639` | `EnsureSubscribed({字段}, On{属性}CollectionChanged)` | **getter**，每次读都调（`IsNotifyCollectionChanged` 时） |
| `Analizer.cs:672` | `Unsubscribe(old, On{属性}CollectionChanged)` | setter 替换旧值**之前** |
| `Analizer.cs:693` | `EnsureSubscribed(value, On{属性}CollectionChanged)` | setter 写入新值**之后** |

**它为什么存在**：`_items = []` 这种字段初始化器**直接写字段**，绕过生成的 setter —— setter 里的订阅步骤永远不跑。getter 侧的 `EnsureSubscribed` 就是补这个洞：懒订阅 + 幂等。

**去重的键是 `(Method, Target)`，不是委托引用**（`ObservableCollectionTracker.cs:96-114`）。这条是必需的：生成的 getter 传的是**方法组**，每次访问都构造一个**新的**委托实例，按引用比会每次重新订阅、无界增长（`:63-70` 的注释把这件事写明了）。`Target` 用 `ReferenceEquals` 比（`:104`），值相等但不同实例的目标不会被合并。

**它只覆盖 `INotifyCollectionChanged`**：类型判定在生成器侧（`Analizer.cs:922-930`，走 `AllInterfaces`），运行期 `EnsureSubscribed` 自己还有一道 `is not INotifyCollectionChanged` 的早退（`:28`）。

**它与 WorkflowSystem 的关系是「被生成器牵连」，不是「被调用」**：Core 里大量 `[VeloxProperty]` 的集合属性（`Src/Core/VeloxDev.Core/WorkflowSystem/CompilerEx/Compile/Model/`、`GUI/GeometryModels/`、`Templates/ViewModels/`）会自动带上这三处调用。而 `Src/Core/VeloxDev.Core/WorkflowSystem/Templates/Helpers/{TreeHelper.cs:114-115, NodeHelper.cs:32, SlotHelper.cs:34-35}` 是**手工**订阅同一批集合的 —— 两条路径并行，不是互相调用。**TransitionSystem 完全不用它**（`Src/Core/VeloxDev.Core/TransitionSystem/` 下零引用）。

---

## 五、不变量

1. **`_isCtsNeeded == false` ⇒ 命令不可打断。** 五种构造路径会把 `item.Cts` 留成 `null`：`CreateTaskOnlyWithParameter`（`:70`）与三个 `Action`/`Func<Task>` 重载（`:108`、`:127`、`:146` 写 `_isCtsNeeded = false`）。此时 `Interrupt` / `Clear` 仍然会发 `Canceled` 事件、仍然会从 `_active` 摘掉它，但**底层那个 task 继续跑到底**（`it.Cts?.Cancel()` 是 null 条件调用）。只有 `CreateTaskOnlyWithCancellationToken` 拿得到真 token —— 而它**目前全仓没有任何调用方**（生成器只走 `new VeloxCommand(...)` 那条路），所以「真取消」这条路径在生产代码里是死的，只有测试覆盖。
2. **事件 handler 抛异常被吞掉，但不再无出口。** `RaiseCanExecuteChanged`（`:216`）与 `RaiseCommandEvent`（`:228`）仍然 `catch` 住不往外抛 —— 这是刻意的：`Exited` 的 handler 若把异常漏出去，`OnExecutionCompletedAsync` 会中断，**队列永远停摆**；`Completed` 的 handler 漏出去则会被 `ExecuteCoreAsync` 的 `catch (Exception)` 抓住，把一次成功误报成 `Failed`。2026-10-01 起新增静态钩子 `VeloxCommand.HandlerException`（`:182`），默认不订阅 ⇒ 行为与从前逐字节一致。**钩子自身也被 `catch` 包住**（`ReportHandlerException` `:203`），否则一个坏掉的诊断订阅者就能制造上面两种事故。
3. **`semaphore < 1` 抛 `ArgumentOutOfRangeException`**（2026-10-01 改）：构造 `:159` 的字段初始化器、`ChangeSemaphoreAsync` `:550` 开头、以及同步入口 `ChangeSemaphore` `:263` 各校验一次。**同步版必须自己校验** —— 它是 `_ = ChangeSemaphoreAsync(...)`，异常若只在 async 方法里抛就没人接得住，会变成未观察异常。以前这里是静默夹紧/静默 no-op。
4. **`Interrupt` / `Clear` 不再清掉调用方已有的锁。** 两者都经 `LockCoreAsync` `:395`，它返回「此前是否已锁」，只有此前**未**锁时才在结尾 `UnLockAsync`（`:428` 起 / `:466` 起）。2026-10-01 之前它们无条件 `UnLockAsync()`，于是对一个本来锁着的命令调 `Interrupt()` 会「取消在跑的 + 解锁 + 经 `TryStartPendingAsync` 放行整个排队队列」—— 与 `Interrupt` 的字面语义相反。仓库自带的 WPF/Avalonia demo（`Examples/MVVM/*/Demo/*ViewModel.cs` 的 `Lock(); Interrupt(); Clear(); UnLock();`）注释里假设的就是现在的语义。
5. **`Interrupt` 与 `Clear` 的差别只在排队项**：`InterruptAsync` 只清 `_active`（`:438`），`_pendingQueue` 原封不动、稍后被放行；`ClearAsync` 把两者都清（`:477-483`），先给每个排队项发 `Dequeued`（`:492`）再统一发 `Canceled`。
6. **`ContinueAsync` 在锁着时是空操作**（`:524` 起，读到已锁就直接 return）。它的存在意义是「解锁之外再踢一次队列」。
7. **`ExecuteAsync` 自己不抛**：内层 `catch` 都在 `ExecuteCoreAsync` 里。但注意 `Notify()` 已从 `finally` 挪到末尾（`:331`）—— 中间那段现在只有赋值和 `RaiseCommandEvent`（自己吞异常），所以仍不会漏发。
8. **`Notify()` 与 `RaiseCanExecuteChanged()` 是两个入口，语义相同**：`Notify()`（`:248`）就是 `RaiseCanExecuteChanged()` 的公开别名。`IVeloxCommand` 把它放进契约（`Src/Core/VeloxDev.Core/Interfaces/MVVM/IVeloxCommand.cs:18`）是为了让视图模型在**谓词外部状态**变了时手动催一次 —— 注意这是唯一能让 `CanExecuteChanged` 提前到达的途径。
9. **`CancellationTokenSource` 的所有权是「谁取出谁释放」**（2026-10-01 新增）。`CommandEventArgs.TakeCts()`（`:657`）用 `Interlocked.Exchange` 取出并置 null，**恰好一个调用者拿得到**。两条释放规则不同：
   - **正在跑的项**：只有 `ExecuteCoreAsync` 的 `finally`（`:364`）释放 —— 那是唯一能确定「命令体已经结束、没人再观察 token」的位置。`Interrupt`/`Clear` 对它们**只 `Cancel()` 不 `Dispose()`**，且 `Cancel()` 包了 `catch (ObjectDisposedException)`（命令体可能已自行跑完并释放）。**提前释放会让命令体里的 `ct.Register` 抛异常，把一次本该是 `Canceled` 的执行变成 `Failed`。**
   - **`Clear` 掉的排队项**：从不进入 `ExecuteCoreAsync`，所以只能由 `ClearAsync` 自己 `Cancel` + `Dispose`（`:500`）。漏了这一步就是纯泄漏。

---

## 六、生成器那一半

两份产物、两个 writer，文件名都是 `<类>_<命名空间>_{后缀}.g.cs`：

| 产物 | writer | 触发条件 | 内容 |
|---|---|---|---|
| `_MVVM.g.cs` | `MVVMWriter` | `MVVMProperties.Count > 0 \|\| AutoProperties.Count > 0 \|\| IsWorkflowComponent`（`MVVMWriter.cs:845`） | 属性/字段重写 + 通知调用 + 集合钩子 + 可能的事件声明 |
| `_Commands.g.cs` | `CommandWriter` | 有 `[VeloxCommand]` 方法（`CommandWriter.cs:118`） | 惰性 `{名}Command` 属性 |

**三件读代码才知道的事：**

- **`[VeloxProperty]` 有两条路，产物不同。** 标在**字段**上（`:96-114`，要求 `global::VeloxDev.MVVM.VeloxPropertyAttribute` 全名精确匹配）走 `MVVMFieldAnalizer`；标在**partial 属性**上（`:122-140`）走 `MVVMPropertyAnalizer`，且 `ShouldGeneratePartialProperty` 会挡掉非 partial 的。两条路都能用，但 `HasSetter` 的推导不同（`Analizer.cs:405-408` vs `:427`）。
- **没有「View 只透传、不发通知」这条路径** —— 曾经有（`IsView` / `GenerateProxy()`），2026-09-26 因从未被走到而整体删除（`Writers/MVVMWriter.cs:105`、`:131` 两处构造一直传 `isView: false`）。现在 `MVVMPropertyFactory.Generate()`（`Base/Analizer.cs:583`，原名 `GenerateViewModel`）是唯一出口，**所有** `[VeloxProperty]` 都按 ViewModel 形态生成通知。
- **`CanWrite()` 里含 `IsWorkflowComponent`**（`MVVMWriter.cs:845`）⇒ 一个 `[Node]` / `[Tree]` 类即使零个 `[VeloxProperty]` 也会拿到一份 MVVM 产物，但里面**不是**槽位三件套：`MVVMWriter.cs:895` 那段的条件是 `!_hasBaseWorkflowSlotInfrastructure && !IsWorkflowComponent && 任一属性 UseWorkflowSlotLifecycle`，**把 workflow 组件本身排除了**，它只服务「非组件、但继承链上有带槽位属性的类」这一种情况。真正 workflow 组件的 `CreateWorkflowSlot<T>` / `OnWorkflowSlotAdded` / `OnWorkflowSlotRemoved` 由 `Writers/WorkflowWriter.cs:899-917` 写。这是与 `Src/Core/VeloxDev.Core/WorkflowSystem/Templates/` 的耦合点。

**`[VeloxCommand]` 的方法签名决定它可不可取消**：`CommandWriter.ParseConstructorType`（`:78-116`）只认三种签名 —— 单参数返回 `Task`/`Task<T>` 且参数是 `object`（→ `CreateTaskOnlyWithParameter`）、单参数是 `CancellationToken`（→ `CreateTaskOnlyWithCancellationToken`）、其余（→ `new VeloxCommand(...)` 指向 `Func<object?, CancellationToken, Task>` 主构造）。**只有第二种能拿到 token**，也就只有它生成的命令 `Interrupt`/`Clear` 真能打断（见 §五·1）。

**`[VeloxCommand]` 的命名**：`name = "Auto"` 时用 `方法名.Replace("Async", "")`（`:64-67`）—— 是**全局替换**，`GetAsyncDataAsync` 会变成 `GetData`；且位置参数先读、具名参数覆盖（`:39-61`）。

**`{名}Command` 属性是惰性 + 缓存的**：`_buffer_{名}Command ??= 构造(...)`（`:160`、`:178`）。所以命令对象在**第一次读属性时**才建，`[VeloxCommand]` 不保证构造顺序。

---

## 七、入口：我要改 X，先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 并发/排队/锁/中断的语义 | `VeloxCommand.cs`（`ExecuteAsync` `:276`、`TryStartPendingAsync` `:568`、`InterruptAsync` `:428`、`ClearAsync` `:466`） |
| 事件在什么时候发、发几次 | 同上，`CommandEventType` `:20-32` + 两个 `Raise*` `:216`/`:228` + 各 stage 的调用点 |
| 锁的语义 / 「持锁期间不许干什么」 | `LockCoreAsync` `:395`（返回「此前是否已锁」）+ §三 |
| CancellationTokenSource 谁释放 | `TakeCts()` `:657` + `ExecuteCoreAsync` 的 `finally` `:364` + `ClearAsync` `:500` + §五·9 |
| handler 抛异常去哪了 | `ReportHandlerException` `:203` + `VeloxCommand.HandlerException` `:182` |
| 「这个命令能不能取消」 | `_isCtsNeeded` 的四个写入点（`:76`、`:108`、`:127`、`:146`）+ `CommandWriter.ParseConstructorType` `:78` |
| `[VeloxProperty]` 生成出什么 | `Base/Analizer.cs` 的 `MVVMPropertyFactory`（getter `:630`、setter 前后 `:672`/`:693`、集合成员 `:761`） |
| 集合订阅的兜底 | `ObservableCollectionTracker.cs` + 上表三处生成点 |
| `[VeloxCommand]` 的命名与构造选择 | `Writers/CommandWriter.cs:64`（命名）、`:78`（构造选择）、`:140`（模板） |
| 「框架基类已经把 setter 写好了」 | `Writers/MVVMWriter.cs:42-89`（CommunityToolkit / Prism / ReactiveUI / Caliburn 的 setter 模式探测） |
| 集合钩子的接缝 | `MVVMWriter.cs:834-843`（基类是否已有 `OnCollectionChanged`）+ `:883-893`（`protected virtual`） |

---

## 八、陷阱（带依据）

1. **「不继承基类就编译不过」已经过期 —— 生成器会自举通知基础设施。** `MVVMWriter.ConfigurePropertyNotificationInfrastructure`（`:198-258`）在「基类链上没有 `PropertyChanged` 事件 + 宿主框架/上层 Velox 类都没提供」时，把 `_generatePropertyChangedEvent` / `_addNotifyPropertyChangedInterface` 置真，产物因此自带事件、接口与 `OnPropertyChanged(string)`（`GenerateBody` `:874-881`、`GenerateBaseInterfaces` `:856-864`）。**判据是符号，不是「有没有基类」。** 基类存在时生成器仍然只发**调用**（`Analizer.cs:608-610` 发 `partial void On{X}Changing/Changed` 声明），事件与 `OnPropertyChanged` 由基类给。
2. **`ObservableCollectionTracker.Unsubscribe` 不「拉黑」这个 (handler, 集合) 对。** 它只做 `-= handler` 与 `entry.Remove(handler)`（`:50-54`），**不移除表项**。所以 `Unsubscribe` 之后再 `EnsureSubscribed` 同一 handler 会**重新订阅**（因为 `Entry` 还在、`TryAdd` 返回 true）。**这不是 bug，是生成器依赖的行为** —— setter 换值时 `Unsubscribe(old)`，而 getter 每次都 `EnsureSubscribed`，靠的就是这份可逆性。（旧版注释曾写成「removes its tracking entry so the subscription is not accidentally restored later」，与代码相反；2026-10-01 已按代码改写注释。`ObservableCollectionTrackerTests` 钉住了这个语义。）
3. **`Unsubscribe` 的减法依赖委托等价**：`-=` 用的是 `Delegate.Equals`（方法 + 目标的**值**比较），而 tracker 自己的去重用的是 `ReferenceEquals(Target)`（`:104`）。对普通视图模型两者一致；对 `Target` 被重写过 `Equals` 的类型，两条判定会分叉 —— 一行注释也没写，属于**只从代码看出的不一致**。
4. **`Clear` 之后再 `Continue` 没用**：`ClearAsync` 已经把 `_pendingQueue` 掏空（`:294-299`），`ContinueAsync` 只是再踢一次空队列。想「暂停队列」该用 `Lock()`。
5. **只有 `Failed` 携带 `Exception` —— `Exited` 带的是 `null`。**（2026-10-01 更正；本文旧版说「`Exited` 也带同一个 `Exception`」，**是错的**。）
   推导：`ExecuteAsync` 造出的那个 `item`（`:278`）`Exception` 恒为 `null`，且**全程不被改写** —— 所有 `With(...)` 都返回**新实例**并且只喂给 `RaiseCommandEvent`。所以 `:198` 那条 `With(..., ex)` 里的 `ex ?? Exception` 里，接收者的 `Exception` 永远是 null，内部**没有任何一处**触发过它。
   `With` 的 `ex ?? Exception`（`:672`）本身是**粘性**的：对一份已带异常的 args 再 `With(别的 stage)` 会继承下去。命令自己走不到（它只从「出厂 args」投影），只有**外部手工链式调用 `With`** 才会碰到 —— 所以它是 `With` 的契约细节，不是运行时行为。
   `VeloxCommandLifecycleTests` / `CommandEventArgsTests` 把这两条都钉住了。
6. **`CommandEventArgs.Cts` 是 `internal set`**（`:391`），外部只能读。想给自定义命令传 CTS 没有公开口子，只能在 `_command` 闭包里自己接 `CancellationToken`。
7. **`_active` 用 `List<T>.Remove`（`:211`）而非 `HashSet`**：`O(n)`，且依赖 `CommandEventArgs` 的**引用**相等（它是普通 class，没有重写 `Equals`）。这没问题，但意味着并发数很大时 `OnExecutionCompletedAsync` 是线性开销。
8. **`Exited` 在池线程上触发 —— UI 处理器必须自己跳回界面线程，而且它抛的异常会被吞掉（现在至少能被钩子看见）。** `ExecuteCoreAsync` 以 `ConfigureAwait(false)` 等待命令体，所以 `RaiseCommandEvent` 发出的事件不在 UI 线程上；而它把处理器包在 `try/catch` 里**静默吞掉**异常 ⇒ 一个在 `Exited` 里写控件属性的 WPF/WinUI 处理器会抛 `InvalidOperationException`、被吞、**界面看起来只是"什么都没发生"**（典型症状：按钮永远不亮）。2026-09-27 实测（把 demo 的运行控制接进七家时撞上）：WPF 用 `Dispatcher.InvokeAsync`、WinUI 用 `DispatcherQueue.TryEnqueue`、Avalonia 用 `Dispatcher.UIThread.Post`、Blazor 用 `InvokeAsync(StateHasChanged)`、MAUI 用 `MainThread.BeginInvokeOnMainThread`。**2026-10-01 起**这些被吞的异常会经 `VeloxCommand.HandlerException` 报出来（订阅它即可定位「代码没跑也不报错」），但**吞掉本身没有变**。**要读 `CommandEventArgs` 也顺手**：委托是 `CommandEventHandler(CommandEventArgs e)` —— 一个参数，写 `+= (_, _)` 编译不过。
