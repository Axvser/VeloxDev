# AspectOriented — 扩展

> 面向「我要加一个新东西」和「我要挂一个切面」。架构见 [architecture.md](architecture.md)。
> 依据只写树里的 `文件:行`。
> **下文不带路径的文件名都指 `Src/Generators/VeloxDev.Core.Generator/`**（`Writers/AopWriter.cs`、`Base/Analizer.cs`、`Base/AnalizeHelper.cs`），不在本模块目录下。
> **本模块没有 `adapters/`，因为不存在平台轴**：五个运行期 .cs 全在 `#if NET` 内、零 GUI 引用，`Src/Adapters/` 下也无人引用它（见 architecture.md §一末）。

---

## 一、扩展点在哪

| 扩展点 | 具体成员 / 位置 | 谁调 |
|---|---|---|
| 标记一个成员 | `[AspectOriented]`（`AspectOrientedAttribute.cs:8`），可标方法 / 属性 / 字段 | 用户 |
| 拿到代理 | `Aop()` 扩展方法 —— **生成器写的，不要手写**（`Writers/AopWriter.cs:53-88`） | 用户 |
| 安装钩子 | `SetProxy(ProxyMembers, string, start, coverage, end)`（`ProxyEx.cs:26`） | 用户，构造后一次 |
| 代理 → 真身 | `Aop.GetTarget<TTarget>(proxy)`（`Aop.cs:25`） | 用户 |
| 决定「哪些成员进代理」 | `AopInterface.cs` 的三个分支（字段 `:46-71` / 属性 `:73-103` / 方法 `:105-120`） | 生成器作者 |
| 决定「哪种类算 AOP 类」 | `AnalizeHelper.IsAopClass`（`:43`） | 生成器作者 |
| 拦截语义（三阶段的顺序与返回值） | `ProxyInstance.Invoke`（`:23-53`） | 改这里等于改全仓库的切面语义 |

用户侧**唯一**真正需要记的形状：`ProxyHandler` 是 `object? (object?[]? parameters, object? previous)`（`ProxyInstance.cs:6`）。

---

## 二、官方做法 vs 看着能编译、但错的捷径

### 1. 「我把 `[AspectOriented]` 标上去了，为什么没反应」

| 看着能编译 | 实际 | 依据 |
|---|---|---|
| 在**字段**上只标 `[AspectOriented]` | 接口里什么都没有 —— 字段那条路要求**同时**有 MVVM 特性（文本匹配 `Contains("Observable") \|\| Contains("Property")`） | `AopInterface.cs:46-48`；`skills/veloxdev-add-aspects/SKILL.md:34` |
| 标 `private` / `internal` 成员 | **静默跳过，无诊断**。`IsAopClass` 按类回答「有活干」，接口里却没这个成员，`Aop()` 照常返回 | `AopInterface.cs:74`（属性 `PublicKeyword`）、`:106`（方法 `PublicKeyword`） |
| 在**接口自己**的成员上加特性 | 生成器只看类的成员；接口是产物不是输入 | `AopInterface.cs` 全程从 `INamedTypeSymbol` 的 `GetMembers()` 出发 |
| 目标是 `struct` / 静态类 | `CreateProxy` 的 `DispatchProxy.Create<T,TProxy>` 要求接口，而 `Aop()` 又把 `x` 当具体类传 —— 类必须能实现接口，静态类不能 | `ProxyEx.cs:20`、`AopWriter.cs:82` |

**官方做法**：字段标 MVVM 特性 + `[AspectOriented]`；属性 / 方法标 `[AspectOriented]` 且 **public**。属性**没有**「getter 与 setter 一起生效」这回事 —— 接口属性虽然一律给 `{ get; set; }`（`AopInterface.cs:57-67`），但钩子键是 `get_X` / `set_X` 两条独立记录（`ProxyEx.cs:51`、`:71`），必须分开装。

### 2. 「切面装上了，但调用绕过去了」

这是本模块**唯一**的核心规则，也是官方专门用一节讲的（`skills/veloxdev-add-aspects/SKILL.md:127-150`）：

| 写法 | 结果 |
|---|---|
| `data.Aop().Name = "x"` | 切面跑 |
| `data.Name = "x"` | **不跑** —— 从没碰过代理 |
| 类内部 `this.Reset()` | **不跑** —— 代理是另一个对象 |

**官方做法**：把内部调用改成走代理。demos 里的模式是造一个标记方法并转发：

```csharp
private void OnMemberAdded(object? sender, NotifyCollectionChangedEventArgs e)
    => this.Aop().AOP_OnMemberAdded(sender, e);   // 经代理, 切面才看得见
```

（`Examples/AOP/WPF/Demo/TeamViewModel.cs`、`Examples/AOP/Avalonia/Demo/`。）

**错的捷径**：在真身上装 `SetProxy` —— 真身不是已登记的代理，`ProxyIDs.TryGetValue` 查不到，三个内部 setter 全部**静默返回 source**（`ProxyEx.cs:45-47`、`:65-67`、`:85-87`）。没有异常，没有日志。

### 3. 「我想改返回值，用 `end`」

不行。三阶段里只有 `coverage` 的返回值成为成员返回值，`end` 的返回值被丢弃（`ProxyInstance.cs:32-34`；`SKILL.md:85`）。而 `coverage` 非 null 时**真身完全不执行**（`:33`）—— 「我只是想改一下结果」会顺手把成员的副作用也弄没。要改结果只能用 `coverage`，且必须自己重放真身逻辑。

**反过来的坑**：`start` 的 `previous` 恒为 `null`（getter 也一样）。写 `(_, previous) => previous + 1` 这种在 `start` 里读上一步的写法必然拿到 null。getter 的「读到的值」出现在 `end` 的 `previous`；setter 的 `end` 的 `previous` **是 null**（setter 返回 void，没有东西可传），要看写入值只能读 `parameters?[0]`（`ProxyInstance.cs:32-34`；`SKILL.md:91`）。

### 4. 「我懒得装了，直接 new 一个实现类塞过去」

不行——生成的接口**有** `partial class` 声明它，但**没有**实现任何成员，唯一实现是 `DispatchProxy` 运行期发出来的那个类型。手写一个实现类可以编译，但它不会被 `AopCache` 认作代理：`AopCache.Resolve` 的键是**真身实例**，工厂只在未命中时跑（`AopCache.cs:30-31`）—— 手写实例要么被覆盖，要么让 `SetProxy` 落到真身上（见 §2·2）。

---

## 三、给一个现有类加切面：步骤清单

1. **标记成员。** 字段：`[VeloxProperty][AspectOriented] private string _name;`（顺序无关，但两个都要有）。属性 / 方法：`[AspectOriented]` + `public`。
2. **确认类是 `partial`。** 生成器要往里加 `partial class` 声明（`AopWriter.cs:37-40`）与 `Aop()` 的宿主。生成的是 `partial` 成员，非 partial 类会直接编译失败。
3. **重新构建。** 产物两个文件：`<类>_<命名空间>_AOP.g.cs`（接口）与 `<类>_<命名空间>_AopExt.g.cs`（扩展方法 + partial 声明），由 `AopProxy.cs` 的两句 `AddSource` 写出。参考实际产物：`Examples/AOP/WPF/Demo/obj/aopgen/VeloxDev.Core.Generator/VeloxDev.Generators.AopProxy/`。
4. **在对象构造之后、一次装钩子。** `var p = obj.Aop();` 然后按需 `p.SetProxy(ProxyMembers.Getter|Setter|Method, nameof(类.成员), start, coverage, end)`。装一次够 —— 代理每实例一个且被缓存（`AopCache.cs:24-33`）。重复装同一成员是**替换**不是叠加（`ProxyEx.cs:54`、`:74`、`:93`）。
5. **把内部调用改成走代理**（若切点是被类自己调用的方法）。照抄 demos 的 `AOP_` 转发模式。
6. **不要标记重载方法**（见 §四·1）。

模板位置：`Examples/AOP/WPF/Demo/`（`TeamViewModel.cs` + `MainWindow.xaml.cs` 的 `ConfigureAOP`）与 `Examples/AOP/Avalonia/Demo/`。

---

## 四、联动清单（改这里 = 必须同步改那几处）

**这是本模块最容易漏的地方。** 生成器与运行期各有自己的一份「成员种类」概念，只在编译产物层面相遇 —— 漏一处不会报错，而是**静默少一个能力**。

### 加一种「可拦截的成员种类」（例如索引器、事件）

按顺序动这 5 处，缺一处就是静默失效：

| # | 文件 | 要加什么 | 漏了的后果 |
|---|---|---|---|
| 1 | `Src/Generators/VeloxDev.Core.Generator/AopInterface.cs` | 新分支，把该成员的接口形状写进 `VeloxDev.AopInterfaces.*` | 接口里没有它 → 钩子无处可挂，`SetProxy` 静默无操作 |
| 2 | `ProxyEx.cs` 的 `ProxyMembers` 枚举 + `SetProxy` 的 `switch` | 新枚举值 + 一个 `case` | `switch` 无 `default`，静默什么都不做 |
| 3 | `ProxyEx.cs` | 一个新的 `SetXxx`（含 `ProxyIDs` 守卫 + `*Actions` 写入） | 同上 |
| 4 | `ProxyInstance.cs` | 第四个 `Dictionary<...>` + `Invoke` 里的分派分支 | 钩子装了但 `Invoke` 永远不查它 |
| 5 | `AnalizeHelper.HasAspectOriented`（`:48`） | 若新种类走的是新特性名，要一并认 | 整类被判为「无 AOP 成员」→ 连接口都不产出 |

**注意第 4 步的顺序**：`Invoke` 现在按 `Name.StartsWith("get_")` / `"set_"` / 其余**三分**（`ProxyInstance.cs:29`、`:37`、`:46`）。加索引器之类会破坏这个互斥划分（索引器的 `MethodInfo.Name` 是 `get_Item`），必须同时决定它的**前缀**，否则会被 `get_` 那条先截走。

### 加一个「触发整个生成管线」的特性

`Base/Analizer.cs:82-94` 的 `TriggerAttributes` 是硬编码清单（`AspectOriented` 在 `:93`，MVVM 的两个在 `:91-92`）。新特性不进去 ⇒ `Filters.Targets` 从不命中该类，生成器全程不跑。这是**所有**生成器共用的表，改它会影响另外几个生成器 —— 参考 `memory/modules/VeloxDev.Core.Generator/`。

### 改「接口全名」的拼接方式

接口名在**两处**被独立算出来：

- `AopInterface.cs:35` 拼 `VeloxDev.AopInterfaces.<类>_<ns>_Aop`
- `AopWriter.cs:61-64` 拼 `<类>_<ns>_AopExtensions`，且 `GenerateBaseInterfaces` 要返回**同一个**接口名（`AopWriter.cs:37-40`）

两处都用 `Replace('.', '_')` 手写。改一处漏一处不会在本仓库报错 —— 只在**下一个真正用到 AOP 的工程**里报「类型不存在」。本仓库的 `Src/`、`Src/Adapters/` **没有任何** AOP 使用者，所以这条回归**跑不出来**，只能靠人工核对。

---

## 五、边界的实测数字与可复核性

- **重载 = 调用即抛**：`Type.GetMethod(string)` 在多重名时抛 `AmbiguousMatchException`（`ProxyInstance.cs:33`/`:41`/`:49`），且三个 `*Actions` 字典以裸名字为键（`ProxyInstance.cs:19-21`）⇒ 即便不抛也分不开两个重载。`SKILL.md:177` 把它写成「不要标记重载方法」。
- **代理不回收**：`ProxyInstances` / `ProxyIDs` 两张静态 `Dictionary` **全仓库零移除点**（`ProxyInstance.cs:10-11`，写入点 `:13` 与 `ProxyEx.cs:23`）。`SKILL.md:59`、`:179` 明确说「不要读成生命周期保证」。**注意这是一条静态读出的结论**：仓库里没有针对它的实测或基准数字。
- **`#if NET`**：五个运行期 .cs 全在 `#if NET` 内，`Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:5` 的 TFM 含 `netstandard2.0`/`netframework4.6.1` ⇒ 那两个 TFM 上命名空间不存在。`SKILL.md:22` 也这么写（「.NET 5 及以后」）。
- **裁剪**：`VeloxDev.Core.csproj:12` 是 `IsTrimmable=false`。`ProxyEx.cs:20-23` 用 `dynamic`（运行期绑定），是一个裁剪不友好的点，但**仓库里没有 AOT/裁剪场景下 AOP 的实测记录**，`Examples/AOP/` 下也没有 `*Trimmed` 变体（对比 `Examples/Theme/` 有两个）。这一条**只能存疑**，不要写成「AOT 下会失败」。
