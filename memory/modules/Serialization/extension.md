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
2. 生成器侧：`Writers/VeloxJsonCodeWriter.cs` 里对应的 `AppendLine` 字符串，且两套都要发
   （`WriteWriterBody` / `WriteReaderBody` 各带一个 `async` 参数）。
3. **两侧一起提交。** 只改一侧的后果是产物编不过，或者（更糟）编得过但行为与另一侧不一致。
4. 加完立刻跑 `VeloxJsonStreamingTests` —— 它在**两条路各跑一遍**同一批语料，是唯一能发现「只改了一侧」
   或「两侧语义漂了」的闸。

**联动清单（漏一处不会报错，只会静默不同步）：**

- 生成器的 `<Version>`（`VeloxDev.Core.Generator.csproj`）
- 每一处 `PackageReference Include="VeloxDev.Core.Generator"` —— **11 处**，其中两处 `Examples/*/Directory.Build.props` 历史上就曾滞后
- 每一处配套的 `ProjectReference`（`Condition="'$(Configuration)' == 'Debug'"`）—— **12 处**
- `Src/Core/VeloxDev.Core.Test/VeloxDev.Core.Test.csproj` 是唯一不分 Debug/Release 的一处，动机写在它的注释里

> ⚠ **这条链今天根本没接通**：仓库无法在 Release 下构建。理由与证据在 [architecture.md](architecture.md) §五。
> 所以「改了生成器」目前**只在 Debug 生效**，升版本号那一步是单独一件事，别顺手做。

---

## 三、想加一个新拼写/新成员类型（例如一种新的容器形状）

1. `Base/VeloxJsonModel.cs` 的 `Classify`（`:572` 附近）决定成员算标量、集合还是字典 —— 新形状先在这里加。
2. 若它需要**读回来**（不是只写得出去），还要 `CollectNestedContainers`（`:265` 附近）与 `IsContainerReadable`（`:287` 附近）认它。**只加写侧是一条已知的坑**：嵌套容器曾经写得出去读不回来，见 `VeloxJsonRegistry.RegisterContainerFactory` 的 remarks。
3. `Writers/VeloxJsonCodeWriter.cs` 的 `ReadMember`（`:189` 附近）发对应的读法。
4. 跑 `Src/Core/VeloxDev.Core.Extension.Test` —— 逐字节黄金与幂等测试会当场告诉你产物变了没有。

**成员收录顺序是契约的一部分**：手写可写属性（声明顺序）在前，`[VeloxProperty]` 提升出来的字段（字段顺序）在后，继承来的再往后。规则在 `Base/VeloxJsonModel.cs:458` 的 `ReadMembers`。**改它会直接打翻黄金文件**，改之前先看 `SerializationOrderTests`。

---

## 四、想加一个能被序列化的类型

不需要动引擎，只需要让它进得了「闭世界」——见 architecture.md §一。三条路：实现四个组件接口之一、贴 `[VeloxProperty]`、或贴 `[VeloxSerializable]`；或者从这些类型出发沿**成员的声明类型**能走到。

走不到也不打紧，**写它时会抛 `MissingWriter`，错误信息本身写着为什么**。不要为了「让它能写」去加反射兜底 —— 闭世界正是这套东西能裁剪的前提。
