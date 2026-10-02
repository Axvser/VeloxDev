# MVVM — 扩展

> 面向「我要加一个新东西」和「我要写一个新视图模型」。架构见 [architecture.md](architecture.md)。
> 依据只写树里的 `文件:行`。
> **下文不带路径的文件名都指 `Src/Generators/VeloxDev.Core.Generator/`**（`Writers/MVVMWriter.cs`、`Writers/CommandWriter.cs`、`Base/Analizer.cs`），不在本模块目录下。
> **本模块没有 `adapters/`，因为不存在平台轴**：运行期只依赖 `System.Windows.Input.ICommand` 与 BCL，七家适配器里没有一行 MVVM 代码。

---

## 一、扩展点在哪

| 扩展点 | 具体成员 / 位置 | 类型 |
|---|---|---|
| 声明可观察属性 | `[VeloxProperty]`（`VeloxPropertyAttribute.cs:25`，`AttributeUsage(Field \| Property)`） | 用户 |
| 字段与属性**成对**声明同一个逻辑属性 | 同一条 `[VeloxProperty]` 同时标在字段与 `partial` 属性上：字段承载默认值与**字段专属特性**，属性承载访问形态与**属性专属特性**。属性路会复用那个字段，不再自己声明（`MVVMWriter.ResolveBackingStorage`，2026-10-02） | 用户 |
| 读生成器报出的声明冲突 | `VELOX_MVVM_PROP001`（Error）/ `VELOX_MVVM_PROP002`、`VELOX_MVVM_PROP003`（Warning），见 `Diagnostics.cs`；命令侧是 `VELOX_MVVM_CMD001` | 用户读诊断 |
| 声明命令 | `[VeloxCommand(name="Auto", canValidate=false, semaphore=1)]`（`VeloxCommandAttribute.cs:35-44`） | 用户 |
| 集合项级钩子 | 四个 `partial void OnItemAddedTo{名} / OnItemRemovedFrom{名} / OnItemMovedIn{名} / OnItemsResetIn{名}`（`Base/Analizer.cs:814-817`） | 用户实现 |
| 属性级钩子 | `partial void On{名}Changing/Changed(old, new)`（`Analizer.cs:606-610`，仅当 `HasSetter`） | 用户实现 |
| 集合总闸 | `protected virtual void OnCollectionChanged<T>(string, NotifyCollectionChangedEventArgs, IEnumerable<T>?, IEnumerable<T>?)`（`Writers/MVVMWriter.cs:889`，仅当基类没有） | 用户覆写 |
| 命令参数校验 | `canValidate: true` 时生成的 `private partial bool CanExecute{名}Command(object? parameter)`（`Writers/CommandWriter.cs:167`） | 用户实现 |
| 手工催 `CanExecuteChanged` | `IVeloxCommand.Notify()`（`Src/Core/VeloxDev.Core/Interfaces/MVVM/IVeloxCommand.cs:18`） | 用户调用 |
| 观察被吞掉的 handler 异常 | `VeloxCommand.HandlerException`（静态 `Action<Exception>?`，`VeloxCommand.cs:182`） | 用户订阅 |
| 等「这一次执行真的结束」 | `VeloxCommandExtensions.ExecuteAndWaitAsync`（`MVVM/VeloxCommandExtensions.cs`）→ `IVeloxCommandCompletion` | 用户调用 |
| 读忙碌 / 排队状态 | `VeloxCommandExtensions.{IsBusy,ActiveCount,PendingCount}` → `IVeloxCommandStatus` | 用户调用 |
| 让事件自己编组回 UI 线程 | `VeloxCommand.EventContext`（`SynchronizationContext?`，**默认 `null` = 不编组**） | 用户设置 |
| ValueTask 命令体 | `CreateTaskOnlyWithValueTaskParameter` / `...CancellationToken`，**只在 `netcoreapp3.0`/`net5.0` 上有**（`#if !NETSTANDARD2_0 && !NETFRAMEWORK`） | 用户调用 |
| 「类算不算 MVVM 目标」 | `Base/Analizer.cs:82-94` 的 `TriggerAttributes` | 生成器作者 |
| setter 写哪套通知方法 | `MVVMWriter.DetectSetterMode`（`:42-89`） | 生成器作者 |

---

## 二、官方做法 vs 看着能编译、但错的捷径

### 1. `[VeloxCommand]` 的方法签名决定你能不能打断它

| 写法 | 生成器选的构造 | `_isCtsNeeded` | `Interrupt`/`Clear` 能打断吗 |
|---|---|---|---|
| `public Task FooAsync(object? p)` | `CreateTaskOnlyWithParameter`（`CommandWriter.cs:135`） | false（`VeloxCommandAttribute.cs` 无关，`VeloxCommand.cs:77`） | **不能** —— 只有 `Canceled` 事件，task 继续跑 |
| `public Task FooAsync(CancellationToken ct)` | `CreateTaskOnlyWithCancellationToken`（`:139-141`） | **true** | 能 |
| `public ValueTask FooAsync(object? p)` 等 | 按形参走对应的 `.AsTask()` thunk（`CommandWriter.cs:149-178`）—— 四种形态与 `Task` 那四种一一对称 | `(CancellationToken)` / `(object?, CancellationToken)` 两种为 **true**，其余 false | 带 token 的两种**能** |
| `public Task FooAsync(string s, CancellationToken ct)` | `new VeloxCommand(command: (parameter, ct) => Foo((string)parameter!, ct), …)` —— **thunk 里强转** | **true** | 能 |
| `[VeloxCommand]` 标在别的签名上 | `new VeloxCommand(command: 方法名, ...)`（`:207`） | 视重载 | 视重载 |

**强转是运行期的**：`FooCommand.Execute(42)` 不会编译报错，而是变成一次 `Failed` 执行（`InvalidCastException`）。多值场景**不要**指望我们自动组元组 —— 走 DTO：`Task FooAsync(MoveArgs args, CancellationToken ct)`，同样是单参数，但编译期安全、实例可复用（零分配）。

**错的捷径**：写 `Task FooAsync(object? p)` 然后指望「取消」生效 —— 编译过、跑得动、`Canceled` 事件照发，但底层工作**不会停**。判定点在 `VeloxCommand.cs:30`（工厂里唯一的 `_isCtsNeeded = false`）。

**官方做法**：要可取消就把参数写成 `CancellationToken`，并在方法体里 `ct.ThrowIfCancellationRequested()`。

### 2. 「生成器会给我发 `PropertyChanged`」

**不会。** 生成器只发 `OnPropertyChanging(...)` / `OnPropertyChanged(...)` 的**调用**与 `partial void` 声明（`Analizer.cs:606-610`），`PropertyChanged` 事件本身来自**你的基类**（`MVVMWriter.cs:874-881` 只在需要时补发事件声明与声明式方法）。照抄的本仓库基类：`Examples/MVVM/WPF/Demo/ObservableViewModelBase.cs`（Avalonia demo 有孪生）。

**错的捷径**：不继承任何基类直接标 `[VeloxProperty]` —— 生成代码编译不过（找不到 `OnPropertyChanged`）。

### 3. 「用 `[VeloxProperty]` 的字段初始化器订阅集合」

`_items = []` 直接写字段，绕过 setter，**setter 里的订阅不跑**。补洞的是 getter 侧每次访问都调的 `EnsureSubscribed`（`Analizer.cs:639`）—— 这是官方设计，不是 bug。但由此推出一条反直觉结论：

> **集合属性只要从未被 getter 读过，就一直是未订阅状态。** `[VeloxProperty]` 的集合属性若没有绑定到 UI（getter 从不执行），`OnItemAddedTo{名}` 永远不触发。要强制订阅，得自己读一次属性。

### 4. 「我给 `VeloxCommand` 加了个方法，绑定里用不到」

生成的命令属性声明类型是 **`IVeloxCommand`**（`CommandWriter.cs:216-217`、`:234`），不是 `VeloxCommand`。所以 `VeloxCommand` 上的任何**不在接口里**的成员（例如 `ExecuteAsync` 之外的构造期配置）在 XAML/绑定侧不可见。加成员的正确顺序是**先加接口**（`Src/Core/VeloxDev.Core/Interfaces/MVVM/IVeloxCommand.cs`），再加实现。

### 5.「`canValidate: true` 就够了」——不够，不写实现直接编译不过

生成器只在一处**声明** `private partial bool CanExecute{名}Command(object? parameter);`（`CommandWriter.cs:228`）并把它作为 `canExecute` 传进构造（`:223`）。这个声明带 `private` 可访问性修饰符、返回类型非 `void` ⇒ C# 要求它**必须有实现部分**，不写就是 **CS8795 编译失败**（2026-10-01 最小复现验证：`error CS8795: 分部方法…必须具有实现部分，因为它具有可访问性修饰符`）。

**「空钩子 → `CanExecute` 恒真」这条路径不存在** —— 它连编译都过不去。仓库内的写法见 `Examples/MVVM/WPF/Demo/MainWindowViewModel.cs:78`（作者在 `:77` 自注「This partial method must be implemented at this point」）。

顺带：`canValidate: false` 时传的是 `canExecute: _ => true`（`CommandWriter.cs:241`），**不是 `null`** —— 所以 `VeloxCommand.CanExecute` 里的 `?? true`（`VeloxCommand.cs:408`）对生成出来的命令走不到，那是留给手写 `new VeloxCommand(...)` 的。

---

## 三、新增一个 X 的步骤清单

### 3a. 加一个 `[VeloxProperty]` 属性（最常见）

1. 类必须是 **`partial`**，且继承一个提供 `OnPropertyChanging` / `OnPropertyChanged` / `PropertyChanged` 的基类。
2. 字段版：`[VeloxProperty] private string _name = string.Empty;`；属性版：`[VeloxProperty] public partial string Name { get; set; }`。
   **两半可以同时写**（2026-10-02）：`[VeloxProperty] private string _name = "默认值";` + `[VeloxProperty] public partial string Name { get; protected set; }` ⇒ 默认值取字段、访问器取属性、字段不重复声明。属性路**只按约定名**（`_camelCase(属性名)`）找字段，找到可访问的就复用 —— **字段标不标 `[VeloxProperty]` 都算**。名字不合约定（`m_id` 之类）不会被复用，会另声明一个 `_name`，与手写的 `m_id` 并存，互不干扰。
   名称的检查是**功能性**的：`name` → `Name` 良构、不报警；只有推不出合法标识符（`_`、`_1x`）才报 `VELOX_MVVM_PROP002`。冲突（类型不一致 / 两字段撞同一属性名 / 字段是 `readonly`·`const`·`static` 却要支撑可写属性）报 `VELOX_MVVM_PROP001` 并**不生成**。
   **属性必须写 `partial`**，否则报 `VELOX_MVVM_PROP003`（Warning）—— 属性体已经写死，生成器只能新增代码、改不进已有访问器，没有任何补救手段。例外：属性另有 `[ObservableProperty]` / `[Reactive]` 这类竞争生成器特性时是**合法让渡**，静默跳过不报。索引器同理报 PROP003（索引器无法 partial）。
3. 集合类型（实现 `INotifyCollectionChanged`）**必须**有 setter，否则生成器不发集合成员（`Analizer.cs:763-766` 的 `!HasSetter` 早退）—— 但 getter 侧仍会发 `EnsureSubscribed(...)`（`:634-637` 只看 `IsNotifyCollectionChanged`）⇒ **get-only 的集合 partial 属性会让生成代码引用一个没声明的 `On{名}CollectionChanged`**。这一步**未能编译验证**，见 §五。
4. 需要集合项级反应就实现 `partial void OnItemAddedTo{名}(IEnumerable<T> items)` 等四个（签名里的元素类型由生成器从类型的泛型 `IEnumerable<T>` 推出，`Analizer.cs:932-940`）。
5. 构建，产物 `<类>_<命名空间>_MVVM.g.cs`。

### 3b. 加一个 `[VeloxCommand]`

1. 方法可以是 `static` 吗 —— **不可以**：`ReadCommandConfig` 只遍历实例成员（`CommandWriter.cs:24` 取 `symbol.GetMembers().OfType<IMethodSymbol>()`，没有过滤 static，但生成的是 `command: {方法名}` 的**方法组**赋值，static 方法组赋给 `Func<object?,CancellationToken,Task>` 是合法的 —— 这条**未能验证**，见 §五）。
2. 选签名（见 §2·1）：要可取消就用 `CancellationToken` 单参数 + 返回 `Task`。
3. 命名：默认 `Auto` → `方法名.Replace("Async","")`（全局替换，`CommandWriter.cs:66`）。要精确定名就传 `name:`。
4. 并发上限用 `semaphore:`（`Math.Max(1, ...)` 夹，`CommandWriter.cs:73`）。
5. 需要参数校验用 `canValidate: true` + **必须**实现 `CanExecute{名}Command`（`private partial bool`，`CommandWriter.cs:167`）—— 它是 `private` + 非 `void` 的 partial，**不实现就 CS8795 编译失败**（见 §2·5）。
6. 产物 `<类>_<命名空间>_Commands.g.cs`。

### 3c. 加一个新框架的 setter 模式（如某天接 Fody / 自家基类）

`MVVMWriter.DetectSetterMode`（`:42-89`）返回 `SetterMode` 枚举，结果塞进每个 `MVVMPropertyFactory.FrameworkSetterMode`（`:107`、`:133`）。加一家 = 加一个枚举值 + 在 `DetectSetterMode` 里加一段探测 + 在 `MVVMPropertyFactory` 里处理该模式生成什么 setter。**探测顺序有先手**：CommunityToolkit → Prism → ReactiveUI → Caliburn，靠前命中就返回（`:45`、`:56`、`:72`、`:83`）。插在中间会改掉后面几家的行为。

---

## 四、联动清单（改这里 = 必须同步改那几处）

### 加一个生命周期事件

| # | 位置 | 漏了的后果 |
|---|---|---|
| 1 | `VeloxCommand.cs:20-32` 的 `CommandEventType` | 没有值可用 |
| 2 | `VeloxCommand.cs:228-242` 的 `event CommandEventHandler?` | 无处发 |
| 3 | `Src/Core/VeloxDev.Core/Interfaces/MVVM/IVeloxCommand.cs:7-14` | **绑定侧永远看不到** —— 生成的属性类型是 `IVeloxCommand`（`CommandWriter.cs:217`） |
| 4 | 实际的 `RaiseCommandEvent(...)` 调用点（`ExecuteAsync` `:482`/`:516`/`:523`；`ExecuteCoreAsync` `:535`/`:551`/`:556`/`:562`；`OnExecutionCompletedAsync` `:594`；`InterruptAsync` `:676`；`ClearAsync` `:712`/`:720`/`:740`；`TryStartPendingAsync` `:817`） | 事件声明了但永不触发 |

注意**同一个 `CommandEventType` 有多个发射点**：`Canceled` 有**五处** —— `:516`（被锁挡下）、`:556`（被取消）、`:676`（`Interrupt`）、`:720`（`Clear` 的排队项）、`:740`（`Clear` 的在跑项）；`Dequeued` 有两处 —— `:712`（`Clear` 排空）与 `:817`（正常出队）。加事件时要决定**所有**语义上该发的点，漏一个就是「某些路径上不发」。

**但 `Canceled` 的五处不等于一次执行会发五条**：2026-10-01 起由 `RaiseCanceled` 经 `CommandEventArgs.TryMarkCancelReported()` 去重，**一次执行只发一条，先到的那条胜出**。加新发射点时走 `RaiseCanceled` 而不是直接 `RaiseCommandEventAs(Canceled, …)`。

**新增第 5 条纪律**：新加的发事件点必须落在**锁外**，否则会重新引入死锁（见 [architecture.md](architecture.md) §三）。照 `TryStartPendingAsync` 的写法：锁内只收集，`Release()` 之后才发。

### 加一个 `NotifyCollectionChangedAction` 分支

生成器里有**两份**几乎相同的 `switch (e.Action)` 模板：

- `Analizer.cs:788-812`（`CollectionItemTypeName` 为空、用 `IEnumerable` 的那份）
- `Analizer.cs:851-879`（有元素类型、用 `IEnumerable<T>` 的那份）

两份里的四个 `partial void` 声明也各写了一遍（`:814-817` 与 `:881-884`）。**加一个分支必须改两处**，且两份的 `Enumerate{名}Items` 重载签名不同（`:775-782` vs `:838-845`）—— 只改一份不会报错，而是**某一类集合属性默默少一个回调**。

### 加一个触发整个生成管线的特性

`Base/Analizer.cs:82-94` 的 `TriggerAttributes` 是硬编码清单（`VeloxPropertyAttribute` 在 `:91`、`VeloxCommandAttribute` 在 `:92`）。不进去 ⇒ `Filters.Targets` 从不命中 ⇒ 生成器全程不跑。

**同一个清单被两个生成器共用**：`VeloxDev.Generators.MVVM`（`MVVM.cs:18`）与 `VeloxDev.Generators.Command`（`Command.cs:18`）都调 `Analizer.Filters.Targets(context)`，各自再靠 `CanWrite()` 过滤（`MVVMWriter.cs:845` 含 `IsWorkflowComponent`，所以两个生成器对同一个类**都可能**产出）。加一个特性进清单会**同时**喂给这两个 writer。

### 改 `_stateLock` 的持锁范围

见 [architecture.md](architecture.md) §三。规则：**任何用户回调都不许在锁内**。新增一个 `XxxAsync` 控制方法时，照 `InterruptAsync`（`VeloxCommand.cs:642`）与 `ClearAsync`（`:686`）的写法：`LockAsync` → 锁内只摘列表 → 出锁后才 `Cancel` + 发事件 → `UnlockAsync`。

### 与别的模块的联动

- **生成器版本**：本项目 `Src/Generators/VeloxDev.Core.Generator/` 的改动要落进 **9 处** `PackageReference` 才在 Release 生效（Debug 走 `ProjectReference`，analyzer 不随它传递）—— 那属于 Generator 模块，9 处的清单见 `memory/modules/VeloxDev.Core.Generator/extension.md` §四。
- **Workflow 槽位**：`MVVMWriter.cs:895-925` 会为 `UseWorkflowSlotLifecycle` 的属性发 `CreateWorkflowSlot<T>` / `OnWorkflowSlotAdded` / `OnWorkflowSlotRemoved`，其中引用 `SlotDefaultViewModel` 与 `CreateSlotCommand` —— **这条契约的归口在 WorkflowSystem**，见 `memory/modules/WorkflowSystem/`。MVVM 侧只知道「有这么三个方法要被生成」。

---

## 五、只能存疑的地方

以下几条**只由静态阅读得出**，没有编译或运行验证，也没有测试覆盖：

1. **get-only 的集合 partial 属性**：`GenerateGetter`（`Analizer.cs:634-637`）只按 `IsNotifyCollectionChanged` 就发 `EnsureSubscribed(_, On{名}CollectionChanged)`，而 `GenerateCollectionMembers`（`:763-766`）在 `!HasSetter` 时返回空 —— 于是 handler 有引用无声明。**推断**会编译失败，但未实际编译过该形态。
2. **`[VeloxCommand]` 标在 `static` 方法上**：`CommandWriter` 不过滤 `IsStatic`（`:42`），生成的是 `command: {方法名}` 方法组赋值。**推断**能编译且行为与实例方法一致，未验证。
3. **`XxxAsync` 与同步孪生的线程语义**：同步版全是 `_ = XxxAsync()`（`VeloxCommand.cs:417-427`），会**吞掉**这些控制方法可能抛出的异常。命令体异常被 `ExecuteCoreAsync` 收口，`_stateLock` 相关的失败没有出口 —— 未实测。
   2026-10-01 补一条已修的例子：`ChangeSemaphore` 原来只在 async 方法里校验，而同步版丢掉 task ⇒ 越界参数会变成未观察异常、**静默失效**。现在校验同时放在同步入口（`:429`）与 async 方法（`:776`）两处。**再往这套 API 上加校验时照此办理**：凡是同步孪生能触达的失败，都不能只写在 async 方法里。
4. **`ObservableCollectionTracker` 的线程安全**：类注释称「Thread-safe for concurrent getter/setter access」（`:13`），`Entry` 内部确实用 `lock (_handlers)`（`:79`、`:87`），但 `ConditionalWeakTable.GetOrCreateValue` 与 `CollectionChanged += / -=` 都**不在锁内**（`:31-34`、`:54`）。**注释与实现之间存在未覆盖的窗口**，未能判定是否有意；不要把它读成强保证。
