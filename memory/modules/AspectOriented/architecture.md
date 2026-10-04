# AspectOriented — 架构

> 代码：`Src/Core/VeloxDev.Core/AspectOriented/`（5 个 .cs），契约在 `Src/Core/VeloxDev.Core/Interfaces/AspectOriented/IAspectOriented.cs`（一个空标记接口）。
> 生成器：`Src/Generators/VeloxDev.Core.Generator/`。**下文不带路径的文件名都指这个目录**：`AopSurface.cs`、`AopProxy.cs`（根）、`Writers/AopWriter.cs`、`Base/AopNames.cs`、`Base/AnalizeHelper.cs`、`Base/Analizer.cs`。它们**不在本模块目录下**，别在 `AspectOriented/` 里找。
> 本文只写「读完这些文件才知道的东西」。类型清单、成员表、继承树请看 IDE。

---

## 一、这个模块是什么，不解决什么

**是什么。** 给一个**已经写好的类**加一层「成员的实现可以被换掉」的入口，且不改成员自身的代码。手段是**编译期生成一对类型**：一个镜像接口，和一个实现它的代理类；调用方从 `Aop()` 拿到代理，代理转调真身。

**代理是消费者程序集里的普通生成代码。** 这条决定了本模块的全部性格，也是它与旧实现的唯一分野：

| | 旧（已删除） | 现 |
|---|---|---|
| 代理从哪来 | `DispatchProxy.Create<T, ProxyInstance>()`，运行期 `Reflection.Emit` 造类型 | 生成器直接写一个 `internal sealed class`（`AopSurface.cs:215`） |
| 真身怎么调 | `MethodInfo.Invoke`（`_targetType.GetMethod(Name)`） | 生成代码里写死的 `_target.X(...)`（`AopSurface.cs:305`） |
| 怎么拿到真身字段 | `dynamic` 写 `proxy._target`（依赖 `Microsoft.CSharp` 运行期绑定器） | 构造函数参数，无绑定器 |
| NativeAOT | 不可用（`Reflection.Emit` + `dynamic`） | **可用，已实测**（见 §八） |

**两个半场，缺一不成**：

| 半场 | 位置 | 产物 |
|---|---|---|
| 编译期 | `AopSurface.cs`（接口 + 代理实现，经 `AopProxy.cs:33` 驱动）+ `AopWriter.cs`（扩展方法） | `<类>_<ns>_Aop`（接口）、`<类>_<ns>_AopProxy`（实现类）、`<类>_<ns>_AopExtensions`（静态类） |
| 运行期 | `Aop.cs` / `AopCache.cs` / `AspectHooks.cs` / `ProxyEx.cs` | 代理的创建与缓存、钩子表的承载、代理→真身反查 |

**不解决什么**（这几条决定了「为什么我的切面不触发」）：

| 你以为在模块内 | 实际 |
|---|---|
| 拦截构造函数 / 字段 / 静态成员 / 事件 | **不支持**。`AspectOrientedAttribute.cs:8` 的 `AttributeUsage` 只有 `Method \| Property \| Field`，且字段那条只是给生成器看的信号 —— 拦截的是它**生成出来的属性**，不是字段读写本身 |
| 拦截「类内部对自身的调用」 | **不拦截**。代理是**另一个对象**，`this.Foo()` 永远走真身。官方解法是加一个 `AOP_` 前缀的转发方法，让内部调用改成走代理（`Examples/AOP/WPF/Demo/TeamViewModel.cs` 的 `OnMemberAdded` → `this.Aop().AOP_OnMemberAdded(...)`） |
| 拦截未标记的成员 | 生成器只把**被标记的 public 成员**放进镜像接口（`AopSurface.cs:93-109` 字段、`:112-133` 属性、`:136-159` 方法） |
| 非 public 成员「至少能编译」 | **静默失效**：类被判为 AOP 类（`AnalizeHelper.IsAopClass` 按类回答 yes），接口里却没有那个成员，`Aop()` 照常返回、调用照常直达真身 —— 没有诊断、没有异常 |
| 标了重载方法 / 表达式体属性 | **生成代码编译失败**（CS0102 / CS0548），不是运行期问题 —— 见 §七·1、§七·2 |
| 在 `netstandard2.0` / `net461` 上降级 | **不存在**。五个 .cs 全部包在 `#if NET` 里，而 `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:5` 的 TargetFrameworks 是 `netstandard2.0;netframework4.6.1;net5.0;netcoreapp3.0;net8.0` ⇒ 前两个 TFM 上这个命名空间**根本不存在**（编译器报「类型不存在」，不是运行期静默降级） |

**Core 与适配器都不消费它**：`Src/Adapters/` 下没有任何一处引用 `VeloxDev.AspectOriented`；`Src/` 里提到这个命名空间的只有它自己的契约（`Interfaces/AspectOriented/IAspectOriented.cs`）、生成器（`AopSurface.cs`/`AopWriter.cs`/`Analizer.cs` 里的字符串）与测试。真正的使用者是 `Examples/AOP/WPF`、`Examples/AOP/Avalonia` 与 `skills/veloxdev-add-aspects/SKILL.md`。所以它是**纯对外能力**，没有内部回归压力。

---

## 二、三个产物、一次遍历、一个命名算法

**产物一：镜像接口**，命名空间 `VeloxDev.AopInterfaces`（`Base/AopNames.cs:13`），基接口两个（`AopSurface.cs:76-87`）：

- `VeloxDev.AspectOriented.IAspectOriented` —— 空标记，标识「这是代理」；
- `VeloxDev.AspectOriented.IAopHookTarget` —— **带成员**的安装面，只有一个 `SetHooks(string, AspectHooks?)`（`AspectHooks.cs:66-77`）。

**产物二：代理实现类**，`internal sealed class <接口名>Proxy : <接口>`，与接口同命名空间（`AopSurface.cs:215`）。它持有 `_target` 与**每个可挂钩成员一个** `AspectHooks?` 字段。

**产物三：扩展方法类**，命名空间 `VeloxDev.AspectOriented`，`Aop()` 一个方法（`AopWriter.cs:58-95`）。

### 接口与代理不会漂移，因为它们是同一次遍历的两个渲染

`AopSurface.GenerateSource` 只走一遍成员列表，把结果同时喂给接口声明和 `WriteProxy`。成员种类只有一个来源（`AnalizeHelper.Members(classSymbol)`），签名只有一个来源（`Hookable.Signature` / `HookableProperty.Type`）—— 所以「两边签名不一致」在结构上不可能，而不是靠人盯。

### 命名只算一次

`Base/AopNames.cs` 是接口名、代理名、命名空间段的**唯一算法**：`ProxyFor` = `InterfaceFor + "Proxy"`（`AopNames.cs:18-19`），`Segment` 对全局命名空间返回 `Global`（`:21-24`）。旧实现里接口名在三处各算一遍，且全局命名空间会产出 `<global namespace>`（尖括号与空格非法），生成器整个抛出去、宿主只报 CS8785 —— 现在这一处只有一个算法。扩展类名 `WriteExtension` 用的是**同一个** `AopNames.Segment`（`AopWriter.cs:64`）。

### 类仍然必须 `partial` —— 但理由变了

AOP 自己已经不需要它了：`AopWriter.GenerateBaseInterfaces()` 现在返回 `[]`（`AopWriter.cs:43`），不再往用户类上注入那句 bodyless `partial class`。**但共享管线仍然只放行 partial 类**：`Analizer.Filters.IsCandidateClass`（`Base/Analizer.cs:170-174`，经 `:127` 的 `ForAttributeWithMetadataName` 过滤）对非 partial 类直接返回 false，于是 `Aop()` 根本不生成，调用方拿到的是 CS1061「未包含 Aop 的定义」。实测：把 `partial` 去掉即复现。所以「类必须 partial」这条不是 AOP 的，是整条生成管线的。

**为什么不再注入那句 partial class**：旧实现需要它，只是为了让 `CreateProxy<接口>(x)` 编译得过（`x` 必须能隐式转成接口）。现在 `new {Proxy}(x)` 不需要任何转换；而接口新增的基接口 `IAopHookTarget` 是**带成员**的，继续让用户类实现它，用户类就得自己实现 `SetHooks`。副作用是**好处**：用户类不再实现生成接口，于是「在真身上装切面」从运行期静默失效变成了**编译错误**（`SetProxy` 的 `where T : IAspectOriented` 过不去）。

---

## 三、一次拦截的完整流向

```
调用方
  │  data.Aop()                      ← 生成器写的扩展方法,每个类一个
  ▼
AopCache.Resolve<TClass,TInterface>(instance, factory)          AopCache.cs:41
  │  ConditionalWeakTable<TClass,TInterface> 命中就返回,否则跑 factory   :49-50
  ▼ factory (只在首次)
new <接口名>Proxy(x)                 AopSurface.cs:239（生成的构造函数）
Aop.Map(p, x)                        Aop.cs:19
  ▼
调用方持有代理(接口类型)
  │  安装钩子: p.SetProxy(ProxyMembers.X, "成员名", start, coverage, end)
  ▼
ProxyEx.SetProxy<T>                                             ProxyEx.cs:50
  ├ target is not IAopHookTarget ⇒ 抛 InvalidOperationException    :60-65
  ├ 键 = "get_X" / "set_X" / "X"                                  :67-72
  └ hookTarget.SetHooks(key, new AspectHooks(start, coverage, end))  :74
  ▼
调用方**经代理**调用成员
  ▼
生成的成员体（AopSurface.WriteProxy）
  ├ var h = 本成员那个 AspectHooks? 字段
  ├ h is null ⇒ 直接 _target.X(...) / return _target.X          ← 快路径
  ├ R0 = h.Start?.Invoke(args, null)
  ├ R1 = h.Coverage is null ? 真身调用 : h.Coverage.Invoke(args, R0)
  └ h.End?.Invoke(args, R1)   ← 返回值丢弃
  ▼
返回值 = R1
```

**三个阶段是「替换」而不是「环绕」**（未变）：`coverage` 非 null 时**真身完全不执行**，所以 `coverage` 不是"拦截器"而是"替身"。

**无钩子时有快路径（新增）**：旧实现无论是否装了切面，每次调用都走 `MethodInfo.Invoke`；现在 `h is null` 直接读真身，反射成本只存在于旧实现。这条写在生成代码里（`AopSurface.cs:251-255` 属性的注释），是本次改动的实际收益之一。

**`SetHooks` 的键是 switch，未知键抛**（`AopSurface.cs:336-361`）：

```csharp
case "get_X": _hooks_get_X = hooks; return;
...
default: throw new ArgumentOutOfRangeException(nameof(memberKey), memberKey, "not an interceptable member of …");
```

旧实现在这里查不到就什么都不做 —— 静默失效是本模块文档反复警告的失败形态，所以现在改成抛。

---

## 四、两个弱表，与「代理现在会回收」

| 表 | 类型 | 键 / 值 | 谁写 |
|---|---|---|---|
| `Aop._proxyToTarget` | `ConditionalWeakTable<object, object>` | 代理 → 真身 | `Aop.Map`（`Aop.cs:19`，由生成的 `Aop()` 调用） |
| `AopCache.Entry<TClass,TInterface>.Instances` | `ConditionalWeakTable<TClass,TInterface>` | 真身 → 代理 | `Resolve` 的 `GetValue`（`AopCache.cs:49-50`，字段 `:24`、`Resolve` 声明 `:41`） |
| 每个成员的 `AspectHooks?` 字段 | 代理实例字段 | 成员键 → 三元组 | 生成的 `SetHooks`（`AopSurface.cs:336`） |

**代理与真身现在会一起被回收。** 旧实现有两张**静态 `Dictionary` 强引用表**（`ProxyInstances` / `ProxyIDs`），只增不减，把每个代理 —— 并经由代理的 `_target` 字段把每个真身 —— 钉到进程结束。这两张表随 `ProxyInstance.cs` 一起删除了，现在只剩两个弱表。实测钉在 `AopProxyTests.AProxyIsCollectableOnceItsTargetIs`（弱引用 + 强制 GC，跑三次稳定通过）。

**注意这条是为「重构回归」写的**：重新引入任何静态注册表都不会让别的测试失败，只有这一条会红。

---

## 五、不变量

1. **`SetProxy` 必须在代理上调用**。运行期守卫是 `target is not IAopHookTarget` ⇒ 抛（`ProxyEx.cs:60-65`）；对生成的代理这一条**编译期**就挡掉了（用户类不再实现 `IAspectOriented`）。运行期那半是给**手写的** `IAspectOriented` 实现留的 —— 空标记接口谁都能实现，必须拒绝而不是忽略。
2. **`IAopHookTarget` 是安装面，不是给用户实现的**。它由生成的代理实现，经 `ProxyEx.SetProxy` 抵达（`ProxyEx.cs:74`），不要手写。
3. **键是「裸成员名」加前缀**：属性走 `get_X` / `set_X`（`ProxyEx.cs:69-70`），方法走方法名原文（`:71`）。所以 Setter 与 Getter 是两条**独立**的注册，属性不会因为注册了一侧而另一侧生效。
4. **三个阶段都在生成的成员体里同步执行**，`start` 收到的 `previous` 恒为 `null`，`end` 收到的 `previous` 是 `R1`。`end` 的返回值被丢弃。
5. **`coverage` 为 null 才调真身**。这条是「切面生效」与「切面把功能弄没了」的唯一分界。
6. **钩子是每代理一份、可覆盖不可叠加**：`SetHooks` 是**赋值**不是追加（`AopSurface.cs:342`），再次安装同一成员是替换上一组三元组；传 `null` 即清除该成员的全部切面。
7. **`Aop.Map` 用 `Add` 而非索引赋值**（`Aop.cs:20`）：同一代理被 Map 两次会抛 `ArgumentException`。生成的工厂只在缓存未命中时跑（`AopCache.cs:49-50`），所以正常路径只有一次。
8. **每个成员一个专属字段**，字段名由成员键推导（`AopSurface.cs:194-195`）。这直接导致 §七·1 与 §七·2 的编译失败。

---

## 六、入口：我要改 X，先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 「哪些成员会出现在代理上」（可见性、字段→属性、接口形状） | `Src/Generators/VeloxDev.Core.Generator/AopSurface.cs`（字段 `:93-109`、属性 `:112-133`、方法 `:136-159`） |
| 生成出来的成员体长什么样（三阶段、快路径） | 同上 `WriteProxy`（`:197-365`） |
| 「哪个类算 AOP 类」的判定 | `Src/Generators/VeloxDev.Core.Generator/Base/AnalizeHelper.cs`（`IsAopClass(symbol)` `:43`、`HasAspectOriented` `:48`） |
| `Aop()` 扩展方法 | `Src/Generators/VeloxDev.Core.Generator/Writers/AopWriter.cs`（`WriteExtension` `:58-95`） |
| 接口 / 代理 / 命名空间段的名字 | `Src/Generators/VeloxDev.Core.Generator/Base/AopNames.cs` |
| 三个阶段的执行顺序与返回值语义 | `AopSurface.WriteProxy`（`:257-261` 属性 getter、`:278-290` setter、`:313-330` 方法） |
| 「成员名怎么变成键」（`get_`/`set_` 前缀） | `ProxyEx.cs:69-71`，与生成侧的 `SetHooks` switch（`AopSurface.cs:336-361`）**必须成对** |
| 代理的缓存与复用 | `AopCache.cs`（`Entry<,>` `:18-25`、`Resolve` `:41`） |
| 代理→真身 | `Aop.cs`（`Map` `:19`、`GetTarget<TTarget>` `:25`） |
| 「为什么这个特性标上去没反应」 | 先看 `AopSurface.cs:93`/`:113`/`:137` 的可见性过滤，再看 `AnalizeHelper.cs:52` 的文本匹配，最后看类是不是 partial（`Analizer.cs:170`） |

---

## 七、陷阱（带依据）

1. **重载成员 = 生成代码编译失败，不是运行期抛。** 两个同名方法各推导出同名槽位 `_hooks_{名}`（`AopSurface.cs:194-195`），于是 CS0102「类型已包含 _hooks_Foo 的定义」；`SetHooks` 的 switch 也会撞出重复 `case`。实测复现。这比旧实现的「接口能编译、调用时抛 `AmbiguousMatchException`」好 —— 失败前移到了构建期 —— 但报错落在 `obj/…/AopSurface/*.g.cs` 里，作者要自己倒推回自己那行。**照旧：不要标重载方法。**
2. **表达式体属性 = 生成代码编译失败。** `hasGetter` / `hasSetter` 的推导在 `property.AccessorList` 为 null 时两者都给 false（`AopSurface.cs:118-125`），于是生成的属性是 `{ }` —— CS0548「属性或索引器必须至少有一个访问器」，接口与代理两处各报一次。实测复现。写成 `public string X => "x";` 并标 `[AspectOriented]` 即触发；`{ get; }` 与 `{ get; set; }` 都正常。
3. **一个切面到底拦的是哪个成员，取决于生成器把什么放进了接口。** 字段必须同时有 MVVM 特性与 `[AspectOriented]`（`AopSurface.cs:94-95`），且它生成的是**推导出的属性名**（`_name` → `Name`，`AnalizeHelper.cs:55-65`）。只用 `[AspectOriented]` 标字段、不标 MVVM 特性 ⇒ 接口里没有该成员，**静默无效**。
4. **接口成员一律给 `{ get; set; }`（字段那条路，`AopSurface.cs:105`）**，所以「接口上看得见 setter」不等于「真身可写」—— 一个只读属性会生成出 setter，然后编译失败。属性那条路则按真身的可访问性给（`:122-124`）。
5. **`Analizer.Filters.Targets` 决定「这个类有没有活干」走符号**（`:106` 的 `VeloxDev.AspectOriented.AspectOrientedAttribute` 是 10 个触发特性之一）；**接口里有哪些成员走语法文本**：`attribute.Name.ToString() == "AspectOriented"`（`AnalizeHelper.cs:52`）。后果是一个**别的**叫 `AspectOriented` 的特性同样会被当成标记；字段那条路认的 MVVM 特性也是文本匹配 `Contains("Observable") || Contains("Property")`（`AopSurface.cs:94`）—— `[VeloxProperty]` 与 CommunityToolkit 的 `[ObservableProperty]` 都算。这是本模块唯一一处「两种匹配方式并存」，改任一侧都要想到另一侧。
6. **裁剪 / AOT 不再是问题，但两个 IL 警告曾经是。** `AopCache` 的 `TInterface` 带着 `[DynamicallyAccessedMembers(PublicParameterlessConstructor)]`（`AopCache.cs:18-23`、`:41-43`）—— 这是为消掉 `ConditionalWeakTable` 的 IL2091 而传下去的标注，**不是**对调用方的真实要求（详见 §八）。
7. **`AopInterface` 这个名字已经不存在**，接口与代理都由 `AopSurface.cs` 一个文件产出。旧记忆与旧文档里凡是提到 `AopInterface.cs` / `ProxyInstance.cs` / `DispatchProxy` 的段落都已作废。

---

## 八、AOT：实测结论（2026-10-02）

**结论：可用。** 验证方式与结果：

| 项 | 结果 |
|---|---|
| 探针 | `net10.0` console，引用 Core + 生成器（`Debug` 配置 —— Core 只在 Debug 用本地生成器源码，Release 走 NuGet 包） |
| 发布 | `dotnet publish -c Debug -r win-x64`，`<PublishAot>true</PublishAot>` —— 成功，**零 IL 警告** |
| 运行 | 原生 exe 输出与 JIT 逐行一致：代理类型 = `VeloxDev.AopInterfaces.Probe_Global_AopProxy`，且 `proxy.GetType().Assembly == 消费者程序集` |
| 覆盖 | 方法三阶段、`coverage` 替换、getter/setter 分离、字段+MVVM 特性那条路、`GetTarget` 反查、每实例缓存 |
| 回归面 | Core 全量 947 个测试通过（2026-10-02 那次运行的计数，**不可复核** —— 当前 `Src/Core/VeloxDev.Core.Test/` 已有 1034 个 `[TestMethod]`）；`Examples/AOP/WPF`、`Examples/AOP/Avalonia` 均 0 警告 0 错误 |

**两个曾经挡住 AOT 的点，现在都在生成期解决了**：`DispatchProxy`（`RequiresDynamicCode`，靠 `Reflection.Emit`）与 `ProxyEx` 里的 `dynamic`（需要 `Microsoft.CSharp` 运行期绑定器）。

**环境坑，不是兼容性问题**：NativeAOT 的链接阶段要找 `vswhere.exe`，而它不在 PATH 上（实际位置 `C:\Program Files (x86)\Microsoft Visual Studio\Installer\`）。不加会看到 `link.exe … 退出代码 123` 与「文件名、目录名或卷标语法不正确」——**那是工具链寻址失败，不要读成 AOT 不兼容**。把该目录加进 PATH 即可。

**关于那两个 IL2091**：`ConditionalWeakTable<TKey,TValue>` 的 `TValue` 带 `DynamicallyAccessedMembers(PublicParameterlessConstructor)` 标注（服务于 `GetOrCreateValue()`，本模块只走带工厂的 `GetValue`，永远不碰那条路）。触发条件是**类型实参为未标注的泛型形参**；具体类型不查构造器 —— 一个没有公共无参构造的类作实参也不报。所以修法是把标注传递到 `TInterface` 上，调用点传的都是生成的具体接口类型，不会因此多出警告。实测：修前发布报 2 条 IL2091，修后干净。
