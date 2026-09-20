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
| 属性变更通知本身（`INotifyPropertyChanged` 的收发） | **不产生事件实现**。生成器只发 `OnPropertyChanging` / `OnPropertyChanged` 的**调用**，事件与 `partial void` 声明由**用户基类**给（`Examples/MVVM/WPF/Demo/ObservableViewModelBase.cs`）。所以一个不继承任何基类的 `[VeloxProperty]` 类**编译不过** |
| 视图模型之外的「命令参数校验」 | 只有 `canValidate: true` 时才生成 `private partial bool CanExecute{名}Command(object? parameter);`（`CommandWriter.cs:167`）；不实现它就是空钩子，`CanExecute` 恒真 |
| 集合变更的**语义**（谁加了谁） | `ObservableCollectionTracker` 只负责「订上」；语义在生成器发的 `OnItemAddedTo{名}` 等 `partial void` 里（`Analizer.cs:814-817`） |
| 平台适配 | **零适配器**。`VeloxCommand` 实现的是 `System.Windows.Input.ICommand`，XAML 绑定不需要任何平台代码 —— 这是它跟 `TransitionSystem` / `WorkflowSystem` 最大的结构差异（那两个有 7 家 `adapters/`） |
| 跨线程编组 | 不解决。`VeloxCommand` 全用 `ConfigureAwait(false)`（`:26`、`:148`、`:184` 等），**不还原同步上下文**；事件回调在哪个线程发就看你从哪调 `Execute` |

---

## 二、VeloxCommand 相对 `ICommand` 多了什么，以及状态机

**多出来的四件事**（都不在 `ICommand` 里）：

1. **并发上限**：构造时给 `semaphore`（默认 1）。超过上限的调用**不丢弃**，而是进 `_pendingQueue` 排队（`:158-167`）。
2. **队列控制**：`Lock` / `UnLock` / `Interrupt` / `Clear` / `Continue` / `ChangeSemaphore`（各带一个 async 孪生，`:132-137` 的同步版全是 `_ = XxxAsync()`）。
3. **取消**：内部给每个 item 配 `CancellationTokenSource`，`Interrupt` / `Clear` 靠它打断正在跑的命令。
4. **8 个生命周期事件**（`CommandEventType`，`:3-14`），每个带 `CommandEventArgs`。

**一次 `Execute` 的完整状态机**（`ExecuteAsync` `:139` → `ExecuteCoreAsync` `:176` → `OnExecutionCompletedAsync` `:206`）：

```
Execute(p)
  └ Created                                   :141   ← 无论后面发生什么, 这个先发
     ├ 被 Lock 挡下  → Cts.Cancel + Canceled → return        :151-156   (不进 _active)
     └ _stateLock.WaitAsync()                                 :148   ← 唯一门
        ├ _active.Count < _maxConcurrency → 进 _active, 启动 ExecuteCoreAsync  :158-162
        └ 否则                            → 进 _pendingQueue, 发 Enqueued    :165-166
        finally: Release + Notify()                           :169-173   ← 每条 Execute 都触发 CanExecuteChanged
ExecuteCoreAsync(item)   (fire-and-forget, `_ = ...` :161)
  ├ Started                                                   :178
  ├ await _command(...)      ← 用 item.Cts 还是静态 _defct 由 _isCtsNeeded 决定  :182-189
  ├ Completed                                                 :190
  ├ catch OperationCanceledException → Canceled               :192-194
  ├ catch Exception                  → Failed                 :196-198
  └ finally → OnExecutionCompletedAsync                       :200-202
OnExecutionCompletedAsync(item)
  ├ _active.Remove(item)                                      :211
  ├ Exited                                                    :218
  ├ RaiseCanExecuteChanged()                                  :219
  └ TryStartPendingAsync()                                    :221   ← 出队并启动, 发 Dequeued  :370-374
```

**四件只在读代码时才会发现的事：**

- **`CanExecute` 与节流无关**。它只读谓词与 `_isForceLocked`（`:125-126`），**不看 `_active.Count`**。所以「队列满了」不会让按钮变灰 —— 想把「忙」反映到 UI，只能自己调 `Lock()`（它才写 `_isForceLocked`）。
- **`Notify()` 在 `ExecuteAsync` 的 `finally` 里**（`:172`）：`Execute` 返回前 `CanExecuteChanged` 一定发过一次，**哪怕这条命令只是进了队列**。
- **同一个 item 可以收到两次 `Canceled`**：`InterruptAsync` 主动 `Cancel()` 后自己发一条（`:274-275`），被中断的命令随后在 `catch (OperationCanceledException)` 里**又发一条**（`:194`）。`ClearAsync` 同理（`:308-309`）。写 handler 按 `CommandEventType` 计数时要注意。
- **`ExecuteCoreAsync` 是 fire-and-forget**（`:161`、`:373`）：`Execute`/`ExecuteAsync` 返回时命令**可能还没跑**（排队中）或**刚跑完**。要等结果只能用 `Completed` / `Exited` 事件，不能 await `ExecuteAsync` 的返回 —— 它只等到「入队成功」。

---

## 三、谁拥有状态（唯一写者）

| 状态 | 门 | 唯一写者 |
|---|---|---|
| `_active`（正在跑的） | `_stateLock` | `ExecuteAsync` `:160` 加、`OnExecutionCompletedAsync` `:211` 减、`InterruptAsync` `:265` / `ClearAsync` `:292` 清空 |
| `_pendingQueue` | `_stateLock` | `ExecuteAsync` `:165` 入队、`TryStartPendingAsync` `:360` 出队、`ClearAsync` `:296` 全部出队 |
| `_isForceLocked` | `_stateLock` | `LockAsync` `:229` / `UnLockAsync` `:244` |
| `_maxConcurrency` | `_stateLock` | `ChangeSemaphoreAsync` `:339`（构造时 `:86` 已 `Math.Max(1, semaphore)`） |
| `item.Cts` | 无锁 | `ExecuteAsync` 构造时 `:144`（`internal set`，`CommandEventArgs` `:391`） |
| `_isCtsNeeded` | **不可变** | 只在构造/工厂里写（`:30`、`:52`、`:64`、`:76`） |
| 代理/订阅表 | 各自锁 | 见 §五 |

**`_stateLock` 是 `SemaphoreSlim(1,1)`（`:82`），绝不在持锁期间发事件**。所有 `RaiseCommandEvent` 都在 `Release()` 之后 —— `InterruptAsync`（`:261-310`）和 `ClearAsync`（`:288-312`）把「摘出列表」与「发事件」**刻意拆成两段**就是为了这个。`TryStartPendingAsync` 同理：先出队拿到 `toStart`，出锁后才逐个 `Dequeued + ExecuteCoreAsync`（`:353-374`）。

**为什么拆**：`RaiseCommandEvent` 调的是**用户 handler**，里面再调 `Lock()` / `Execute()` 是正常用法；`SemaphoreSlim` 不可重入，持锁发事件必然死锁。

---

## 四、`ObservableCollectionTracker` 服务谁

**只有一个调用者群体：生成器吐出来的代码。** 全仓库（`Src/`、`Src/Adapters/`）对它的引用**恰好三处，全是 `Analizer.cs` 里的模板字符串**：

| 位置 | 生成的语句 | 在哪个成员里 |
|---|---|---|
| `Analizer.cs:646` | `EnsureSubscribed({字段}, On{属性}CollectionChanged)` | **getter**，每次读都调（`IsNotifyCollectionChanged` 时） |
| `Analizer.cs:679` | `Unsubscribe(old, On{属性}CollectionChanged)` | setter 替换旧值**之前** |
| `Analizer.cs:700` | `EnsureSubscribed(value, On{属性}CollectionChanged)` | setter 写入新值**之后** |

**它为什么存在**：`_items = []` 这种字段初始化器**直接写字段**，绕过生成的 setter —— setter 里的订阅步骤永远不跑。getter 侧的 `EnsureSubscribed` 就是补这个洞：懒订阅 + 幂等。

**去重的键是 `(Method, Target)`，不是委托引用**（`ObservableCollectionTracker.cs:96-114`）。这条是必需的：生成的 getter 传的是**方法组**，每次访问都构造一个**新的**委托实例，按引用比会每次重新订阅、无界增长（`:63-70` 的注释把这件事写明了）。`Target` 用 `ReferenceEquals` 比（`:104`），值相等但不同实例的目标不会被合并。

**它只覆盖 `INotifyCollectionChanged`**：类型判定在生成器侧（`Analizer.cs:922-930`，走 `AllInterfaces`），运行期 `EnsureSubscribed` 自己还有一道 `is not INotifyCollectionChanged` 的早退（`:28`）。

**它与 WorkflowSystem 的关系是「被生成器牵连」，不是「被调用」**：Core 里大量 `[VeloxProperty]` 的集合属性（`Src/Core/VeloxDev.Core/WorkflowSystem/CompilerEx/Compile/Model/`、`GUI/GeometryModels/`、`Templates/ViewModels/`）会自动带上这三处调用。而 `Src/Core/VeloxDev.Core/WorkflowSystem/Templates/Helpers/{TreeHelper.cs:114-115, NodeHelper.cs:32, SlotHelper.cs:34-35}` 是**手工**订阅同一批集合的 —— 两条路径并行，不是互相调用。**TransitionSystem 完全不用它**（`Src/Core/VeloxDev.Core/TransitionSystem/` 下零引用）。

---

## 五、不变量

1. **`_isCtsNeeded == false` ⇒ 命令不可打断。** 五种构造路径会把 `item.Cts` 留成 `null`：`CreateTaskOnlyWithParameter`（`:30`）与三个 `Action`/`Func<Task>` 重载（`:52`、`:64`、`:76`）。此时 `Interrupt` / `Clear` 仍然会发 `Canceled` 事件、仍然会从 `_active` 摘掉它，但**底层那个 task 继续跑到底**（`it.Cts?.Cancel()` 是 null 条件调用，`:274`、`:308`）。只有 `CreateTaskOnlyWithCancellationToken`（`:33-41`）拿得到真 token。
2. **事件 handler 抛异常被静默吞掉。** `RaiseCanExecuteChanged`（`:103-112`）与 `RaiseCommandEvent`（`:114-123`）都是空 `catch`。所以 handler 里的 bug 没有任何出口 —— 没有异常、没有日志。这是「我的 `Completed` 里代码没跑但也不报错」的根因。
3. **`semaphore < 1` 被夹到 1**：构造 `:86` 与 `ChangeSemaphoreAsync` `:339` 都是 `Math.Max(1, ...)`，且 `ChangeSemaphoreAsync` 开头对 `< 1` 直接 `return`（`:333-334`）—— `ChangeSemaphore(0)` **什么都不做**，不是「停掉队列」。
4. **`Interrupt` 会解锁。** `InterruptAsync` 结尾是 `UnLockAsync()`（`:278`），而 `UnLockAsync` 会 `TryStartPendingAsync()`（`:252`）。所以对一个**本来锁着**的命令调 `Interrupt()`，结果是「取消在跑的 + 解除锁 + 放行排队中的」。真正想什么都不放行的是 `Lock()`（`LockAsync` 里没有 `TryStartPendingAsync`，`:224-237`）。
5. **`Interrupt` 与 `Clear` 的差别只在排队项**：`InterruptAsync` 只清 `_active`（`:264-265`），`_pendingQueue` 原封不动；`ClearAsync` 把两者都清（`:291-298`）并给排队项各发 `Dequeued` 再 `Canceled`（`:297`、`:308-309`）。
6. **`ContinueAsync` 在锁着时是空操作**（`:320-321` 提前 return）。它的存在意义是「解锁之外再踢一次队列」。
7. **`ExecuteAsync` 自己不抛**：内层 `catch` 都在 `ExecuteCoreAsync` 里，`ExecuteAsync` 只有 `try/finally`。所以 `Execute`（`:128`）丢掉的那个 task 上不会有未观察异常。
8. **`Notify()` 与 `RaiseCanExecuteChanged()` 是两个入口，语义相同**：`Notify()`（`:130`）就是 `RaiseCanExecuteChanged()` 的公开别名。`IVeloxCommand` 把它放进契约（`Src/Core/VeloxDev.Core/Interfaces/MVVM/IVeloxCommand.cs:18`）是为了让视图模型在**谓词外部状态**变了时手动催一次 —— 注意这是唯一能让 `CanExecuteChanged` 提前到达的途径。

---

## 六、生成器那一半

两份产物、两个 writer，文件名都是 `<类>_<命名空间>_{后缀}.g.cs`：

| 产物 | writer | 触发条件 | 内容 |
|---|---|---|---|
| `_MVVM.g.cs` | `MVVMWriter` | `MVVMProperties.Count > 0 \|\| AutoProperties.Count > 0 \|\| IsWorkflowComponent`（`MVVMWriter.cs:845`） | 属性/字段重写 + 通知调用 + 集合钩子 + 可能的事件声明 |
| `_Commands.g.cs` | `CommandWriter` | 有 `[VeloxCommand]` 方法（`CommandWriter.cs:118`） | 惰性 `{名}Command` 属性 |

**三件读代码才知道的事：**

- **`[VeloxProperty]` 有两条路，产物不同。** 标在**字段**上（`:96-114`，要求 `global::VeloxDev.MVVM.VeloxPropertyAttribute` 全名精确匹配）走 `MVVMFieldAnalizer`；标在**partial 属性**上（`:122-140`）走 `MVVMPropertyAnalizer`，且 `ShouldGeneratePartialProperty` 会挡掉非 partial 的。两条路都能用，但 `HasSetter` 的推导不同（`Analizer.cs:405-408` vs `:427`）。
- **类型是 View 时不发通知。** `Generate()` 分派 `IsView ? GenerateProxy() : GenerateViewModel()`（`Analizer.cs:890`）：View 只生成 `get => 字段; set => 字段 = value;` 的透传（`:893-914`），**没有** `OnPropertyChanged`。
- **`CanWrite()` 里含 `IsWorkflowComponent`** ⇒ 一个 Workflow 组件类即使零个 `[VeloxProperty]` 也会拿到一份 MVVM 产物，里面是 `CreateWorkflowSlot<T>` / `OnWorkflowSlotAdded` / `OnWorkflowSlotRemoved`（`MVVMWriter.cs:895-925`）。这是与 `Src/Core/VeloxDev.Core/WorkflowSystem/Templates/` 的耦合点。

**`[VeloxCommand]` 的方法签名决定它可不可取消**：`CommandWriter.ParseConstructorType`（`:78-116`）只认三种签名 —— 单参数返回 `Task`/`Task<T>` 且参数是 `object`（→ `CreateTaskOnlyWithParameter`）、单参数是 `CancellationToken`（→ `CreateTaskOnlyWithCancellationToken`）、其余（→ `new VeloxCommand(...)` 指向 `Func<object?, CancellationToken, Task>` 主构造）。**只有第二种能拿到 token**，也就只有它生成的命令 `Interrupt`/`Clear` 真能打断（见 §五·1）。

**`[VeloxCommand]` 的命名**：`name = "Auto"` 时用 `方法名.Replace("Async", "")`（`:64-67`）—— 是**全局替换**，`GetAsyncDataAsync` 会变成 `GetData`；且位置参数先读、具名参数覆盖（`:39-61`）。

**`{名}Command` 属性是惰性 + 缓存的**：`_buffer_{名}Command ??= 构造(...)`（`:160`、`:178`）。所以命令对象在**第一次读属性时**才建，`[VeloxCommand]` 不保证构造顺序。

---

## 七、入口：我要改 X，先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 并发/排队/锁/中断的语义 | `VeloxCommand.cs`（`ExecuteAsync` `:139`、`TryStartPendingAsync` `:349`、`InterruptAsync` `:255`） |
| 事件在什么时候发、发几次 | 同上，`CommandEventType` `:3-14` + 三个 `Raise*` `:103`/`:114` + 各 stage 的调用点 |
| 「这个命令能不能取消」 | `_isCtsNeeded` 的五个写入点（`:30`、`:52`、`:64`、`:76`）+ `CommandWriter.ParseConstructorType` `:78` |
| `[VeloxProperty]` 生成出什么 | `Base/Analizer.cs` 的 `MVVMPropertyFactory`（getter `:630`、setter 前后 `:672`/`:693`、集合成员 `:761`） |
| 集合订阅的兜底 | `ObservableCollectionTracker.cs` + 上表三处生成点 |
| `[VeloxCommand]` 的命名与构造选择 | `Writers/CommandWriter.cs:64`（命名）、`:78`（构造选择）、`:140`（模板） |
| 「框架基类已经把 setter 写好了」 | `Writers/MVVMWriter.cs:42-89`（CommunityToolkit / Prism / ReactiveUI / Caliburn 的 setter 模式探测） |
| 集合钩子的接缝 | `MVVMWriter.cs:834-843`（基类是否已有 `OnCollectionChanged`）+ `:883-893`（`protected virtual`） |

---

## 八、陷阱（带依据）

1. **不继承基类的 `[VeloxProperty]` 编译不过。** 生成器只发 `OnPropertyChanging(...)` / `OnPropertyChanged(...)` 的**调用**（`Analizer.cs:608-610` 只发 `partial void On{X}Changing/Changed` 声明），`PropertyChanged` 事件本身要基类给（`MVVMWriter.cs:874-881` 只在需要时补发）。漏了基类 → 生成代码报「找不到方法」。
2. **`ObservableCollectionTracker.Unsubscribe` 的文档注释与代码不一致**：注释说「removes its tracking entry so the subscription is not accidentally restored later」（`ObservableCollectionTracker.cs:38-42`），但代码只做了两件事 —— `-= handler` 与 `entry.Remove(handler)`（`:50-54`），**没有**移除表项（`Entry` 也没有被删，`ConditionalWeakTable` 的表项随集合被回收）。**以代码为准**：`Unsubscribe` 之后再 `EnsureSubscribed` 同一 handler 会**重新订阅**，因为 `Entry` 还在且 `TryAdd` 会返回 true。
3. **`Unsubscribe` 的减法依赖委托等价**：`-=` 用的是 `Delegate.Equals`（方法 + 目标的**值**比较），而 tracker 自己的去重用的是 `ReferenceEquals(Target)`（`:104`）。对普通视图模型两者一致；对 `Target` 被重写过 `Equals` 的类型，两条判定会分叉 —— 一行注释也没写，属于**只从代码看出的不一致**。
4. **`Clear` 之后再 `Continue` 没用**：`ClearAsync` 已经把 `_pendingQueue` 掏空（`:294-299`），`ContinueAsync` 只是再踢一次空队列。想「暂停队列」该用 `Lock()`。
5. **`CommandEventArgs.With` 会保留 `Exception`**：`With(newType, ex = null)` 里写的是 `ex ?? Exception`（`:394`），所以后续事件会一直带着**第一个**异常。`Failed` 之后 `Exited` 也带同一个 `Exception`（`:218` 用 `completed.With(CommandEventType.Exited)`）—— 按 `EventType == Exited` 判断「成功」会错。
6. **`CommandEventArgs.Cts` 是 `internal set`**（`:391`），外部只能读。想给自定义命令传 CTS 没有公开口子，只能在 `_command` 闭包里自己接 `CancellationToken`。
7. **`_active` 用 `List<T>.Remove`（`:211`）而非 `HashSet`**：`O(n)`，且依赖 `CommandEventArgs` 的**引用**相等（它是普通 class，没有重写 `Equals`）。这没问题，但意味着并发数很大时 `OnExecutionCompletedAsync` 是线性开销。
