# AspectOriented — 架构

> 代码：`Src/Core/VeloxDev.Core/AspectOriented/`（5 个 .cs），契约在 `Src/Core/VeloxDev.Core/Interfaces/AspectOriented/`（只有 `IAspectOriented.cs`，一个空标记接口）。
> 生成器：`Src/Generators/VeloxDev.Core.Generator/`。**下文不带路径的文件名都指这个目录**：`AopInterface.cs`、`AopProxy.cs`（根）、`Writers/AopWriter.cs`、`Base/Analizer.cs`、`Base/AnalizeHelper.cs`。它们**不在本模块目录下**，别在 `AspectOriented/` 里找。
> 本文只写「读完这些文件才知道的东西」。类型清单、成员表、继承树请看 IDE。

---

## 一、这个模块是什么，不解决什么

**是什么。** 给一个**已经写好的类**加一层「成员的实现可以被换掉」的入口，且不改成员自身的代码。手段是**编译期生成一个镜像接口**，运行期由 `DispatchProxy` 动态实现它 —— 调用方拿到的是代理，代理转调真身。

**两个半场，缺一不成**：

| 半场 | 位置 | 产物 |
|---|---|---|
| 编译期 | `AopInterface.cs` + `AopWriter.cs`（经 `AopProxy.cs` 驱动） | `<类>_<命名空间>_Aop`（接口，命名空间 `VeloxDev.AopInterfaces`）+ `<类>_<ns>_AopExtensions`（静态类，命名空间 `VeloxDev.AspectOriented`）+ 一句 `partial class` 声明 |
| 运行期 | `Aop.cs` / `AopCache.cs` / `ProxyEx.cs` / `ProxyInstance.cs` | 代理的创建、缓存、三个阶段的钩子表、代理→真身反查 |

**不解决什么**（这几条决定了「为什么我的切面不触发」）：

| 你以为在模块内 | 实际 |
|---|---|
| 拦截构造函数 / 字段 / 静态成员 / 事件 | **不支持**。`AspectOrientedAttribute.cs:8` 的 `AttributeUsage` 只有 `Method \| Property \| Field`，且字段那条只是给生成器看的信号 —— 拦截的是它**生成出来的属性**，不是字段读写本身 |
| 拦截「类内部对自身的调用」 | **不拦截**。代理是**另一个对象**，`this.Foo()` 永远走真身。官方解法是加一个 `AOP_` 前缀的转发方法，让内部调用改成走代理（`Examples/AOP/WPF/Demo/TeamViewModel.cs` 的 `OnMemberAdded` → `this.Aop().AOP_OnMemberAdded(...)`） |
| 拦截未标记的成员 | 生成器只把**被标记的 public 成员**放进镜像接口（`AopInterface.cs:74`、`:106`；字段那条要求同时有 MVVM 特性，`:47-48`） |
| 非 public 成员「至少能编译」 | **静默失效**：`IsAopClass` 按类回答 yes，接口里却没有那个成员，`Aop()` 照常返回、调用照常直达真身 —— 没有诊断、没有异常 |
| 重载方法 | **调用即抛**（见 §七·1） |
| 在 `netstandard2.0` / `net461` 上降级 | **不存在**。五个 .cs 全部包在 `#if NET` 里（`ProxyEx.cs:1`、`ProxyInstance.cs:1`、`AopCache.cs:1`、`Aop.cs:1`、`AspectOrientedAttribute.cs:1`），而 `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:5` 的 TargetFrameworks 是 `netstandard2.0;netframework4.6.1;net5.0;netcoreapp3.0` ⇒ 前两个 TFM 上这个命名空间**根本不存在**（编译器报「类型不存在」，不是运行期静默降级） |

**Core 与适配器都不消费它**：`Src/`、`Src/Adapters/` 下没有任何一处引用 `VeloxDev.AspectOriented`；唯一的使用者是 `Examples/AOP/WPF`、`Examples/AOP/Avalonia` 与 `skills/veloxdev-add-aspects/SKILL.md`。所以它是**纯对外能力**，没有内部回归压力。

---

## 二、代理是怎么生成的（最容易猜错的一段）

**不是 IL 手写、不是表达式树、也不是生成器造实现类**：`ProxyEx.CreateProxy<T>` 调用 `DispatchProxy.Create<T, ProxyInstance>()`（`ProxyEx.cs:20`），`T` 是那个**生成的接口**；`ProxyInstance` 是 Core 里唯一的 `DispatchProxy` 子类（`ProxyInstance.cs:8`）。真正的实现类型由 `DispatchProxy` 在运行期自己 `Reflection.Emit` 出来 —— 所以仓库里搜不到任何 `MethodBuilder`。

`partial class` 那一句**不是装饰，是必须的**：生成的 `Aop()` 里是 `CreateProxy<{接口}>(x)`（`AopWriter.cs:82`），而 `x` 是具体类的实例。`ProxyEx.CreateProxy<T>(this T target)` 的 `T` 被显式指定为接口类型，所以 `x` 必须能隐式转换成该接口 —— 只有 `AopWriter.cs:37-40` 让 `partial class` 声明实现它，这行才编译得过。

**三个消费者，两种匹配方式**（这是本模块最容易踩的一处不一致）：

| 谁 | 匹配方式 | 依据 |
|---|---|---|
| `Analizer.Filters.Targets` 决定「这个类有没有活干」 | **按符号**（`VeloxDev.AspectOriented.AspectOrientedAttribute` 是 10 个触发特性之一） | `Base/Analizer.cs:93` |
| `AopInterface` 决定「接口里有哪些成员」 | **按语法文本**：`attribute.Name.ToString() == "AspectOriented"` | `Base/AnalizeHelper.cs:52` |
| `AopWriter` 决定「要不要出接口 + Aop()」 | **按符号**（`IsAopClass(symbol)`，跨全部分片部分类看） | `Base/AnalizeHelper.cs:43-46`、`Writers/AopWriter.cs:22-25` |

- 走符号的理由写在 `AnalizeHelper.cs:19-22`：成员可能落在**另一个**分片部分类文件上，只看手上这一份会整类静默不产出。`Analizer.cs:100-106` 的备注把同一件事再说了一遍（「Attributes are resolved as symbols rather than matched by name」）。
- **但「哪一份分片被交给 writer」另有一条规定**：`Analizer.cs:75-81` 说明**类级特性排在清单前面**，目的是让「带类级特性的那一份声明」成为部分类型的**代表**——因为 `MonoWriter`、`AopWriter`、`AopInterface` 是**语法作用域**的 writer，从它们拿到的那一份声明上读特性。`AopWriter` 明知这一点仍走符号（`AopWriter.cs:19-21` 与 `:24`），于是「代表是哪一份」只影响它产出的**文件名**（`Syntax.Identifier.Text` + 命名空间），不影响判定结果。
- 走文本的后果：一个**别的**叫 `AspectOriented` 的特性同样会被当成标记；反过来，字段那条路认的 MVVM 特性也是文本匹配 `Contains("Observable") || Contains("Property")`（`AopInterface.cs:47-48`）—— `[VeloxProperty]` 与 CommunityToolkit 的 `[ObservableProperty]` 都算。
- 字段标记后，接口里出现的是**推导出的属性名**（`_name` → `Name`，`AnalizeHelper.cs:55-65`），且接口一律给 `{ get; set; }`（`AopInterface.cs:57-67`）。

---

## 三、一次拦截的完整流向

```
调用方
  │  data.Aop()                      ← 生成器写的扩展方法,每个类一个
  ▼
AopCache.Resolve<TClass,TInterface>(instance, factory)          AopCache.cs:24
  │  ConditionalWeakTable<TClass,TInterface> 命中就返回,否则跑 factory   :30
  ▼ factory (只在首次)
ProxyEx.CreateProxy<接口>(x)                                     ProxyEx.cs:17
  ├ DispatchProxy.Create<T, ProxyInstance>()                     :20   ← 真身是运行期生成的类型
  ├ dynamic 写 proxy._target / proxy._targetType                 :21-22
  └ ProxyInstance.ProxyIDs.Add(proxy, proxy._localid)            :23   ← 静态强引用表
  ▼
调用方持有代理(接口类型)
  │  安装钩子: p.SetProxy(ProxyMembers.X, "成员名", start, coverage, end)
  ▼
ProxyEx.SetPropertyGetter / SetPropertySetter / SetMethod        ProxyEx.cs:43 / :63 / :83
  └ 写进 ProxyInstance 的三个实例字典,键 = "get_X" / "set_X" / 方法名
  ▼
调用方**经代理**调用成员
  ▼
ProxyInstance.Invoke(targetMethod, args)                         ProxyInstance.cs:23
  ├ 按 methodName 前缀分派: get_ / set_ / 其余                        :29 / :37 / :46
  ├ R0 = start?.Invoke(args, null)
  ├ R1 = coverage is null ? 真身反射调用 : coverage.Invoke(args, R0)
  └ end?.Invoke(args, R1)   ← 返回值丢弃
  ▼
返回值 = R1
```

**三个阶段是「替换」而不是「环绕」**：`coverage` 非 null 时**真身完全不执行**（`ProxyInstance.cs:33` 的三元表达式），所以 `coverage` 不是"拦截器"而是"替身"。

**真身调用是反射**：`_targetType?.GetMethod(Name)?.Invoke(_target, args)`（`ProxyInstance.cs:33`/`:41`/`:49`），`_targetType` 是**接口**类型（`CreateProxy` 里 `_targetType = typeof(T)`，`ProxyEx.cs:22`），所以在接口 `MethodInfo` 上 `Invoke` 会虚分派到真实实现。这条也解释了 §七·1 的重载问题。

---

## 四、两个静态表：谁拥有什么

| 表 | 类型 | 键 / 值 | 谁写 | 生命周期 |
|---|---|---|---|---|
| `ProxyInstance.ProxyInstances` | `Dictionary<Guid, ProxyInstance>`（static） | `_localid` → 代理 | `ProxyInstance` 的构造函数（`:13`） | **只增不减**，全仓库零移除点 |
| `ProxyInstance.ProxyIDs` | `Dictionary<object, Guid>`（static） | 代理 → `_localid` | `ProxyEx.CreateProxy`（`:23`） | **只增不减** |
| `Aop._proxyToTarget` | `ConditionalWeakTable<object, object>` | 代理 → 真身 | `Aop.Map`（`Aop.cs:19`，由生成的 `Aop()` 调用） | 弱表，但见下 |
| `AopCache.Entry<TClass,TInterface>.Instances` | `ConditionalWeakTable<TClass,TInterface>` | 真身 → 代理 | `Resolve` 的 `GetValue`（`AopCache.cs:30-31`） | 弱表 |
| `ProxyInstance` 的 `GetterActions`/`SetterActions`/`MethodActions` | 三个实例字典 | 成员名 → `Tuple<start, coverage, end>` | `SetPropertyGetter`/`SetPropertySetter`/`SetMethod` | **每代理一份**，随代理一起"长生" |

**两张弱表加起来也不构成弱引用保证**：`ConditionalWeakTable` 的值不保活键 —— 但 `ProxyInstances` 这份**静态强引用表**保活着代理，而代理的 `_target` 字段（`ProxyInstance.cs:15`）保活着真身。于是真身、代理、`AopCache` 里那条表项三者一起活到进程结束。这是**代码可确证**的推断（三处字段 + 零移除点），`skills/veloxdev-add-aspects/SKILL.md:59` 与 `:179` 也把它写成了「不要读成生命周期保证」。

---

## 五、不变量

1. **`SetProxy` 必须在代理上调用，且只对已登记的代理生效**。三个内部 setter 都以 `ProxyInstance.ProxyIDs.TryGetValue(source, ...)` 开头，查不到就**原样返回 source、什么都不做**（`ProxyEx.cs:45-47`）。因为扩展方法的约束是 `where T : class, IAspectOriented`，真身类天然不满足（只有生成的接口继承 `IAspectOriented`），所以正常写法碰不到这条 —— 但它是一处**静默无操作**的兜底。
2. **键是「裸成员名」加前缀**：属性走 `get_X` / `set_X`（`ProxyEx.cs:51`、`:71`），方法走方法名原文（`:83`）。所以 `SetProxy(ProxyMembers.Getter, ...)` 与 `SetProxy(ProxyMembers.Setter, ...)` 是两条**独立**的注册，属性不会因为注册了一侧而另一侧生效。
3. **三个阶段都在 `Invoke` 里同步执行**，`start` 收到的 `previous` 恒为 `null`，`end` 收到的 `previous` 是 `R1`（`ProxyInstance.cs:32-34`）。`end` 的返回值被丢弃。
4. **`coverage` 为 null 才调真身**（`:33`）。这条是「切面生效」与「切面把功能弄没了」的唯一分界。
5. **`Invoke` 只按方法名分派，不看签名**：`MethodActions.TryGetValue(Name, ...)`（`:47`）。重名的两个成员共用一条钩子记录 —— 见 §七·1。
6. **钩子是每代理一份、可覆盖不可叠加**：`Set*` 里 `ContainsKey` 时是**赋值**不是追加（`ProxyEx.cs:54`、`:74`、`:93`），所以对同一成员再注册一次是替换上一组三元组。
7. **`Aop.Map` 用 `Add` 而非索引赋值**（`Aop.cs:20`）：同一代理被 Map 两次会抛 `ArgumentException`。生成的工厂只在缓存未命中时跑（`AopCache.cs:30`），所以正常路径只有一次。
8. **`CreateProxy` 的 `T` 必须是接口**。`DispatchProxy.Create<T, TProxy>()` 对非接口 `T` 抛异常，而这里的约束只有 `T : IAspectOriented`（`ProxyEx.cs:17`）—— 传一个真身类进去是运行期失败，不是编译期。

---

## 六、入口：我要改 X，先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 「哪些成员会出现在代理上」（可见性、字段→属性、接口形状） | `Src/Generators/VeloxDev.Core.Generator/AopInterface.cs`（字段 `:46-71`、属性 `:73-103`、方法 `:105-120`） |
| 「哪个类算 AOP 类」的判定 | `Src/Generators/VeloxDev.Core.Generator/Base/AnalizeHelper.cs`（`IsAopClass` `:43`、`HasAspectOriented` `:48`） |
| `Aop()` 扩展方法 / `partial class` 声明 | `Src/Generators/VeloxDev.Core.Generator/Writers/AopWriter.cs`（`GenerateBaseInterfaces` `:37`、`WriteExtension` `:53`） |
| 三个阶段的执行顺序与返回值语义 | `ProxyInstance.cs` 的 `Invoke`（`:23-53`） |
| 「成员名怎么变成键」（`get_`/`set_` 前缀） | `ProxyEx.cs:51`、`:71`、`:83` |
| 代理的缓存与复用 | `AopCache.cs`（`Entry<,>` `:14-19`、`Resolve` `:24`） |
| 代理→真身 | `Aop.cs`（`Map` `:19`、`GetTarget<TTarget>` `:25`） |
| 「为什么这个特性标上去没反应」 | 先看 `AopInterface.cs:74`/`:106` 的可见性过滤，再看 `AnalizeHelper.cs:52` 的文本匹配 |

---

## 七、陷阱（带依据）

1. **重载成员的调用会抛，而不是「随便挑一个」。** 真身调用是 `_targetType?.GetMethod(Name)`（`ProxyInstance.cs:33`/`:41`/`:49`），`Type.GetMethod(string)` 在多于一个同名方法时抛 `AmbiguousMatchException`。`AopWriter` 会把这**两个**重载都写进接口（`AopInterface.cs:105-120` 不按签名去重），所以「接口能编译、调用时炸」。另外三个 `*Actions` 字典都以**裸名字**为键（`ProxyInstance.cs:19-21`），两个重载共用同一条钩子记录 —— 即便不炸也分不开。
2. **一个切面到底拦的是哪个成员，取决于生成器把什么放进了接口。** 字段必须同时有 MVVM 特性与 `[AspectOriented]`（`AopInterface.cs:47-48`）；且它生成的是 `接口成员 X` ↔ 真身的 `X` 属性。只用 `[AspectOriented]` 标字段、不标 MVVM 特性 ⇒ 接口里没有该成员，**静默无效**（`AopInterface.cs:46-48` 的两条 Where 同时成立才进循环体）。
3. **`Invoke` 的前缀判定先于一切**：`Name.StartsWith("get_")`（`:29`）。所以一个真正叫 `get_X` 的方法会被当成属性 getter 处理，钩子也会去查 `GetterActions`。
4. **`dynamic` 是必需的，同时是裁剪/AOT 的不友好点**：`ProxyEx.cs:20-23` 用 `dynamic` 读写 `ProxyInstance` 的两个 `internal` 字段（`_target`/`_targetType`，`ProxyInstance.cs:15-16`），因为代理的**具体类型**运行期才知道。代价是 `CreateProxy` 这一处依赖 `Microsoft.CSharp` 运行期绑定器；`Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:12` 明写 `IsTrimmable=false`，仓库也没有 `Examples/AOP/*Trimmed` 目录（对比 `Examples/Theme/` 有两个）。**未实测**：AOT 下这条路会怎么失败，仓库里没有记录。
5. **`Invoke` 里 `Name == string.Empty` 直接返回 null**（`ProxyInstance.cs:27`）。这覆盖的是 `DispatchProxy` 能否给出 null `MethodInfo` 的边界 —— 正常调用到不了。读到它时不要以为那是「未实现成员」的通用出口。
6. **`AopInterface` 与 `AopWriter` 的产出是两个独立的 source**，分别由 `AopProxy.cs:31-38` 的两句 `AddSource` 写出，文件名是 `<类>_<ns>_AOP.g.cs` / `<类>_<ns>_AopExt.g.cs`（`AopWriter.cs:32`、`:50`）。两边的名字拼接方式必须一致（`AopWriter.cs:61-64` 与 `AopInterface.cs:35` 都手写了 `Replace('.', '_')`），改一处漏一处会在**下一个用到 AOP 的项目**里报「找不到类型」。
