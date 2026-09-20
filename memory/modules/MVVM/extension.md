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
| 声明命令 | `[VeloxCommand(name="Auto", canValidate=false, semaphore=1)]`（`VeloxCommandAttribute.cs:34-43`） | 用户 |
| 集合项级钩子 | 四个 `partial void OnItemAddedTo{名} / OnItemRemovedFrom{名} / OnItemMovedIn{名} / OnItemsResetIn{名}`（`Base/Analizer.cs:814-817`） | 用户实现 |
| 属性级钩子 | `partial void On{名}Changing/Changed(old, new)`（`Analizer.cs:606-610`，仅当 `HasSetter`） | 用户实现 |
| 集合总闸 | `protected virtual void OnCollectionChanged<T>(string, NotifyCollectionChangedEventArgs, IEnumerable<T>?, IEnumerable<T>?)`（`Writers/MVVMWriter.cs:889`，仅当基类没有） | 用户覆写 |
| 命令参数校验 | `canValidate: true` 时生成的 `private partial bool CanExecute{名}Command(object? parameter)`（`Writers/CommandWriter.cs:167`） | 用户实现 |
| 手工催 `CanExecuteChanged` | `IVeloxCommand.Notify()`（`Src/Core/VeloxDev.Core/Interfaces/MVVM/IVeloxCommand.cs:18`） | 用户调用 |
| 「类算不算 MVVM 目标」 | `Base/Analizer.cs:82-94` 的 `TriggerAttributes` | 生成器作者 |
| setter 写哪套通知方法 | `MVVMWriter.DetectSetterMode`（`:42-89`） | 生成器作者 |

---

## 二、官方做法 vs 看着能编译、但错的捷径

### 1. `[VeloxCommand]` 的方法签名决定你能不能打断它

| 写法 | 生成器选的构造 | `_isCtsNeeded` | `Interrupt`/`Clear` 能打断吗 |
|---|---|---|---|
| `public Task FooAsync(object? p)` | `CreateTaskOnlyWithParameter`（`CommandWriter.cs:106`） | false（`VeloxCommandAttribute.cs` 无关，`VeloxCommand.cs:30`） | **不能** —— 只有 `Canceled` 事件，task 继续跑 |
| `public Task FooAsync(CancellationToken ct)` | `CreateTaskOnlyWithCancellationToken`（`:110-112`） | **true** | 能 |
| `[VeloxCommand]` 标在别的签名上 | `new VeloxCommand(command: 方法名, ...)`（`:150`） | 视重载 | 视重载 |

**错的捷径**：写 `Task FooAsync(object? p)` 然后指望「取消」生效 —— 编译过、跑得动、`Canceled` 事件照发，但底层工作**不会停**。判定点在 `VeloxCommand.cs:30`（工厂里唯一的 `_isCtsNeeded = false`）。

**官方做法**：要可取消就把参数写成 `CancellationToken`，并在方法体里 `ct.ThrowIfCancellationRequested()`。

### 2. 「生成器会给我发 `PropertyChanged`」

**不会。** 生成器只发 `OnPropertyChanging(...)` / `OnPropertyChanged(...)` 的**调用**与 `partial void` 声明（`Analizer.cs:606-610`），`PropertyChanged` 事件本身来自**你的基类**（`MVVMWriter.cs:874-881` 只在需要时补发事件声明与声明式方法）。照抄的本仓库基类：`Examples/MVVM/WPF/Demo/ObservableViewModelBase.cs`（Avalonia demo 有孪生）。

**错的捷径**：不继承任何基类直接标 `[VeloxProperty]` —— 生成代码编译不过（找不到 `OnPropertyChanged`）。

### 3. 「用 `[VeloxProperty]` 的字段初始化器订阅集合」

`_items = []` 直接写字段，绕过 setter，**setter 里的订阅不跑**。补洞的是 getter 侧每次访问都调的 `EnsureSubscribed`（`Analizer.cs:646`）—— 这是官方设计，不是 bug。但由此推出一条反直觉结论：

> **集合属性只要从未被 getter 读过，就一直是未订阅状态。** `[VeloxProperty]` 的集合属性若没有绑定到 UI（getter 从不执行），`OnItemAddedTo{名}` 永远不触发。要强制订阅，得自己读一次属性。

### 4. 「我给 `VeloxCommand` 加了个方法，绑定里用不到」

生成的命令属性声明类型是 **`IVeloxCommand`**（`CommandWriter.cs:155-156`、`:174`），不是 `VeloxCommand`。所以 `VeloxCommand` 上的任何**不在接口里**的成员（例如 `ExecuteAsync` 之外的构造期配置）在 XAML/绑定侧不可见。加成员的正确顺序是**先加接口**（`Src/Core/VeloxDev.Core/Interfaces/MVVM/IVeloxCommand.cs`），再加实现。

### 5. 「`canValidate: true` 就够了」

生成器只在一处**声明** `private partial bool CanExecute{名}Command(object? parameter);`（`CommandWriter.cs:167`）并把它作为 `canExecute` 传进构造（`:162`）。不写实现 → C# 的 `partial` 方法被移除 → `canExecute` 变成 `null` → `CanExecute` 走 `?? true`（`VeloxCommand.cs:126`）→ **恒真**。没有诊断。

---

## 三、新增一个 X 的步骤清单

### 3a. 加一个 `[VeloxProperty]` 属性（最常见）

1. 类必须是 **`partial`**，且继承一个提供 `OnPropertyChanging` / `OnPropertyChanged` / `PropertyChanged` 的基类。
2. 字段版：`[VeloxProperty] private string _name = string.Empty;`；属性版：`[VeloxProperty] public partial string Name { get; set; }`。
3. 集合类型（实现 `INotifyCollectionChanged`）**必须**有 setter，否则生成器不发集合成员（`Analizer.cs:763-766` 的 `!HasSetter` 早退）—— 但 getter 侧仍会发 `EnsureSubscribed(...)`（`:634-637` 只看 `IsNotifyCollectionChanged`）⇒ **get-only 的集合 partial 属性会让生成代码引用一个没声明的 `On{名}CollectionChanged`**。这一步**未能编译验证**，见 §五。
4. 需要集合项级反应就实现 `partial void OnItemAddedTo{名}(IEnumerable<T> items)` 等四个（签名里的元素类型由生成器从类型的泛型 `IEnumerable<T>` 推出，`Analizer.cs:932-940`）。
5. 构建，产物 `<类>_<命名空间>_MVVM.g.cs`。

### 3b. 加一个 `[VeloxCommand]`

1. 方法可以是 `static` 吗 —— **不可以**：`ReadCommandConfig` 只遍历实例成员（`CommandWriter.cs:24` 取 `symbol.GetMembers().OfType<IMethodSymbol>()`，没有过滤 static，但生成的是 `command: {方法名}` 的**方法组**赋值，static 方法组赋给 `Func<object?,CancellationToken,Task>` 是合法的 —— 这条**未能验证**，见 §五）。
2. 选签名（见 §2·1）：要可取消就用 `CancellationToken` 单参数 + 返回 `Task`。
3. 命名：默认 `Auto` → `方法名.Replace("Async","")`（全局替换，`CommandWriter.cs:66`）。要精确定名就传 `name:`。
4. 并发上限用 `semaphore:`（`Math.Max(1, ...)` 夹，`CommandWriter.cs:73`）。
5. 需要参数校验用 `canValidate: true` + 实现 `CanExecute{名}Command`（`private partial`，`CommandWriter.cs:167`）。
6. 产物 `<类>_<命名空间>_Commands.g.cs`。

### 3c. 加一个新框架的 setter 模式（如某天接 Fody / 自家基类）

`MVVMWriter.DetectSetterMode`（`:42-89`）返回 `SetterMode` 枚举，结果塞进每个 `MVVMPropertyFactory.FrameworkSetterMode`（`:107`、`:133`）。加一家 = 加一个枚举值 + 在 `DetectSetterMode` 里加一段探测 + 在 `MVVMPropertyFactory` 里处理该模式生成什么 setter。**探测顺序有先手**：CommunityToolkit → Prism → ReactiveUI → Caliburn，靠前命中就返回（`:45`、`:56`、`:72`、`:83`）。插在中间会改掉后面几家的行为。

---

## 四、联动清单（改这里 = 必须同步改那几处）

### 加一个生命周期事件

| # | 位置 | 漏了的后果 |
|---|---|---|
| 1 | `VeloxCommand.cs:3-14` 的 `CommandEventType` | 没有值可用 |
| 2 | `VeloxCommand.cs:94-101` 的 `event CommandEventHandler?` | 无处发 |
| 3 | `Src/Core/VeloxDev.Core/Interfaces/MVVM/IVeloxCommand.cs:7-14` | **绑定侧永远看不到** —— 生成的属性类型是 `IVeloxCommand`（`CommandWriter.cs:155`） |
| 4 | 实际的 `RaiseCommandEvent(...)` 调用点（`:146`、`:154`、`:166`、`:178`、`:190`、`:194`、`:198`、`:218`、`:275`、`:297`、`:309`、`:372`） | 事件声明了但永不触发 |

注意**同一个 `CommandEventType` 有多个发射点**：`Canceled` 在 `:154`（被锁挡下）、`:194`（被取消）、`:275`（`Interrupt`）、`:309`（`Clear`）四处；`Dequeued` 在 `:297`（`Clear` 排空）与 `:372`（正常出队）两处。加事件时要决定**所有**语义上该发的点，漏一个就是「某些路径上不发」。

### 加一个 `NotifyCollectionChangedAction` 分支

生成器里有**两份**几乎相同的 `switch (e.Action)` 模板：

- `Analizer.cs:788-812`（`CollectionItemTypeName` 为空、用 `IEnumerable` 的那份）
- `Analizer.cs:851-879`（有元素类型、用 `IEnumerable<T>` 的那份）

两份里的四个 `partial void` 声明也各写了一遍（`:814-817` 与 `:881-884`）。**加一个分支必须改两处**，且两份的 `Enumerate{名}Items` 重载签名不同（`:775-782` vs `:838-845`）—— 只改一份不会报错，而是**某一类集合属性默默少一个回调**。

### 加一个触发整个生成管线的特性

`Base/Analizer.cs:82-94` 的 `TriggerAttributes` 是硬编码清单（`VeloxPropertyAttribute` 在 `:91`、`VeloxCommandAttribute` 在 `:92`）。不进去 ⇒ `Filters.Targets` 从不命中 ⇒ 生成器全程不跑。

**同一个清单被两个生成器共用**：`VeloxDev.Generators.MVVM`（`MVVM.cs:18`）与 `VeloxDev.Generators.Command`（`Command.cs:18`）都调 `Analizer.Filters.Targets(context)`，各自再靠 `CanWrite()` 过滤（`MVVMWriter.cs:845` 含 `IsWorkflowComponent`，所以两个生成器对同一个类**都可能**产出）。加一个特性进清单会**同时**喂给这两个 writer。

### 改 `_stateLock` 的持锁范围

见 [architecture.md](architecture.md) §三。规则：**任何用户回调都不许在锁内**。新增一个 `XxxAsync` 控制方法时，照 `InterruptAsync`（`VeloxCommand.cs:255-279`）与 `ClearAsync`（`:281-313`）的写法：`LockAsync` → 锁内只摘列表 → 出锁后才 `Cancel` + 发事件 → `UnLockAsync`。

### 与别的模块的联动

- **生成器版本**：本项目 `Src/Generators/VeloxDev.Core.Generator/` 的改动要落进 **9 处** `PackageReference` 才在 Release 生效（Debug 走 `ProjectReference`，analyzer 不随它传递）—— 那属于 Generator 模块，9 处的清单见 `memory/modules/VeloxDev.Core.Generator/extension.md` §四。
- **Workflow 槽位**：`MVVMWriter.cs:895-925` 会为 `UseWorkflowSlotLifecycle` 的属性发 `CreateWorkflowSlot<T>` / `OnWorkflowSlotAdded` / `OnWorkflowSlotRemoved`，其中引用 `SlotDefaultViewModel` 与 `CreateSlotCommand` —— **这条契约的归口在 WorkflowSystem**，见 `memory/modules/WorkflowSystem/`。MVVM 侧只知道「有这么三个方法要被生成」。

---

## 五、只能存疑的地方

以下几条**只由静态阅读得出**，没有编译或运行验证，也没有测试覆盖：

1. **get-only 的集合 partial 属性**：`GenerateGetter`（`Analizer.cs:634-637`）只按 `IsNotifyCollectionChanged` 就发 `EnsureSubscribed(_, On{名}CollectionChanged)`，而 `GenerateCollectionMembers`（`:763-766`）在 `!HasSetter` 时返回空 —— 于是 handler 有引用无声明。**推断**会编译失败，但未实际编译过该形态。
2. **`[VeloxCommand]` 标在 `static` 方法上**：`CommandWriter` 不过滤 `IsStatic`（`:24`），生成的是 `command: {方法名}` 方法组赋值。**推断**能编译且行为与实例方法一致，未验证。
3. **`XxxAsync` 与同步孪生的线程语义**：同步版全是 `_ = XxxAsync()`（`VeloxCommand.cs:132-137`），会**吞掉**这些控制方法可能抛出的异常。命令体异常虽被 `ExecuteCoreAsync` 收口（`:196`），但 `_stateLock` 相关的失败没有出口 —— 未实测。
4. **`ObservableCollectionTracker` 的线程安全**：类注释称「Thread-safe for concurrent getter/setter access」（`:13`），`Entry` 内部确实用 `lock (_handlers)`（`:75`、`:83`），但 `ConditionalWeakTable.GetOrCreateValue` 与 `CollectionChanged += / -=` 都**不在锁内**（`:31-34`、`:50`）。**注释与实现之间存在未覆盖的窗口**，未能判定是否有意；不要把它读成强保证。
