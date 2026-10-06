# Serialization — 扩展

> 引擎：`Src/Core/VeloxDev.Core/Serialization/`。生成器：`Src/Generators/VeloxDev.Core.Generator/Writers/VeloxJsonCodeWriter.cs`。
> 先读 [architecture.md](architecture.md)，那里面有闭世界与逐字节契约。

---

## 一、想改「某个值怎么写」，先看改的是哪一侧

同一个拼写规则在三条路上都要成立，但**只有一处**是权威：

| 你要改的 | 打开 |
| --- | --- |
| 字符串怎么转义、double 怎么拼 | `VeloxJsonText.cs` — **唯一一处**。归档写入器（`VeloxJsonWriter.WriteEscaped` 只是转发）与 JSON 树（`VeloxJsonValue`）都走它 |
| 数字/布尔/`Guid` 的写法 | `VeloxJsonWriter.cs` 的 `WriteInt32` / `WriteInt64` / `WriteDouble` / … |
| 缩进、换行、成员分隔 | `VeloxJsonWriter.cs` 的 `NewLine()` / `Separate()` / `StartContainer()` |
| `$id` / `$type` / `$ref` 的书架结构 | `VeloxJsonWriter.WriteStartObject` 与 `VeloxJsonReader.BeginObject` |

**官方路径 vs 会在编译期通过的错误捷径：**

- ✅ 改 `VeloxJsonText`。
- ❌ 在 `VeloxJsonWriter` 里另写一份转义 —— 这正是 2026-10-04 之前的状态，两处规则会各自漂移，而漂移不会报错，只会让「归档写的」与「JSON 树写的」不是同一种文档。
- ❌ 把 `NewLine()` 里的 `Environment.NewLine` 换成 `"\n"`。见 architecture.md §三：**这会通过弱闸、在强闸上失败**。

---

## 二、想改「生成出来的读写器长什么样」

生成器的产物调用引擎，引擎的实现可以随便换，**但两侧的调用面必须同时改**。

1. 引擎侧：在 `VeloxJsonReader` / `VeloxJsonWriter` / `VeloxJsonSerializer` 上加或改成员。
   **同步与异步两条链路都要改** —— 异步面在 `*.Async.cs` 里，生成器为每个类型产出 `X` 与 `XAsync` 一对。
   **`VeloxJsonRegistry` 的注册方法也是这条调用面的一部分**：生成物在 `[ModuleInitializer]` 里调
   `RegisterWriter` / `RegisterReader` / `RegisterName`，签名一改，**旧的 `.g.cs` 就编不过** ——
   2026-10-05 给两个注册方法加 `declaresType` 时就是这样（理由见 [architecture.md](architecture.md) §一）。
2. 生成器侧：`Writers/VeloxJsonCodeWriter.cs` 里对应的 `AppendLine` 字符串，且两套都要发
   （`WriteWriterBody` / `WriteReaderBody` 各带一个 `async` 参数）。
3. **两侧一起提交。** 只改一侧的后果是产物编不过，或者（更糟）编得过但行为与另一侧不一致。
4. 加完立刻跑 `VeloxJsonStreamingTests` —— 它在**两条路各跑一遍**同一批语料，是唯一能发现「只改了一侧」
   或「两侧语义漂了」的闸。

**联动清单（漏一处不会报错，只会静默不同步）：**

- 生成器的 `<Version>`（`VeloxDev.Core.Generator.csproj`）
- 每一处 `PackageReference Include="VeloxDev.Core.Generator"` —— **11 处**，其中 `Examples/*/Directory.Build.props` 那两处容易滞后
- 每一处配套的 `ProjectReference`（`Condition="'$(Configuration)' == 'Debug'"`）—— **12 处**
- `Src/Core/VeloxDev.Core.Test/VeloxDev.Core.Test.csproj` 是唯一不分 Debug/Release 的一处，动机写在它的注释里

> ⚠ **这条链今天根本没接通**：仓库无法在 Release 下构建。理由与证据在 [architecture.md](architecture.md) §五。
> 所以「改了生成器」目前**只在 Debug 生效**，升版本号那一步是单独一件事，别顺手做。

---

## 三、想加一个新拼写/新成员类型（例如一种新的容器形状）

1. `Base/VeloxJsonModel.cs` 的 `Classify` 决定成员算标量、集合还是字典 —— 新形状先在这里加。它的返回值是四元组 `(Kind, Element, Key, InterfaceKeyed)`：**`Key` 是字典的键类型，也是 `Reachable` 的入口之一**（2026-10-04 起），新形状要么给它一个键，要么显式给 `null`。
2. 若它需要**读回来**（不是只写得出去），还要 `CollectNestedContainers` 与 `IsContainerReadable` 认它。**只加写侧是一条已知的坑**：只加写侧会让嵌套容器写得出去、读不回来，见 `VeloxJsonRegistry.RegisterContainerFactory` 的 remarks。
3. `Writers/VeloxJsonCodeWriter.cs` 的 `ReadMember` 发对应的读法。
4. 跑 `Src/Core/VeloxDev.Core.Extension.Test` —— 逐字节黄金与幂等测试会当场告诉你产物变了没有。

**成员收录顺序是契约的一部分**：手写可写属性（声明顺序）在前，`[VeloxProperty]` 提升出来的字段（字段顺序）在后，继承来的再往后。规则在 `Base/VeloxJsonModel.cs` 的 `ReadMembers`。**改它会直接打翻黄金文件**，改之前先看 `SerializationOrderTests`。

---

## 四、想加一个能被序列化的类型

不需要动引擎，只需要让它进得了「闭世界」——见 architecture.md §一。四条路：实现四个组件接口之一、贴 `[VeloxProperty]`、贴 `[Archivable]`、或带 `[WorkflowBuilder.*]`；或者从这些类型出发沿**成员的声明类型**能走到。**能走到的比声明本身宽**（派生类向下展开、字典的键、根形状开放泛型的约束，见 architecture.md §一「收录面比…宽三条」）。

`[Archivable(typeof(A), typeof(B))]` 是第五条路：被点名的类型直接当根收进来，可以链式（被点名的类型自己也能再点名），去重靠既有的 `included`。**点名却发不出条目的类型报 `VELOX_JSON_ARCH001`（错误），不静默丢掉** —— 判据与措辞在 `Base/VeloxJsonModel.cs` 的 `WhyNotWritable`。

走不到也不打紧，**写它时会抛 `MissingWriter`，错误信息本身写着为什么**。不要为了「让它能写」去加反射兜底 —— 闭世界正是这套东西能裁剪的前提。

**成员类型声明成基类时，本程序集里它的派生类 / 实现类会被自动收进来**（2026-10-04 起对**具体**基类也成立）；跨程序集的实现者要自己贴 `[Archivable]`。失败形态（写抛 `MissingWriter`、读抛 `MissingReader`、`object` 成员静默降级成字典）见 [pitfalls.md](pitfalls.md) §六。

**收录了什么可以不用猜**：全量清单（类型名 + `$type` 名 + 出处）写在生成文件 `*_VeloxJson.g.cs` 的**文件头注释**里（VS：Dependencies → Analyzers；CLI：`-p:EmitCompilerGeneratedFiles=true`）。构建期只有两类出声：`VELOX_JSON_INCLUDE001`（Info）**只报没有声明点名过的**那些，`VELOX_JSON_GENERIC001`（Warning）报解析不出的类型参数。

---

## 四·五、想改「某一个成员怎么写」

`[Archive(ArchiveOptions, object?)]` 是成员级唯一的开关：`KeepProperty` 放行计算属性、`KeepField` 放行没有对应属性的字段、`IgnoreField` 把成员整个排除、`ReName` 换成员在文档里的名字（第二个实参给新名）。

.NET 自带的 `[JsonIgnore]` 一并认：`Always`（默认）= 排除，`Never` = 显式放行，`WhenWritingNull` / `WhenWritingDefault` = 条件写出。**给已经有 `[JsonIgnore]` 的类型接入这套格式时不必改写。**

`EnumName` 让**那一个**枚举成员写值名而不是底层整数（默认仍是整数，那是既有契约）。它只对枚举与可空枚举有意义，用在别处报 `VELOX_JSON_MEMBER001`。

**必填**走 C# 的 `required` 或 STJ 的 `[JsonRequired]`，不需要 Velox 自己的特性：生成的 reader 缺了它就读不过去（抛 `InvalidOperationException`）。注意必填与「排除」「条件写出」互斥 —— 三者一起用会让文档永远无法满足，生成器报错。

**它只动单个成员，不动规则**：成员顺序是逐字节契约，`[Archive]` 不重排、不改变其余成员的取舍。改「所有成员」的粒度仍然只有 `SerializationOptions.WithExcludedPropertyTypes`，而且它按**声明类型**精确匹配。

写这个特性时会撞上三条只有做过才知道的约束（生成器看不见别的生成器、生成器引用不了目标程序集、引用程序集剥非 public 成员），都记在 [pitfalls.md](pitfalls.md) §七。
