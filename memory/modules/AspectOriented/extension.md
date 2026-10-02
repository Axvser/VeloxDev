# AspectOriented — 扩展

> 面向「我要加一个新东西」和「我要挂一个切面」。架构见 [architecture.md](architecture.md)。
> 依据只写树里的 `文件:行`。
> **下文不带路径的文件名都指 `Src/Generators/VeloxDev.Core.Generator/`**（`AopSurface.cs`、`AopProxy.cs`、`Writers/AopWriter.cs`、`Base/AopNames.cs`、`Base/AnalizeHelper.cs`、`Base/Analizer.cs`），不在本模块目录下。
> **本模块没有 `adapters/`，因为不存在平台轴**：五个运行期 .cs 全在 `#if NET` 内、零 GUI 引用，`Src/Adapters/` 下也无人引用它（见 architecture.md §一末）。

---

## 一、扩展点在哪

| 扩展点 | 具体成员 / 位置 | 谁调 |
|---|---|---|
| 标记一个成员 | `[AspectOriented]`（`AspectOrientedAttribute.cs:8`），可标方法 / 属性 / 字段 | 用户 |
| 拿到代理 | `Aop()` 扩展方法 —— **生成器写的，不要手写**（`Writers/AopWriter.cs:52-89`） | 用户 |
| 安装钩子 | `SetProxy(ProxyMembers, string, start, coverage, end)`（`ProxyEx.cs:50`） | 用户，构造后一次 |
| 代理 → 真身 | `Aop.GetTarget<TTarget>(proxy)`（`Aop.cs:25`） | 用户 |
| 决定「哪些成员进代理」 | `AopSurface.cs` 的三个分支（字段 `:94-110` / 属性 `:113-134` / 方法 `:137-160`） | 生成器作者 |
| 决定「哪种类算 AOP 类」 | `AnalizeHelper.IsAopClass`（`:43`） | 生成器作者 |
| 拦截语义（三阶段的顺序与返回值） | `AopSurface.WriteProxy`（`:198-366`） | 改这里等于改全仓库的切面语义 |
| 代理的安装面（键 → 槽位） | 生成的 `IAopHookTarget.SetHooks`（`AopSurface.cs:337-362`） | 生成器作者 |

用户侧**唯一**真正需要记的形状：`ProxyHandler` 是 `object? (object?[]? parameters, object? previous)`（`AspectHooks.cs:17`）。

---

## 二、官方做法 vs 看着能编译、但错的捷径

### 1. 「我把 `[AspectOriented]` 标上去了，为什么没反应」

| 看着能编译 | 实际 | 依据 |
|---|---|---|
| 在**字段**上只标 `[AspectOriented]` | 接口里什么都没有 —— 字段那条路要求**同时**有 MVVM 特性（文本匹配 `Contains("Observable") \|\| Contains("Property")`） | `AopSurface.cs:95-96`；`skills/veloxdev-add-aspects/SKILL.md:34` |
| 标 `private` / `internal` 成员 | **静默跳过，无诊断**。类被判为 AOP 类，接口里却没这个成员，`Aop()` 照常返回 | `AopSurface.cs:114`（属性 `PublicKeyword`）、`:138`（方法 `PublicKeyword`） |
| 类**不是 `partial`** | `Aop()` 根本不生成，调用方报 CS1061「未包含 Aop 的定义」。这不是 AOP 自己的要求，是整条生成管线的：`Analizer.Filters.IsCandidateClass`（`Base/Analizer.cs:165-169`，经 `:122` 过滤）只放行 partial 类 | 实测 |
| 目标是 `struct` / 静态类 | 生成器按 `ClassDeclarationSyntax` 走（`Analizer.cs:167`），静态类/结构体不在其中 | `Base/Analizer.cs:165-169` |

**官方做法**：字段标 MVVM 特性 + `[AspectOriented]`；属性 / 方法标 `[AspectOriented]` 且 **public**；类保持 `partial`。属性**没有**「getter 与 setter 一起生效」这回事 —— 钩子键是 `get_X` / `set_X` 两条独立记录（`ProxyEx.cs:69-70`），必须分开装。

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

**错的捷径 —— 但这条捷径现在被编译期堵住了**：在真身上装 `SetProxy` 过不去 `where T : IAspectOriented`（`ProxyEx.cs:57`），因为用户类不再实现生成接口（`AopWriter.GenerateBaseInterfaces()` 返回 `[]`，`AopWriter.cs:40`）。这一条从「静默无操作」变成了**编译错误**，是本模块最值得记住的一处改善。

剩下那半是运行期守卫：`target is not IAopHookTarget` ⇒ 抛 `InvalidOperationException`（`ProxyEx.cs:60-65`），针对的是**手写的** `IAspectOriented`（空标记接口谁都能实现）。

### 3. 「我想改返回值，用 `end`」

不行。三阶段里只有 `coverage` 的返回值成为成员返回值，`end` 的返回值被丢弃（生成的成员体；`SKILL.md:85`）。而 `coverage` 非 null 时**真身完全不执行** —— 「我只是想改一下结果」会顺手把成员的副作用也弄没。要改结果只能用 `coverage`，且必须自己重放真身逻辑。

**反过来的坑**：`start` 的 `previous` 恒为 `null`（getter 也一样）。写 `(_, previous) => previous + 1` 这种在 `start` 里读上一步的写法必然拿到 null。getter 的「读到的值」出现在 `end` 的 `previous`；setter 的 `end` 的 `previous` **是 null**（setter 返回 void，没有东西可传），要看写入值只能读 `parameters?[0]`（`SKILL.md:91`）。

### 4. 「我懒得装了，直接 new 一个实现类塞过去」

生成的接口**没有**实现体 —— 实现类只有一个，就是生成器产出的那个 `internal sealed class <接口>Proxy`。手写一个实现类可以编译，但它不会被 `AopCache` 认作代理：`AopCache.Resolve` 的键是**真身实例**，工厂只在未命中时跑（`AopCache.cs:31`）—— 手写实例要么被覆盖，要么让 `SetProxy` 落到真身上（见 §2·2）。

---

## 三、给一个现有类加切面：步骤清单

1. **标记成员。** 字段：`[VeloxProperty][AspectOriented] private string _name;`（顺序无关，但两个都要有）。属性 / 方法：`[AspectOriented]` + `public`。
2. **确认类是 `partial`。** 不是为了 AOP —— 是共享管线只放行 partial 类（`Base/Analizer.cs:165-169`）；非 partial 类连 `Aop()` 都不会生成。
3. **重新构建。** 产物三个文件，都在 `VeloxDev.AopInterfaces` / `VeloxDev.AspectOriented` 命名空间下：`<类>_<ns段>_Aop.g.cs`（接口）、`<类>_<ns段>_AopProxy.g.cs`（代理实现）、`<类>_<ns段>_AopExt.g.cs`（扩展方法）。
   **要看到它们得加 `-p:EmitCompilerGeneratedFiles=true`**，否则根本不落盘 —— 落点是 `obj/<配置>/<TFM>/generated/VeloxDev.Core.Generator/VeloxDev.Generators.{AopSurface,AopProxy}/`。文件不在那里**不是**「生成器没跑」的证据（同理见 `Docs/…/06_tickable/01_install/index.md`）。
4. **在对象构造之后、一次装钩子。** `var p = obj.Aop();` 然后按需 `p.SetProxy(ProxyMembers.Getter|Setter|Method, nameof(类.成员), start, coverage, end)`。装一次够 —— 代理每实例一个且被缓存（`AopCache.cs:41`）。重复装同一成员是**替换**不是叠加（生成的 `SetHooks` 是赋值，`AopSurface.cs:343`）。
5. **把内部调用改成走代理**（若切点是被类自己调用的方法）。照抄 demos 的 `AOP_` 转发模式。
6. **不要标记重载方法、也不要标记表达式体属性**（见 §四·1、§四·2）。

模板位置：`Examples/AOP/WPF/Demo/`（`TeamViewModel.cs` + `MainWindow.xaml.cs` 的 `ConfigureAOP`）与 `Examples/AOP/Avalonia/Demo/`。

---

## 四、联动清单（改这里 = 必须同步改那几处）

**这是本模块最容易漏的地方。** 生成器与运行期各有自己的一份「成员种类」概念，只在编译产物层面相遇 —— 漏一处不会报错，而是**静默少一个能力**。

### 1. 加一种「可拦截的成员种类」（例如索引器、事件）

按顺序动这 5 处，缺一处就是静默失效：

| # | 文件 | 要加什么 | 漏了的后果 |
|---|---|---|---|
| 1 | `AopSurface.cs` | 新分支，把该成员的接口形状与代理实现一起产出（**同一次遍历**，见 `Hookable` `:42`） | 接口里没有它 → 钩子无处可挂 |
| 2 | `ProxyEx.cs` 的 `ProxyMembers` 枚举（`:8`）+ `SetProxy` 的键推导 `switch`（`:67-72`） | 新枚举值 + 一个 `case` | `switch` 走 `_` 分支，键算错 → `SetHooks` 抛 `ArgumentOutOfRangeException` |
| 3 | `AopSurface.cs` 的 `Slot`（`:195-196`） | 新键要能推导出**唯一且合法**的字段名 | 两个成员撞进同一个槽位 → CS0102（这正是重载现在的失败形态） |
| 4 | 生成的 `SetHooks` switch（`AopSurface.cs:337-362`） | 新增 `case` | 装了也不生效，且被 `default` 抛出去 |
| 5 | `AnalizeHelper.HasAspectOriented`（`:48`） | 若新种类走的是新特性名，要一并认 | 整类被判为「无 AOP 成员」→ 连接口都不产出 |

**注意第 3 步**：槽名现在直接由**成员键**推导（`Slot`），所以「键唯一」与「字段唯一」是同一件事。加一种成员时若不慎让两个成员的键相同，失败发生在生成代码的编译期，而不是运行期。

### 2. 加一个「触发整个生成管线」的特性

`Base/Analizer.cs:90-102` 的 `TriggerAttributes` 是硬编码清单（`AspectOriented` 在 `:101`）。新特性不进去 ⇒ `Filters.Targets` 从不命中该类，生成器全程不跑。这是**所有**生成器共用的表，改它会影响另外几个生成器 —— 参考 `memory/modules/VeloxDev.Core.Generator/`。

### 3. 改「接口 / 代理 / 命名空间段」的拼接方式

**只有一个地方**：`Base/AopNames.cs`。接口名与代理名由 `InterfaceFor` / `ProxyFor` 派生（`ProxyFor` = `InterfaceFor + "Proxy"`），扩展类名用的是同一个 `Segment`（`AopWriter.cs:58`）。这三者必须对同一个类给出一致的名字 —— 历史上它们曾在三处各算一遍、且在全局命名空间下产出非法的 `<global namespace>`（宿主只报 CS8785）。**不要在任何一处重新手写拼接**；要改就改 `AopNames`。

本仓库的 `Src/`、`Src/Adapters/` **没有任何** AOP 使用者，所以这类回归**跑不出来** —— 但 `Src/Core/VeloxDev.Core.Test/AspectOriented/` 与 `Examples/AOP/*` 会先红。

---

## 五、边界的实测数字与可复核性

- **重载 = 生成代码编译失败**：两个同名方法撞同一个 `_hooks_{名}` 字段（`AopSurface.cs:195-196`）⇒ CS0102；`SetHooks` 的 switch 也会撞重复 `case`。实测复现。`SKILL.md:177` 把它写成「不要标记重载方法」。
- **表达式体属性 = 生成代码编译失败**：`AccessorList` 为 null 时 `hasGetter` / `hasSetter` 都是 false（`AopSurface.cs:119-126`）⇒ 生成 `{ }` ⇒ CS0548，接口与代理各报一次。实测复现（`public string X => "x";`）。
- **代理会回收**：两张静态强引用表（`ProxyInstances` / `ProxyIDs`）随旧实现删除，现在只剩两个 `ConditionalWeakTable`。实测钉在 `Src/Core/VeloxDev.Core.Test/AspectOriented/AopProxyTests.AProxyIsCollectableOnceItsTargetIs`（弱引用 + 强制 GC，跑三次稳定通过）。**这条是为重构回归写的** —— 重新引入静态注册表只会让这一条红。
- **AOT 可用**：NativeAOT 发布 + 运行实测通过，零 IL 警告。做法、环境坑（`vswhere.exe` 不在 PATH）与 IL2091 的成因写在 architecture.md §八。
- **`#if NET`**：五个运行期 .cs 全在 `#if NET` 内，`Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:5` 的 TFM 含 `netstandard2.0`/`netframework4.6.1` ⇒ 那两个 TFM 上命名空间不存在。`SKILL.md:22` 也这么写（「.NET 5 及以后」）。
