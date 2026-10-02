# MVVM — 架构

> 运行期：`Src/Core/VeloxDev.Core/MVVM/`（6 个 .cs），契约在 `Src/Core/VeloxDev.Core/Interfaces/MVVM/`（`IVeloxCommand.cs` + 两个可选扩展接口 `IVeloxCommandCompletion.cs` / `IVeloxCommandStatus.cs`）。
> 生成器：`Src/Generators/VeloxDev.Core.Generator/`。**下文不带路径的文件名都指这个目录**：`Writers/MVVMWriter.cs`、`Writers/CommandWriter.cs`、`Base/Analizer.cs`（配置读取与代码模板在它的 `MVVM*` 部分）。它们**不在本模块目录下**，别在 `MVVM/` 里找。
> 本文只写「读完这些文件才知道的东西」。类型清单、成员表、继承树请看 IDE。

---

## 一、这个模块是什么，不解决什么

**是什么。** 两件互不依赖的事被放在了同一个命名空间：

| 半边 | 归谁 | 本质 |
|---|---|---|
| 命令的**运行期语义** | `VeloxCommand.cs`（`VeloxCommand` + `CommandEventArgs` + `CommandEventType`） | 一个比 `ICommand` 重得多的实现：**上限并发 + 排队 + 强制锁 + 8 个生命周期事件 + 可取消的异步执行** |
| 命令的**可等待**与**忙碌读模型** | `CommandCompletion.cs` + `Interfaces/MVVM/IVeloxCommandCompletion.cs`、`IVeloxCommandStatus.cs` + `VeloxCommandExtensions.cs` | 2026-10-01 新增。**故意不放进 `IVeloxCommand`** —— 往接口上加成员会打碎仓内 4 个手写实现方（`WorkflowTestKit.cs:14`、`SlotEnumeratorTests.cs:12/71`、`ProbeNodes.cs:20`）以及所有库外实现方，而 `net461`/`netstandard2.0` 上没法用默认接口成员兜底。见 §九 |
| 集合属性的**订阅兜底** | `ObservableCollectionTracker.cs` | 一个静态去重订阅器，用来补「字段初始化器绕过 setter」这个洞 |
| 两者的**声明方式** | 生成器（`MVVMWriter` / `CommandWriter`）+ 两个特性 | `[VeloxProperty]` / `[VeloxCommand]` —— 都只是**给生成器看的信号**，运行期不认识它们 |

**不解决什么（这些边界常常被误以为在模块内）：**

| 不在模块内 | 实际归谁 |
|---|---|
| 属性变更通知的实现本身 | 生成器**只在没人提供时才自举**：基类链上没有 `PropertyChanged` 事件、宿主框架也没给，它就自己补事件 + `INotifyPropertyChanged` / `INotifyPropertyChanging` 接口 + `OnPropertyChanged(string)` / `OnPropertyChanging(string)` 虚方法（`Writers/MVVMWriter.cs:198-312` 决策、`:856-877` 落笔）。**所以不继承任何基类也能编译** —— 产物依据：`Src/Core/VeloxDev.Core.Extension/obj/Debug/netstandard2.0/generated/VeloxDev.Core.Generator/VeloxDev.Generators.MVVM/McpStatusViewModel_VeloxDev_AI_MCP_MVVM.g.cs`，该类 `Symbol.BaseType` 是 `object`，产物里自带了事件与两个虚方法。基类已提供时生成器只发 `OnPropertyChanging(...)` / `OnPropertyChanged(...)` 的**调用**（`Examples/MVVM/WPF/Demo/ObservableViewModelBase.cs` 就是这种情形） |
| 视图模型之外的「命令参数校验」 | 只有 `canValidate: true` 时才生成 `private partial bool CanExecute{名}Command(<参数类型> parameter);`。**2026-10-02 起参数类型跟随命令**：非 `object?` 的参数生成 `P`，`object?` 的仍生成 `object?` —— 所以校验钩子改强类型只影响「具体类型形参 + canValidate」这一种组合（仓内零处），库外是实现者要改签名的破坏性变更。该声明带 `private` 修饰符且返回非 `void` ⇒ **必须写实现体，不写就是 CS8795**（2026-10-01 最小复现验证过）。`canValidate: false` 时传的是 `canExecute: _ => true`，**不是 `null`** |
| 集合变更的**语义**（谁加了谁） | `ObservableCollectionTracker` 只负责「订上」；语义在生成器发的 `OnItemAddedTo{名}` 等 `partial void` 里（`Analizer.cs:814-817`） |
| 平台适配 | **零适配器**。`VeloxCommand` 实现的是 `System.Windows.Input.ICommand`，XAML 绑定不需要任何平台代码 —— 这是它跟 `TransitionSystem` / `WorkflowSystem` 最大的结构差异（那两个有 7 家 `adapters/`） |
| 跨线程编组 | 默认不解决。`VeloxCommand` 全用 `ConfigureAwait(false)`（`:490`、`:545`、`:584` 等），**不还原同步上下文**；事件回调在哪个线程发就看你从哪调 `Execute` |

---

## 二、VeloxCommand 相对 `ICommand` 多了什么，以及状态机

**多出来的四件事**（都不在 `ICommand` 里）：

1. **并发上限**：构造时给 `semaphore`（默认 1）。超过上限的调用**不丢弃**，而是进 `_pendingQueue` 排队。
2. **队列控制**：`Lock` / `Unlock` / `Interrupt` / `Clear` / `Continue` / `ChangeSemaphore`（各带一个 async 孪生，`:417-427` 的同步版全是 `_ = XxxAsync()`）。
3. **取消**：内部给每个 item 配 `CancellationTokenSource`，`Interrupt` / `Clear` 靠它打断正在跑的命令。
4. **8 个生命周期事件**（`CommandEventType`，`:20-32`），每个带 `CommandEventArgs`。

**一次 `Execute` 的完整状态机**（`ExecuteAsync` `:442` → `ExecuteCoreAsync` `:533` → `OnExecutionCompletedAsync` `:582`）：

```
Execute(p)
  └ Created                                   :482   ← 无论后面发生什么, 这个先发
     └ _stateLock.WaitAsync()                 :490   ← 唯一门, 锁内只推进状态、一个事件都不发
        ├ 被 Lock 挡下                     → 记 forceLocked
        ├ _active.Count < _maxConcurrency  → 进 _active, 记 startNow
        └ 否则                             → 进 _pendingQueue, 记 enqueued
        Release()                             :510
     ── 以下全在锁外 ──                            ← RaiseCommandEvent 调的是用户 handler,
        ├ forceLocked → Cts.Cancel + Canceled :516 + 释放 Cts :518 → return   (不进 _active)
        ├ enqueued    → Enqueued              :523
        └ startNow    → `_ = ExecuteCoreAsync(item)` :526
        Notify()                              :530
ExecuteCoreAsync(item)   (fire-and-forget, `_ = ...`)
  ├ Started                                   :535
  ├ await _command(...)      ← 用 item.Cts 还是静态 _defct 由 _isCtsNeeded 决定  :545/:549
  ├ Completed                                 :551
  ├ catch OperationCanceledException → Canceled  :556
  ├ catch Exception                  → Failed    :562
  └ finally → OnExecutionCompletedAsync, 然后 item.TakeCts()?.Dispose()  :577  ← 唯一释放点
OnExecutionCompletedAsync(item)
  ├ _active.Remove(item)                      :587
  ├ Exited                                    :594
  └ TryStartPendingAsync()                    ← 出队并启动, 发 Dequeued; 末句 RaiseCanExecuteChanged :821
```

**四件只在读代码时才会发现的事：**

- **`CanExecute` 与节流无关**。它只读谓词与 `_isForceLocked`（`:407-408`），**不看 `_active.Count`**。所以「队列满了」不会让按钮变灰 —— 想把「忙」反映到 UI，只能自己调 `Lock()`（它才写 `_isForceLocked`），或者读 §十一 的忙碌读模型。
- **`Notify()` 在 `ExecuteAsync` 的末尾**（`:530`，不再是 `finally`）：`Execute` 返回前 `CanExecuteChanged` 一定发过一次，**哪怕这条命令只是进了队列**。
- **同一次执行只报一条 `Canceled`**（2026-10-01 起）。`InterruptAsync`/`ClearAsync` 会主动报（及时），被中断的命令体随后在 `catch (OperationCanceledException)` 里**还会想再报一条**（晚到）—— 由 `CommandEventArgs.TryMarkCancelReported()` 的 `Interlocked.Exchange` 挡掉，**先到的那条胜出**。以前两条都发，按 `CommandEventType` 计数的 handler 会多数一次。
- **`ExecuteCoreAsync` 是 fire-and-forget**（`:526`、`:818`）：`Execute`/`ExecuteAsync` 返回时命令**可能还没跑**（排队中）或**刚跑完**。要等结果不要用 `Completed` / `Exited` 自己配对（那两条「不进 `ExecuteCoreAsync`」的路径不发 `Exited`，会永久挂起）—— 用 `ExecuteAndWaitAsync`，见 §九。

---

## 三、谁拥有状态（唯一写者）

| 状态 | 门 | 唯一写者 |
|---|---|---|
| `_active`（正在跑的） | `_stateLock` | `ExecuteAsync` `:498` 加、`OnExecutionCompletedAsync` `:587` 减、`InterruptAsync` `:652` / `ClearAsync` `:697` 清空 |
| `_pendingQueue` | `_stateLock` | `ExecuteAsync` `:503` 入队、`TryStartPendingAsync` `:805` 出队、`ClearAsync` `:701` 全部出队 |
| `_isForceLocked` | `_stateLock` | `LockCoreAsync` `:611-612`（`LockAsync` 与 `Interrupt`/`Clear` 都经它）/ `UnlockAsync` `:625` |
| `_maxConcurrency` | `_stateLock` | `ChangeSemaphoreAsync` `:784`；构造时 `:200` 校验 `semaphore >= 1`，否则抛 |
| `item.Cts` | `TakeCts()` 的 `Interlocked.Exchange` | `ExecuteAsync` 构造时 `:479`（**整个 `internal`**，`CommandEventArgs` `:855`）；取出即置 null |
| `_isCtsNeeded` | **不可变** | 只在构造/工厂里写（`:77`、`:114`、`:148`、`:167`、`:186`） |
| 代理/订阅表 | 各自锁 | 见 §五 |

**`_stateLock` 是 `SemaphoreSlim(1,1)`（`:195`），持锁期间绝不调用用户代码**。这条以前只是**写在文档里的愿望** —— 2026-10-01 之前实际有四处违反（`ExecuteAsync` 的 `Canceled`/`Enqueued`，以及 `_ = ExecuteCoreAsync(item)` 在第一个 `await` 前同步发出的 `Started`；还有 `ClearAsync` 排空循环里的 `Dequeued`）。现在 `ExecuteAsync`/`ClearAsync` 都改成 `TryStartPendingAsync` 那套：**锁内只收集，出锁后才发事件、才启动 `ExecuteCoreAsync`**。

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

1. **`_isCtsNeeded == false` ⇒ 命令不可打断。** 五种构造路径会把 `item.Cts` 留成 `null` —— 五个 `_isCtsNeeded = false` 写入点（`:77`、`:114`、`:148`、`:167`、`:186`）。此时 `Interrupt` / `Clear` 仍然会发 `Canceled` 事件、仍然会从 `_active` 摘掉它，但**底层那个 task 继续跑到底**（`it.Cts?.Cancel()` 是 null 条件调用）。只有 `CreateTaskOnlyWithCancellationToken` 拿得到真 token —— 而它**目前全仓没有任何调用方**（生成器只走 `new VeloxCommand(...)` 那条路），所以「真取消」这条路径在生产代码里是死的，只有测试覆盖。
2. **事件 handler 抛异常被吞掉，但不再无出口。** `RaiseCanExecuteChanged`（`:328`）与 `RaiseCommandEvent`（`:353`）仍然 `catch` 住不往外抛 —— 这是刻意的：`Exited` 的 handler 若把异常漏出去，`OnExecutionCompletedAsync` 会中断，**队列永远停摆**；`Completed` 的 handler 漏出去则会被 `ExecuteCoreAsync` 的 `catch (Exception)` 抓住，把一次成功误报成 `Failed`。2026-10-01 起新增静态钩子 `VeloxCommand.HandlerException`（`:223`），默认不订阅 ⇒ 行为与从前逐字节一致。**钩子自身也被 `catch` 包住**（`ReportHandlerException` `:285`），否则一个坏掉的诊断订阅者就能制造上面两种事故。
3. **`semaphore < 1` 抛 `ArgumentOutOfRangeException`**（2026-10-01 改）：构造 `:200` 的字段初始化器、`ChangeSemaphoreAsync` `:776` 开头、以及同步入口 `ChangeSemaphore` `:429` 各校验一次。**同步版必须自己校验** —— 它是 `_ = ChangeSemaphoreAsync(...)`，异常若只在 async 方法里抛就没人接得住，会变成未观察异常。以前这里是静默夹紧/静默 no-op。
4. **`Interrupt` / `Clear` 不再清掉调用方已有的锁。** 两者都经 `LockCoreAsync` `:609`，它返回「此前是否已锁」，只有此前**未**锁时才在结尾 `UnlockAsync`（`:642` 起 / `:686` 起）。2026-10-01 之前它们无条件 `UnlockAsync()`，于是对一个本来锁着的命令调 `Interrupt()` 会「取消在跑的 + 解锁 + 经 `TryStartPendingAsync` 放行整个排队队列」—— 与 `Interrupt` 的字面语义相反。仓库自带的 WPF/Avalonia demo（`Examples/MVVM/*/Demo/*ViewModel.cs` 的 `Lock(); Interrupt(); Clear(); Unlock();`）注释里假设的就是现在的语义。
5. **`Interrupt` 与 `Clear` 的差别只在排队项**：`InterruptAsync` 只清 `_active`（`:652`），`_pendingQueue` 原封不动、稍后被放行；`ClearAsync` 把两者都清（`:697-703`），先给每个排队项发 `Dequeued`（`:712`）再统一发 `Canceled`。
6. **`ContinueAsync` 在锁着时是空操作**（`:750` 起，读到已锁就直接 return）。它的存在意义是「解锁之外再踢一次队列」。
7. **`ExecuteAsync` 自己不抛**：内层 `catch` 都在 `ExecuteCoreAsync` 里。但注意 `Notify()` 已从 `finally` 挪到末尾（`:530`）—— 中间那段现在只有赋值和 `RaiseCommandEvent`（自己吞异常），所以仍不会漏发。
8. **`Notify()` 与 `RaiseCanExecuteChanged()` 是两个入口，语义相同**：`Notify()`（`:414`）就是 `RaiseCanExecuteChanged()` 的公开别名。`IVeloxCommand` 把它放进契约（`Src/Core/VeloxDev.Core/Interfaces/MVVM/IVeloxCommand.cs:18`）是为了让视图模型在**谓词外部状态**变了时手动催一次 —— 注意这是唯一能让 `CanExecuteChanged` 提前到达的途径。
9. **`CancellationTokenSource` 的所有权是「谁取出谁释放」**（2026-10-01 新增）。`CommandEventArgs.TakeCts()`（`:888`）用 `Interlocked.Exchange` 取出并置 null，**恰好一个调用者拿得到**。两条释放规则不同：
   - **正在跑的项**：只有 `ExecuteCoreAsync` 的 `finally`（`:577`）释放 —— 那是唯一能确定「命令体已经结束、没人再观察 token」的位置。`Interrupt`/`Clear` 对它们**只 `Cancel()` 不 `Dispose()`**，且 `Cancel()` 包了 `catch (ObjectDisposedException)` **与 `catch (AggregateException)`**（命令体可能已自行跑完并释放；或它注册的取消回调抛了异常 —— 后者若逃逸会跳过 `UnlockAsync`，让命令永久锁死）。**提前释放会让命令体里的 `ct.Register` 抛异常，把一次本该是 `Canceled` 的执行变成 `Failed`。**
   - **`Clear` 掉的排队项**：从不进入 `ExecuteCoreAsync`，所以只能由 `ClearAsync` 自己 `Cancel` + `Dispose`（`:721`）。漏了这一步就是纯泄漏。

---

## 六、生成器那一半

两份产物、两个 writer，文件名都是 `<类>_<命名空间>_{后缀}.g.cs`：

| 产物 | writer | 触发条件 | 内容 |
|---|---|---|---|
| `_MVVM.g.cs` | `MVVMWriter` | `MVVMProperties.Count > 0 \|\| AutoProperties.Count > 0 \|\| IsWorkflowComponent`（`MVVMWriter.cs:845`） | 属性/字段重写 + 通知调用 + 集合钩子 + 可能的事件声明 |
| `_Commands.g.cs` | `CommandWriter` | 有 `[VeloxCommand]` 方法（`CommandWriter.cs:179`） | 惰性 `{名}Command` 属性；参数类型是具体类型时为 `IVeloxCommand<P>`；方法的类型参数出现在参数类型里时改为 `Get{名}Command<T>()` 访问器（2026-10-02） |

**三件读代码才知道的事：**

- **`[VeloxProperty]` 有两条路，产物不同。** 标在**字段**上（`:96-114`，要求 `global::VeloxDev.MVVM.VeloxPropertyAttribute` 全名精确匹配）走 `MVVMFieldAnalizer`；标在**partial 属性**上（`:122-140`）走 `MVVMPropertyAnalizer`，且 `ShouldGeneratePartialProperty` 会挡掉非 partial 的。两条路都能用，但 `HasSetter` 的推导不同（`Analizer.cs:405-408` vs `:427`）。
- **两半可以同时声明，认成同一个逻辑属性（2026-10-02）。** 字段 `_id` + `partial` 属性 `Id` 同时标 `[VeloxProperty]` 时：字段路被 `ShouldGenerateFieldProperty` 挡掉（它扫**整条继承链**找同名属性，正是配对所依赖的行为，**2026-10-02 起没有改动**），属性路照常跑，但**不再声明 backing 字段** —— 它去复用那个已存在的字段。于是「默认值/初始化器放字段、访问形态与修饰符放属性」成立，而字段专属特性与属性专属特性各自留在原位。
- **复用的判据在 `MVVMWriter.ResolveBackingStorage`（新增）。** 按 `_camelCase(属性名)` 推出字段名，用 `FindFieldInHierarchy`（照同文件 `FindEventInHierarchy` / `FindMethodInHierarchy` 的范式）沿基类上溯，再用既有的 `IsAccessibleFromTarget`（`:470-486`）判可访问性。**字段标不标 `[VeloxProperty]` 都算** —— 只写属性、字段是手写私有时也复用。可用性再过滤 `static`/`const`/`readonly`（`CanBackProperty`：只读属性连 `readonly`/`const` 都能用，可写属性不能）。
- **`MVVMPropertyFactory.ShouldEmitField`（新增）取代 `IsFromField` 当发出判据。** `IsFromField` 仍然存在但已不再被 `GenerateFieldDeclaration` 读 —— 字段路恒 `false`、属性路默认 `true`，复用命中时置 `false`。`SourceName` **不做 `this.` 统一**：属性路它是裸名，同时兼任字段声明名与读写表达式，改成 `this._x` 会写出非法的 `private T this._x`。
- **类型比较必须走 `Analizer.DisplayFullTypeName`（新增的共享方法）。** 两个 analyzers 原先各写一份私有 `GetFullyQualifiedTypeName`，而字段复用处若改用 `SymbolDisplayFormat.FullyQualifiedFormat` 去比，得到的是 `string` 而 `FullTypeName` 是 `System.String` —— **每一次复用都会被误判成类型冲突**（2026-10-02 实测踩到）。现在两处私有方法都委托给这一个共享实现。
- **非法字段名不再是崩溃或坏产物。** 单字符字段名 `_` 以前会走 `fieldName[1]` 越界，把生成器整个打崩（AD0001）；`_1x` 以前会生成一个 `1x` 属性（编不过）。现在 `GetPropertyNameFromFieldName` 推不出合法标识符时返回空串，调用方报 `VELOX_MVVM_PROP002`（Warning）并跳过。
- **诊断四件套（2026-10-02，ID 由 `VELOXCMD001` 改名并扩充）**：`VELOX_MVVM_CMD001`（不支持的 `[VeloxCommand]` 签名，Error）、`VELOX_MVVM_PROP001`（`[VeloxProperty]` 声明冲突：字段/属性类型不一致、两个字段推出同一属性名、字段不可作 backing、同名非字段成员，Error）、`VELOX_MVVM_PROP002`（名字推不出合法成员，Warning）、`VELOX_MVVM_PROP003`（`[VeloxProperty]` 属性没写 `partial`，或标在索引器上，Warning）。都在 `Diagnostics.cs`；`MVVMWriter.Diagnostics` 收集、`MVVM.cs` 在 `CanWrite()` **之前**报出去 —— 被拒的声明不会进产物，只能这样让作者看到。**注意 `Diagnostics` 这个实例属性会遮蔽同名静态类**，所以 writer 里引用描述符要全限定 `VeloxDev.Generators.Diagnostics.X`（`CommandWriter` 早就是这么写的）。
- **没写 `partial` 的属性只能放弃，不能「就地补全」**（`VELOX_MVVM_PROP003`）。生成器只新增代码，改不进用户已经写死的访问器体 —— 所以 `[VeloxProperty] public string Name { get; set; }` 这种写法没有任何补救手段，只能警告。**唯一的例外是 `HasCompetingPropertyGeneratorAttribute`**：属性另有 `[ObservableProperty]` / `[Reactive]` 之类时是把职责让给了别人，属于合法跳过，**不报**（`ShouldGeneratePartialProperty` 两种 `false` 的差别就在这里）。类级缺 `partial` 仍然完全静默 —— 那在 `IsCandidateClass` 就挡掉了，writer 根本不运行，无从报起。
- **命名是功能性判据，不是风格判据。** 只写字段时不要求下划线：`name` → `Name` 是良构的，**不报警**。按风格严查会在仓库自己的代码里炸出约 130 条告警（实测：字段带下划线 64 处、不带 130 处），这是刻意的取舍。
- **不做特性复制/投射/去重。** 生成器只新增代码，改不了用户已写的源文件；两半上的特性各留原位。MVVM 路径下所有 `GetAttributes()` 都是**探测**用途，产物里不含任何特性。
- **没有「View 只透传、不发通知」这条路径** —— 曾经有（`IsView` / `GenerateProxy()`），2026-09-26 因从未被走到而整体删除（`Writers/MVVMWriter.cs:105`、`:131` 两处构造一直传 `isView: false`）。现在 `MVVMPropertyFactory.Generate()`（`Base/Analizer.cs:583`，原名 `GenerateViewModel`）是唯一出口，**所有** `[VeloxProperty]` 都按 ViewModel 形态生成通知。
- **`CanWrite()` 里含 `IsWorkflowComponent`**（`MVVMWriter.cs:845`）⇒ 一个 `[Node]` / `[Tree]` 类即使零个 `[VeloxProperty]` 也会拿到一份 MVVM 产物，但里面**不是**槽位三件套：`MVVMWriter.cs:895` 那段的条件是 `!_hasBaseWorkflowSlotInfrastructure && !IsWorkflowComponent && 任一属性 UseWorkflowSlotLifecycle`，**把 workflow 组件本身排除了**，它只服务「非组件、但继承链上有带槽位属性的类」这一种情况。真正 workflow 组件的 `CreateWorkflowSlot<T>` / `OnWorkflowSlotAdded` / `OnWorkflowSlotRemoved` 由 `Writers/WorkflowWriter.cs:899-917` 写。这是与 `Src/Core/VeloxDev.Core/WorkflowSystem/Templates/` 的耦合点。

**`[VeloxCommand]` 的方法签名决定它可不可取消**：判定在 `CommandWriter.BuildSpec`（2026-10-02 由 `ParseConstructorType` 改名并扩充），返回类型决定「值怎么变成 Task」，形参决定「走哪个构造入口」与「属性强不强类型」。入口枚举 `CommandConstruction`：

| 入口 | 何时 |
|---|---|
| `UntypedMainCtor`（`new VeloxCommand`） | 带 token 的方法组；`void` 走 `Action`/`Action<object?>` 重载 |
| `UntypedParameterOnlyFactory` | 单参数是 `object?`、无 token、返回 `Task` |
| `UntypedTokenOnlyFactory` | 单参数是 `CancellationToken` |
| `TypedMainCtor`（`new VeloxCommand<P>`） | 具体类型形参 + token |
| `TypedParameterOnlyFactory`（`CreateTypedTaskOnlyWithParameter<P>`） | 具体类型形参、无 token |

**只有形参里带 token 的两种拿得到真 token**，其余生成的命令 `Interrupt`/`Clear` 打不断（见 §五·1）。`TypedParameterOnlyFactory` 不是可选项：无 token 的强类型命令若走主构造，`_isCtsNeeded` 会从 `false` 变成 `true`，每次执行白分配一个命令体根本观察不到的 `CancellationTokenSource`，并打破 `AParameterOnlyBody_GetsNoCancellationTokenSource_ForEitherReturnType` 钉住的对称性。

**强类型只在「T 已知」时成立，分两种（2026-10-02）**：

| | T 从哪来 | 产物 | 判据 |
|---|---|---|---|
| 情形 1 | 所属类（`class Vm<T> { Task M(T x) }`） | 属性类型 `IVeloxCommand<P>`，P 可直接写出 | `CollectTypeParameters(P)` 里没有**方法**声明的类型参数 |
| 情形 2 | 方法（`Task M<T>(T x)`） | 只能生成方法 `Get{名}Command<T>()`，按类型实参用 `ConcurrentDictionary` 缓存 | 参数类型里出现了方法自己的类型参数 |

**判据必须按符号身份，不能按名字**：`class Vm<T> { Task M<T>(T x) }` 里方法的 `T` 遮蔽了类的 `T`，按名字判会错生成情形 1。四条实测结论（Roslyn 4.3.1 实编，反直觉，别推翻）：

1. **派生接口给不了编译期检查。** `IVeloxCommand<TParam, TResult> : IVeloxCommand` 时 `c.Execute(42)` 仍然编译通过（基接口的 `object?` 重载始终可达），加 `new` 隐藏也照样通过。强类型买到的是**类型信息**，不是保证 —— 这一点写在 `IVeloxCommand{TParam,TResult}.cs` 的 XML 注释里。
2. **属性类型改成强类型会打断接口实现。** `IWorkflow{Node,Slot,Tree,Link}ViewModel` 里约 20 个命令属性声明为非强类型 `IVeloxCommand`，派生接口类型**不会**隐式实现它（CS0738）。所以 `RequiresUntypedProperty` 命中时只把**属性类型**退回声明的那个类型，缓冲字段与构造保持强类型，getter 上转。
3. **强类型成员上不能加 `new`** —— CS0109（形参类型是类型参数，`Execute(T)` 与 `Execute(object)` 是两个不同重载，谈不上隐藏）。
4. **`_isCtsNeeded` 走构造参数**，不是对象初始化器 —— 管道泛型化之后 `private` 字段跨闭合类型不可写，而且构造里本来就有这个参数。

**情形 2 的访问器不可绑定**（是方法不是属性），且**每个封闭 `T` 一套队列/锁/并发上限** —— `GetXCommand<int>().Lock()` 不影响 `GetXCommand<string>()`。缓存键覆盖**全部**类型参数：只按第一个索引会让 `M<T, U>` 的不同 `U` 共用同一个实例。约束逐字抄源码文本（`where T : U` 依赖书写顺序，符号渲染容易改错）。

**命令支持最多 15 个形参（2026-10-02）**。命令只有一个实参槽，而 C# 没有可变泛型 —— 所以 N 个形参只能靠一个**载体**共享管道。载体是 `ValueTuple`（结构体，任意元数都不分配；>7 时嵌套的 `TRest` 也是结构体内联存储），外面套一层**薄门面**把它挡在公开面之外：调用点写 `cmd.ExecuteAsync(a, b, ct)`，永远看不到元组。

- **元数 = 形参个数 + 1**，最后一个类型参数永远是结果 —— 与 `Func<TResult>`、`Func<T1, TResult>` 同一套排法。上限 **15 个形参**：体是 `Func<T1..Tn, CancellationToken, Task<TResult>>`，共 n+2 个类型实参，而 `Func` 最多 17 个。
- **最小形状是两个泛型参数**：`IVeloxCommand<TParam, TResult>`。无返回值时 `TResult = object?` 且恒为 null。**1-arity 的 `IVeloxCommand<T>` 与 `VeloxCommand<T>` 已删除** —— 它们的存在会让「1 形参无结果」与「结果族」在元数上撞车。1-arity 的成员（`CanExecute/Execute/ExecuteAsync` 单参版）折进了 2-arity。
- **族是生成的**：`Src/Core/VeloxDev.Core/MVVM/CommandArities.cs`（2..15 形参，接口 + 门面各 14 个）。内容是机械的 —— 新增一档就是照抄相邻那档、把元数整体加一。上限 15 个形参，因为体是 `Func<T1..Tn, CancellationToken, Task<TResult>>` 而 `Func` 最多 17 个类型实参。
- **void 通道只留给「单形参 + 无返回值」**：arity 族没有无返回体的公开入口，多形参的 void / `Task` / `ValueTask` 体只能包一层 `async` lambda 进结果通道。**必须显式 `return default!`** —— `async` lambda 落到 `Task<T>` 目标时不允许从末尾漏出去（**CS1643**，与 `async Task<T>` 方法不同，实测过）。同步跑完的 async 由编译器缓存状态机，所以这条不额外分配。
- **校验器的形参表跟着源方法走**（类型与名字都是），`canValidate: false` 时交给构造的恒真谓词**也要跟着元数**（arity 族收 `Func<T1..Tn, bool>`，写死 `_ => true` 是 CS1593）。

**强类型入口已经不装箱了（2026-10-02）**。管道本身泛型化成了 `CommandPipeline<TParam, TResult>`，三个类做**兄弟**（它抽出来当共同基类，因为 `VeloxCommand : VeloxCommand<object?,object?>` 写不出来 —— 泛型叶子上 `IVeloxCommand<object?,object?>.ExecuteAsync(object?, ct)` 与继承来的 `IVeloxCommandResult.ExecuteAsync(object?, ct)` 同签名不同返回，CS0111）：

| 类 | 是什么 |
|---|---|
| `CommandPipeline<TParam, TResult>` | 抽象，**唯一**的管道实现：队列、锁、并发、8 个事件、两个 sink 全按 `TParam`/`TResult` 定型 |
| `VeloxCommand` | `= CommandPipeline<object?, object?>`，兜底面，构造/工厂都在这 |
| `VeloxCommand<TP,TR>` | 强类型入口 + `IVeloxCommand<TP,TR>` |
| `VeloxCommand<T>` | `: VeloxCommand<T, object?>`，参数强类型、无返回值；单独存在是因为它的体不收 token，塞进 `Task<TResult>` 会给每次执行加一层 async 包装分配 |

**免装箱的只有六个强类型入口**：`CanExecute(T)`、`Execute(T)`、`ExecuteAsync(T)`、`ExecuteAsync(T, ct)`、`Execute(T, out TR)`、强类型事件投递。`ICommand`/`IVeloxCommand`/`IVeloxCommandResult`/`IVeloxCommandCompletion` 的重载、`e.Parameter`、`CommandCompletion.Result` 按契约就是 `object?`，**仍然装箱** —— 那是兜底面。**从 XAML 绑定过来的值本来就已装箱**（`CommandParameter` 是 `object`），所以省下的只对**代码调用点**有效。

**证明而不是断言**：`CommandBoxingTests` 拿两条结构完全相同、只差 `T` 是值类型还是引用类型的命令做差分，值类型那侧不得高于引用类型那侧。它用 `GC.GetTotalAllocatedBytes(precise: true)` 而不是按线程计数 —— 管道把续体排在池线程上，按线程计数会漏掉要找的那次分配。

**事件的两副面孔**：一个类不能有两个同名公开事件，所以强类型那 8 个走 `IVeloxCommandEvents<TParam,TResult>` 的**显式实现**，类上的名字留给兜底形状。投递时**只建一份副本**，两副面孔共用；一边都没订阅就一份都不建。8 个槽是每个命令实例一份，不是每次执行。

**`HandlerException` 不能进泛型类型**：它是 static 事件，泛型类型上的静态成员是每个闭合类型一份，搬进去就等于让订阅者只收到自己那个形状的命令上报的异常。它留在非泛型的 `CommandDiagnostics` 上，`VeloxCommand` 那边的同名事件转发过去。

**兜底路径的代价略升**：每次执行的 item 现在是 `CommandEventArgs<TParam,TResult>`，强类型命令还要多带一个 `object?` 原始参数槽（用 `object?` 入口进来时值已经装箱了，但转换必须推迟到命令体调用那一刻，否则传错类型会变成同步抛而不是 `Failed`）。记忆里旧版「既有路径逐字节不变」的说法**不再成立**。

**命令体的返回值现在交得出来（2026-10-02）**。此前 `_command` 是 `Func<object?, CancellationToken, Task>`，`Task<T>` 靠**返回类型协变**绑定进来，`T` 在委托绑定那一刻就没了 —— 生成器每个 `Task<T>` 形态发的都是丢值的表达式。

- **不改 `_command`**，另加第二条内部委托 `private readonly Func<object?, CancellationToken, Task<object?>>? _resultCommand`，**默认 null**；既有 5 个构造/工厂一律不碰它，所以既有路径的行为与分配逐字节不变（`CommandAllocationTests` 是护栏）。只有 `VeloxCommand<T>` / `VeloxCommand<TP,TR>` 经一个 `protected` 构造把它填上。
- **捕获点只有一处**：`ExecuteCoreAsync` 里那两处 `await _command(...)`；返回值顺着 `item.Complete(outcome, failure, result)` 流进 sink。所以值天然属于**本次执行**，从不上命令字段。
- `CommandCompletion` 加 `object? Result` + `GetResultOrThrow()` / `GetResultOrThrow<TR>()`。它是 `public readonly struct` —— **加字段会改尺寸，对已编译的消费者是二进制破坏**。
- **接口**：`IVeloxCommandResult`（装箱通道：`Task<object?> ExecuteAsync(object?, ct)` + `void Execute(object?, out object?)`，由 `VeloxCommand` 实现，所以每条命令都有）；`IVeloxCommand<TP,TR> : IVeloxCommand<TP>`（参数与结果都不装箱）。
- **2-arity 不继承 `IVeloxCommandResult`**：两者都声明双参 `ExecuteAsync`，同时实现时除 `TP = object` 外都合法，而那正是不会生成强类型命令的组合。
- **失败语义**：`Task<TR>` / `GetResultOrThrow` **失败即抛** —— `Failed` 用 `ExceptionDispatchInfo` 重抛**原异常实例**（保栈），`Canceled` 抛 `OperationCanceledException`，`Refused` 抛 `InvalidOperationException`。这与 `ExecuteAndWaitAsync` 的「不抛、只报结局」是**两种取用方式**，不是替代。
- **同步 `Execute(p, out r)` 会阻塞调用线程**（含排队与等锁），命令内部全程 `ConfigureAwait(false)` 所以不会因同步上下文死锁，但在命令体内部对自己调用会自锁。
- **三条 C# 硬约束**（实测）：`async` + `out` 是 CS1988，所以「带 out 的 `ExecuteAsync`」写不出来，`out` 只能落在同步方法上；同名、形参相同、只有返回类型不同是 CS0111，所以异步那半必须靠**元数**区分；同名不同元数合法。

**校验器的形参名跟随源方法（2026-10-02，破坏性）**：生成的是 `CanExecute{名}Command({P} {源形参名})` —— `HandleNote(NotePayload note)` ⇒ `note`。零形参与仅 token 的方法没有源形参名可抄，仍用 `parameter`。**既有校验器若用了别的名字（仓内两个 demo 原本用 `sender`）会立刻报 CS8826**，同一笔提交里已全部改名。

**参数类型的可访问性现在会外溢（2026-10-02）**：生成的命令属性一律 `public`，而强类型属性的类型里现在带着参数类型 —— 参数类型若不可见（`internal`）就是 **CS0053**。以前 `object?` 形参不暴露任何类型，所以这条约束是新出现的。

**`CanExecute` 始终可能收到 null 实参，这不是异常路径**（2026-10-02 由 Demo 实跑暴露）：`ICommand.CanExecute(object?)` 是公开的，**WPF 在应用按钮模板时会带着 null 调一次**。于是强类型校验器有两档命运：

- **值类型的 T**：`(T)value` 在 `VeloxCommand<T>` 的适配器里就抛，校验器根本没机会执行。`CanExecute` 的异常落在 UI 线程上会**直接崩掉整个应用**。所以 `VeloxCommand<T>` 里那条校验适配器在「实参为 null 且 `default(T) is not null`」时一律答 `false`。
- **引用类型的 T**：null 原样交给校验器 —— **用户写的校验器必须自己 null 检查**（`parameter is not null && …`）。不写就是 UI 线程上的 NRE。

这是「强类型是类型信息、不是保证」在实践中的具体形态：类型签名说 `NotePayload` 非空，运行时照样能收到 null。

**`[VeloxCommand]` 的命名**：`name = "Auto"` 时用 `方法名.Replace("Async", "")`（`:84`）—— 是**全局替换**，`GetAsyncDataAsync` 会变成 `GetData`；且位置参数先读、具名参数覆盖（`:56-79`）。

**`{名}Command` 属性是惰性 + 缓存的**：`_buffer_{名}Command ??= 构造(...)`（`:221`、`:239`；声明在 `:216`/`:234`）。所以命令对象在**第一次读属性时**才建，`[VeloxCommand]` 不保证构造顺序。

---

## 七、入口：我要改 X，先打开哪个文件

> **行号锚点核对于 2026-10-01。** `VeloxCommand.cs` 改动频繁，行号会漂 —— 每个锚点旁都写了符号名，
> **按符号名核**，行号只当快速定位用。

| 想改的东西 | 先打开 |
|---|---|
| 并发/排队/锁/中断的语义 | `VeloxCommand.cs`（`ExecuteAsync` `:442`、`ExecuteAndWaitAsync` `:452`、`TryStartPendingAsync` `:794`、`InterruptAsync` `:642`、`ClearAsync` `:686`） |
| 事件在什么时候发、发几次 | 同上，`CommandEventType` `:20-32` + `RaiseCanExecuteChanged` `:328` / `RaiseCommandEvent` `:353` / `RaiseCommandEventAs` `:373` / `RaiseCanceled` `:398` + 各 stage 的调用点 |
| 锁的语义 / 「持锁期间不许干什么」 | `LockCoreAsync` `:609`（返回「此前是否已锁」）+ §三 |
| CancellationTokenSource 谁释放 | `TakeCts()` `:888` + `ExecuteCoreAsync` 的 `finally` `:577` + `ClearAsync` `:721` + §五·9 |
| handler 抛异常去哪了 | `ReportHandlerException` `:285` + `VeloxCommand.HandlerException` `:223` |
| 「这个命令能不能取消」 | `_isCtsNeeded` 的五个写入点（`:77`、`:114`、`:148`、`:167`、`:186`）+ `CommandWriter.ParseConstructorType` `:78` |
| `[VeloxProperty]` 生成出什么 | `Base/Analizer.cs` 的 `MVVMPropertyFactory`（getter `:630`、setter 前后 `:672`/`:693`、集合成员 `:761`） |
| 集合订阅的兜底 | `ObservableCollectionTracker.cs` + 上表三处生成点 |
| `[VeloxCommand]` 的命名与构造选择 | `Writers/CommandWriter.cs:84`（命名）、`:102`（构造选择）、`:149`（ValueTask thunk）、`:201`（模板） |
| 「框架基类已经把 setter 写好了」 | `Writers/MVVMWriter.cs:42-89`（CommunityToolkit / Prism / ReactiveUI / Caliburn 的 setter 模式探测） |
| 集合钩子的接缝 | `MVVMWriter.cs:834-843`（基类是否已有 `OnCollectionChanged`）+ `:883-893`（`protected virtual`） |

---

## 八、陷阱（带依据）

1. **「不继承基类就编译不过」已经过期 —— 生成器会自举通知基础设施。** `MVVMWriter.ConfigurePropertyNotificationInfrastructure`（`:198-258`）在「基类链上没有 `PropertyChanged` 事件 + 宿主框架/上层 Velox 类都没提供」时，把 `_generatePropertyChangedEvent` / `_addNotifyPropertyChangedInterface` 置真，产物因此自带事件、接口与 `OnPropertyChanged(string)`（`GenerateBody` `:874-881`、`GenerateBaseInterfaces` `:856-864`）。**判据是符号，不是「有没有基类」。** 基类存在时生成器仍然只发**调用**（`Analizer.cs:608-610` 发 `partial void On{X}Changing/Changed` 声明），事件与 `OnPropertyChanged` 由基类给。
2. **`ObservableCollectionTracker.Unsubscribe` 不「拉黑」这个 (handler, 集合) 对。** 它只做 `-= handler` 与 `entry.Remove(handler)`（`:54-57`），**不移除表项**。所以 `Unsubscribe` 之后再 `EnsureSubscribed` 同一 handler 会**重新订阅**（因为 `Entry` 还在、`TryAdd` 返回 true）。**这不是 bug，是生成器依赖的行为** —— setter 换值时 `Unsubscribe(old)`，而 getter 每次都 `EnsureSubscribed`，靠的就是这份可逆性。（旧版注释曾写成「removes its tracking entry so the subscription is not accidentally restored later」，与代码相反；2026-10-01 已按代码改写注释。`ObservableCollectionTrackerTests` 钉住了这个语义。）
3. **`Unsubscribe` 的减法依赖委托等价**：`-=` 用的是 `Delegate.Equals`（方法 + 目标的**值**比较），而 tracker 自己的去重用的是 `ReferenceEquals(Target)`（`:108`）。对普通视图模型两者一致；对 `Target` 被重写过 `Equals` 的类型，两条判定会分叉 —— 一行注释也没写，属于**只从代码看出的不一致**。
4. **`Clear` 之后再 `Continue` 没用**：`ClearAsync` 已经把 `_pendingQueue` 掏空（`:697-703`），`ContinueAsync` 只是再踢一次空队列。想「暂停队列」该用 `Lock()`。
5. **只有 `Failed` 携带 `Exception` —— `Exited` 带的是 `null`。**（2026-10-01 更正；本文旧版说「`Exited` 也带同一个 `Exception`」，**是错的**。）
   推导：`ExecuteAsync` 造出的那个 `item`（`:476`）`Exception` 恒为 `null`，且**全程不被改写** —— 所有 `With(...)` 都返回**新实例**并且只喂给 `RaiseCommandEvent`。所以 `:562` 那条 `With(..., ex)` 里的 `ex ?? Exception` 里，接收者的 `Exception` 永远是 null，内部**没有任何一处**触发过它。
   `With` 的 `ex ?? Exception`（`:915`）本身是**粘性**的：对一份已带异常的 args 再 `With(别的 stage)` 会继承下去。命令自己走不到（它只从「出厂 args」投影），只有**外部手工链式调用 `With`** 才会碰到 —— 所以它是 `With` 的契约细节，不是运行时行为。
   `VeloxCommandLifecycleTests` / `CommandEventArgsTests` 把这两条都钉住了。
6. **`CommandEventArgs.Cts` 整个是 `internal`**（2026-10-01 起，此前是公开 getter + `internal set`）。理由：命令在本次执行结束时就把它释放掉，公开读只会让人从 `Exited` 里拿到一个正在失效的对象 —— 实测全仓**零外部读取**。要让自定义命令拿到 CTS，只能在 `_command` 闭包里自己接 `CancellationToken`。
7. **`VeloxCommand` 实现了 `IDisposable`**（2026-10-01 新增），只释放 `_stateLock`。**只在确认没有在执行时调用** —— 有排队或正在跑的调用时释放，会让它的下一次取锁抛 `ObjectDisposedException`。只是被丢弃的命令**不需要**释放：那把锁没有非托管资源。生成出来的命令属性是惰性缓存的，没有谁会去释放它们，所以这个 API 目前是**给手动 teardown 用的**。
8. **`_active` 是 `HashSet<CommandEventArgs>`**（2026-10-01 由 `List<T>` 改）。`OnExecutionCompletedAsync` 的按项摘除是热路径，`List.Remove` 是 O(n) 而 `HashSet` 是 O(1)。`CommandEventArgs` 没重写 `Equals`/`GetHashCode`，所以仍是引用语义，与改动前一致。**代价**：`Interrupt`/`Clear` 收集多个在跑项时顺序不再确定 —— 但仓库里 `semaphore` 恒为 1，`_active` 至多一项。
9. **`Exited` 在池线程上触发 —— UI 处理器必须自己跳回界面线程，而且它抛的异常会被吞掉（现在至少能被钩子看见）。** `ExecuteCoreAsync` 以 `ConfigureAwait(false)` 等待命令体，所以 `RaiseCommandEvent` 发出的事件不在 UI 线程上；而它把处理器包在 `try/catch` 里**静默吞掉**异常 ⇒ 一个在 `Exited` 里写控件属性的 WPF/WinUI 处理器会抛 `InvalidOperationException`、被吞、**界面看起来只是"什么都没发生"**（典型症状：按钮永远不亮）。2026-09-27 实测（把 demo 的运行控制接进七家时撞上）：WPF 用 `Dispatcher.InvokeAsync`、WinUI 用 `DispatcherQueue.TryEnqueue`、Avalonia 用 `Dispatcher.UIThread.Post`、Blazor 用 `InvokeAsync(StateHasChanged)`、MAUI 用 `MainThread.BeginInvokeOnMainThread`。**2026-10-01 起**这些被吞的异常会经 `VeloxCommand.HandlerException` 报出来（订阅它即可定位「代码没跑也不报错」），但**吞掉本身没有变**。**要读 `CommandEventArgs` 也顺手**：委托是 `CommandEventHandler(CommandEventArgs e)` —— 一个参数，写 `+= (_, _)` 编译不过。

---

## 九、`ExecuteAndWaitAsync`：把「这一次执行真的结束」变成可等待的

**为什么需要它。** `ExecuteAsync` 只等到入队，调用方只能自己配对 `Exited` + `Failed` —— 而**那两条不进 `ExecuteCoreAsync` 的路径根本不发 `Exited`**，配对者会永久挂起（`WorkflowAgentToolkit` 的等待助手在 2026-10-01 之前就是这样）。

**全部终止点，一个都不能漏** —— 漏一个就是一条永久挂起的路径：

| 路径 | 发信号的人 | `CommandOutcome` | 发 `Exited` 吗 |
|---|---|---|---|
| 立刻跑完 / 排队后跑完 | `ExecuteCoreAsync` 的 `finally`（嵌在 `OnExecutionCompletedAsync` 外层） | `Completed` / `Failed` | 会 |
| 跑着被 `Interrupt` 打断，命令体仍收尾 | 同上 | `Canceled` | 会（体结束时） |
| 排队时被 `Clear` 丢弃 | `ClearAsync` 的排队项循环 | `Canceled` | **不会** |
| 被 `Lock` 挡下 | `ExecuteCore` 的拒绝分支 | `Refused` | **不会** |

判据：一个 item 任一时刻只处于「被拒 / 在 `_pendingQueue` / 在 `_active`」三者之一，而**凡进过 `_active` 的，`ExecuteCoreAsync` 必被调用一次且必走完 `finally`**。所以只有那两条不进 `_active` 的路径要在别处补信号。

三条纪律：

- **`InterruptAsync`/`ClearAsync` 对正在跑的项不发信号** —— 它们的命令体会自己走完 `finally`。若那里也发，一次「体不理会 token 的中断」会先被记成 `Canceled`，等体真跑完时正确的 `Completed` 已经输给 `TrySetResult`，**报出错的结局**。
- **`Failed` 的异常在 `catch` 里算出来往下传**，不从 `item` 或事件上读回来：等结果不能依赖「恰好有人订阅了 `Failed`」。`CommandEventArgs.Exception` 恒为 null（§八·5）。
- **信号在 `finally` 里，且嵌一层** —— `OnExecutionCompletedAsync` 抛异常时收尾与释放都不能被跳过。

`With(...)` 产生的副本**不带**信号槽，这是有意的：副本不该能收尾。`CommandEventArgs.Completion` 是 `internal`，公开面不变。

---

## 十、`EventContext`：事件编组（默认关）

非空且与 `SynchronizationContext.Current` 不是同一实例时，事件改走 `Post`。

- **默认关 ⇒ 行为逐字节不变**（`VeloxCommandEventContextTests` 第一条用例钉的就是这个）。
- 开启后**发事件变成异步的**：`Post` 有序，但事件可能在其触发调用返回之后才到达处理器。需要处理器「在事件触发那一刻」观察命令的话，不要开。
- 编组路径每次 `Post` 一次装箱分配；未开启时零额外分配（快路径只做一次字段读 + 引用比较）。

---

## 十一、忙碌读模型

`IsBusy` / `ActiveCount` / `PendingCount` —— `VeloxCommand` 上是属性，`IVeloxCommand` 上是 `VeloxCommandExtensions` 的扩展方法。

**为什么需要**：`CanExecute` 只读谓词与 `_isForceLocked`，**完全不看队列**（§二），所以槽位占满时按钮照样显示为可执行。`VeloxCommandStatusTests` 有一条专门把这个反直觉行为钉住。

**无锁读**：直接读 `_active.Count` / `_pendingQueue.Count`，并发下可能差一步 —— 刻意如此，属性 getter 不能阻塞在 `SemaphoreSlim` 上。
