# Serialization — 架构

> 代码：`Src/Core/VeloxDev.Core/Serialization/`，11 个 `.cs`。命名空间 `VeloxDev.Serialization`。
> 读写器各有同步 / 异步两份：`VeloxJsonReader(.Async).cs`、`VeloxJsonWriter(.Async).cs`、`VeloxJsonSerializer(.Async).cs`。
> 生成器：`Src/Generators/VeloxDev.Core.Generator/`（`VeloxJson.cs` / `Base/VeloxJsonModel.cs` / `Writers/VeloxJsonCodeWriter.cs`）。
> 通用 VM 序列化面：`Src/Core/VeloxDev.Core/Serialization/ViewModelSerializer.cs`（2026-10-04 从 Extension 下沉，原名 `ComponentModelEx`，命名空间由 `VeloxDev.MVVM.Serialization` 并入 `VeloxDev.Serialization`）。
> 工作流领域的两个封装仍在 Extension：`CheckpointEx.cs`、`CompiledGraphEx.cs`。

本文只写「读完这 7 个文件、并且改坏过一次才知道的东西」。类型清单、成员表请看 IDE。

---

## 一、三层，别记成两层

| 层 | 在哪 | 是什么 |
| --- | --- | --- |
| **引擎** | `Src/Core/VeloxDev.Core/Serialization/` | 手写、**零反射**的 JSON 读写。`VeloxJsonSerializer` 是门面，`VeloxJsonReader` / `VeloxJsonWriter` 是游标 |
| **代码生成** | `Src/Generators/VeloxDev.Core.Generator/` | 为每个类型编出直写的 reader/writer，产物是**消费者项目里的 C# 文本** |
| **通用序列化面** | `Src/Core/VeloxDev.Core/Serialization/ViewModelSerializer.cs` | VM 的 `Serialize` / `TryDeserialize` / `Deserialize` / 流 / 字节。**没有引擎逻辑** |
| **领域封装** | `Src/Core/VeloxDev.Core.Extension/` | `CheckpointEx`（检查点 + 文件存储）、`CompiledGraphEx`（编译图快照）。它们当初留在 Extension 的理由（「Core 没有序列化器」）已经过期，留着是因为它们是**工作流领域**对通用面的预设 |

引擎**不在生成器里**——生成器只产出调用它的代码。要改行为先判断改的是哪一层；改错层的后果是 Debug 通过、Release 不变（见 §五）。

### 闭世界：什么类型进得了文档

**当且仅当生成器为它编出了读写器。** 收录条件：实现四个组件接口之一、或贴 `[VeloxProperty]`、或贴 `[Archivable]`，或能从这些类型出发沿**成员的声明类型**走到。

`[Archivable(typeof(A), typeof(B))]` 把点名到的类型直接当根收进来（可链式；去重靠 `included`；发不出条目的报 `VELOX_JSON_ARCH001` 而不是静默丢掉）。

引擎里**没有反射、也没有兜底**（`VeloxJsonSerializer` 的 remarks 写着这句）。进不了闭世界的类型写它会抛 `MissingWriter`，错误信息自己说明原因。**这不是缺陷，是能裁剪的前提。**

### 闭世界里的第二个开关：成员不只有默认规则

类型进了闭世界之后，「哪些成员进文档」由三条规则决定：默认规则（public 且 public setter 的属性、按声明顺序、加上 `[VeloxProperty]` 提升出来的）、成员级 `[Archive(ArchiveOptions, object?)]`、以及 .NET 自带的 `[JsonIgnore]`。

五个选项 `KeepProperty` / `KeepField` / `IgnoreField` / `ReName` / `EnumName` 都**只动它标的那一个成员**，不重排、不改其余成员的取舍 —— 顺序是逐字节契约。生成器把「文档里的名字」与「CLR 成员名」拆成 `VeloxJsonMember.DocumentName` 与 `.Name`，`ReName` 与只写得出去的成员都靠这两个字段区分；`WriteCondition`（条件写出）、`IsRequired`（必填）、`IsEnumName`（枚举写名字）是同一条路上的另外三个开关。

**必填**有两个同义的口子：C# 的 `required` 与 STJ 的 `[JsonRequired]`。生成的工厂因此要为它们写对象初始化器（`new T() { X = default! }` —— 只写 `new T()` 编不过），而生成的 reader 读完会核对每个必填成员到没到，缺了就抛。判定与初始化器在 `Base/RequiredMembers.cs`，AIContextTree 生成器共用同一份。

`[JsonIgnore]` 按它本来的意思被认下来（理由与钩子那次相同：使用方不必为了同一件事改写代码）：`Always` = 排除该成员，`Never` = 显式放行，`WhenWritingNull` / `WhenWritingDefault` = **条件写出**（`VeloxJsonMember.WriteCondition`，写侧发一条守卫，读侧不必配合）。它的 `Condition` **按名字判、不按数值** —— 理由见 [pitfalls.md](pitfalls.md) §七。

**生命周期钩子是 BCL 那四个特性**（`[OnSerializing]` / `[OnSerialized]` / `[OnDeserializing]` / `[OnDeserialized]`），生成器沿基类链先基后派生地调，带 `StreamingContext` 的传 `default`。原来的四个 `IVeloxJson*` 钩子接口已删。

---

## 二、热路径上的三个结构事实

1. **注册表读取无锁。** 五张表收在一个不可变 `Snapshot` 里，用 `private static volatile Snapshot _snapshot`（`VeloxJsonRegistry.cs:140`）发布；注册在 `Gate` 内造新快照再换，**五个查询方法一把锁都不拿**。查询在每一步都发生（写：`WriterFor` 每个值一次、`NameOf` 每个类型不符一次；读：`TypeOf` + `ReaderFor` 每个对象各一次），原先的全局锁因此是每节点的固定开销。
2. **成员名不落成字符串。** 生成器发出的是 `while (reader.NextMember())` + `reader.MemberNameEquals("字面量")` 的 `if/else if` 链（`Writers/VeloxJsonCodeWriter.cs:206`），名字在原文上就地比对（`VeloxJsonReader.cs:263`、`:303`）。**旧的 `while (NextMember(out var name)) switch (name)` 每读一个成员先分配一个字符串，只为和字面量比一次就丢掉。**
3. **转义拼法只有一份。** 在 `VeloxJsonText.EscapeSequence`（`VeloxJsonText.cs:52`）—— 归档写入器与 JSON 树都从它取。**没有转义字符的字符串整串一次写出**，不再每字符一次虚调用。

---

## 二·五、同步面与异步面是**两套**，不是一套加了 await

读写两侧各有两条完整链路：同步的（`VeloxJsonReader.cs` / `VeloxJsonWriter.cs`）与异步的
（`VeloxJsonReader.Async.cs` / `VeloxJsonWriter.Async.cs`）。生成器为每个类型**同时产出**
`Read`/`ReadAsync` 与 `Write`/`WriteAsync`。

**为什么不合并**：唯一的合并办法是让每个 token 都穿过一层状态机，而内存内那条路
（`Serialize()` / `Deserialize<T>(string)`，也是 7 个平台 demo 走的那条）**永远不发生 I/O**，
穿状态机是纯亏。分开之后同步面保住上一轮量出来的速度，异步面才付它该付的代价。

**代价与纪律**：产物大约翻倍，而且两条路**必须同义**。守卫是 `VeloxJsonStreamingTests` ——
同一批语料（四份黄金文件、分块 1..64、缓冲区 1..16）在**两条路各跑一遍**。改一条链路就得改另一条。

四条不显眼但必须守的规矩：

- **异步方法不能有 `out`**（CS1988）。`BeginObjectAsync` 因此返回 `(bool Opened, int ReferenceId,
  string? TypeName)`，`PeekCodeAsync` 用 `-1` 表示文档结束。同步面保留 `out`。
- **写侧缓冲只给异步面。** 同步写入器**不**缓冲，仍然直接写进 `TextWriter`。给同步面加缓冲会改变字符
  到达输出的**时机**，而那是对直接使用 `VeloxJsonWriter` 的调用方可见的行为 —— 2026-10-04 加过一次，
  6 条测试当场红，于是退回。异步面有内部缓冲，收尾必须 `CompleteAsync()`。
- **`Task` 而不是 `ValueTask`**：`ValueTask` 在 netstandard2.0/net461 上不在框架里，用它就得让生成产物
  随 TFM 变化 —— 而生成器跑在消费者的编译里，判断不了 TFM。
- **`FileCheckpointStore` 的异步文件读写按 `#if NET8_0_OR_GREATER` 分档**：`File.WriteAllTextAsync`
  是 .NET Core 2.0 起的 API，旧档保留同步写。

---

## 三、逐字节契约（以及两道闸强度不同）

四份冻结文档：`Src/Core/VeloxDev.Core.Extension.Test/Serialization/Golden/*.json`。**任何改动都不得让它们差一个字节。**

两道闸，**强度不一样**，这是最容易误判的地方：

| 闸 | 位置 | 比法 |
| --- | --- | --- |
| **强** | `SerializationGoldenTests.cs:57` | `File.ReadAllText` **精确**比较，不归一化 |
| 弱 | `VeloxJsonSerializerTests.cs:30-34` | 先把 `\r\n` 归一化成 `\n` 再比 |

> ⚠ **换行必须写 `Environment.NewLine`**（`VeloxJsonWriter.cs:258`）。
> 黄金文件在**索引里是 LF、工作区里是 CRLF**（`.gitattributes` 为 `* text=auto`），所以「写 `Environment.NewLine`」是**唯一**能同时过两道闸的写法。
> **把 `Environment.NewLine` 硬编码成 `"\n"` 会在弱闸上通过、在强闸上失败。**

同一族里另外几条：`$id`/`$type` 必须是对象的**前两个成员**；`$type` 的值是注册表的**键**（`Namespace.Type, AssemblyName`，泛型再带上每个类型实参 —— 见 [pitfalls.md](pitfalls.md) §五·五）；重复对象写 `{"$ref":"n"}`；double 用最短往返且整数值补 `.0`；NaN/±Infinity 写成字符串；枚举写底层整数；非 ASCII **不转义**；空容器写 `{}`/`[]`。

### 陷阱：`$id` 的值是**带引号的字符串**

`VeloxJsonWriter.WriteStartObject` 用 `WriteString(...)` 写 `$id`（`VeloxJsonWriter.cs:109`），所以文档里是 `"$id": "1"` 而不是 `"$id": 1`。
读侧 `ReadReferenceId`（`VeloxJsonReader.cs:458`）两种都认，但**按整数解析是一个已经犯过的错**——2026-10-04 那次改动让 36 条测试当场红，报的是 `expected a reference id`。

### 幂等测试是最强的一条闸

`SerializationIdempotenceTests.cs` 断言 `write(read(golden)) == golden`。
黄金测试只钉 `write(read model)`；**读回来对象图变了**这类回归只有幂等测试看得见。它读**原文**（不归一化），所以顺带钉住了换行。

---

## 四、性能：改了什么，量出来多少

基准在 `Src/Verification/VeloxDev.Serialization.Benchmarks/`（跑法见 `Src/Verification/README.md`）。

**量出来的数字**（同一台机器、同一会话，`Debug -p:Optimize=true`、进程内；语料是节点+槽位+链路的工作流树）：

| 用例 | 改前耗时 | 改后耗时 | 改前分配 | 改后分配 |
| --- | --- | --- | --- | --- |
| Serialize 2 | 19.45 µs | 18.54 µs | 33.84 KB | 34.16 KB |
| Deserialize 2 | 52.37 µs | 43.02 µs | 89.88 KB | **53.70 KB** |
| Serialize 1000 | 8 628.77 µs | 7 973.83 µs | 7 663.55 KB | 7 726.11 KB |
| Deserialize 1000 | 40 110.54 µs | 35 228.27 µs | 24 347.72 KB | **14 604.12 KB** |
| Serialize 10000 | 105 269.77 µs | 91 433.65 µs | 76 548.38 KB | 77 173.39 KB |
| Deserialize 10000 | 428 905.63 µs | 375 605.76 µs | 243 916.98 KB | **146 632.75 KB** |

**读侧分配降四成**，是最稳的一维。写侧耗时降 5–13%，**分配基本持平**（写入器分配的是文档本身，改动影响不到它）。

贡献它的四处（都已落地）：注册表快照（§二·1）、转义批量化 + 缩进缓存、读侧快路径（免 `StringBuilder`、免元数据候选串、免 `Substring` 解析）、生成器免分配成员派发（§二·2）。

**流式那一步（可续读窗口）本身是有代价的**：`CharAt`/`Require` 的间接层加进了按字符跑的循环里，第一次量出来 10 000 节点两个用例各**慢 3%**。所以三个最热的循环（`SkipWhitespace`、`ReadQuoted` 的扫描、`ReadBareToken`）和 `MemberNameEquals` 各留了一条 **string 直连快路径**，恢复之后是：

| 用例 | 流式之后 |
| --- | --- |
| Serialize 2 / 1000 / 10000 | 15.96 / 7 885.92 / 91 310.76 µs |
| Deserialize 2 / 1000 / 10000 | 39.68 / 32 738.11 / 374 952.58 µs |

即**没有回归**，分配也一模一样。**改这几条快路径之前先量**：它们是「一份实现 + 几处直连」换来的，不是冗余。

异步面（§二·五）落地后同步面**分配量逐字节不变**、耗时在误差内 —— 这正是「两条链路分开」买到的东西。
中途踩过一次：给同步写入器也加了内部缓冲（为了少一层分支），2 节点那份文档的分配量立刻涨了近一半
（每个写入器实例预分配 8 KB×2），而且字符到达输出的时机变了、6 条测试当场红。**缓冲只归异步面。**

### 一个**没有**采纳的方案，别再试一遍

计划里原本要把内部游标从 UTF-16 换成 UTF-8 字节。**证据不支持**：

- `ReadString`/`ReadText` 与所有属性赋值都要 `string`，成员名也要（生成器那条链虽已改成就地比对，但值仍然要字符串），所以字节化省不掉字符串物化；
- 公开的 `VeloxJsonReader` 是 **class**，而 `Utf8JsonReader` 是 `ref struct`，**不能当它的字段**——想借 STJ 的分词器就得把公开类型改成 ref struct；
- `string` 入口改字节要**先转码一次**，对 `string` 这条主路径是净亏。

要真正省下那份 UTF-16 常驻，走的**不是**字节化，而是**可续读窗口**（2026-10-04 落地）：

- 读侧：`VeloxJsonReader` 多了一个 `TextReader`/`Stream` 入口。`_position` 改成**文档绝对偏移**，窗口压缩时改 `_origin` 而不动任何偏移字段；一个 `Require(floorAbs, count)` 原语负责压缩、增长、续读。**一个 token 在窗口里始终连续**这条不变量靠 `floorAbs` 保证 —— 扫描不定长 token 的循环把它的起点传下去。
- 两个「钉住下限」的字段容易忘：`_checkpointFloor`（`$ref`/`$id`/`$type` 探测回退期间，必须 `try/finally`）与 `_memberHeld`（`NextMember()` 记下名字后置位，**任何取值方法都要清掉**，否则一个长字符串值会把下限钉在名字上，内存又变回 O(整份文档)）。
- 写侧本来就在流式写（`WriteTo(TextWriter, …)`），只是补了 `WriteTo(Stream, …)`。
- **`$ref` 从来只向后指**（写侧先分配 id 再写对象，重复出现才写引用），所以**单遍前向读就够了**，不需要两遍式重设计 —— 早先记忆里那句「需要两遍」是错的，已删。

峰值的实际形态：`缓冲区 + 对象图`，而不是 `整份文档 + 对象图`。缓冲区会为一个超长 token 增长到那个 token 的大小。

### 四处「长度感知」的比较，是这套东西最容易静默错的地方

窗口截断时**不能拿前缀去比**：`NextIsNull`、`ReadLiteral`、`StartsWithQuoted`、`MemberNameEquals` 四个都必须先 `Require` 足长度再比。漏掉任何一处，症状是「只在分块边界上、只在恰好截断时」解析出错，普通测试**抓不住**。

守卫在 `VeloxJsonStreamingTests`：把四份黄金文件按 **1 字符一段**喂进去（每个 token 都跨越一次续读），缓冲区小到 1（逼出增长路径），断言 `write(read(golden)) == golden`。

---

## 五、Release 通道**目前根本跑不起来**（既有状态，与性能改动无关）

`VeloxDev.Core.csproj` 在非 Debug 下引用 `VeloxDev.Core.Generator` **包**，而 `10.0.0` 从未发布（nuget.org 上最新是 `9.0.153`）。手工把本地 `bin/Debug` 的 nupkg 喂给还原也没用：**包里的分析器不产出任何东西**，Core 会以数百个「不实现接口成员」错误失败。

**推论（两条，都很容易踩）：**

- 本仓库**只有 Debug 可构建**。「Release 不受益」的旧说法其实还说轻了。
- 基准因此以 `-c Debug -p:Optimize=true` 构建、并用 `InProcessEmitToolchain` 运行（`BenchmarkConfig.cs`）。**`-t:Rebuild` 是必需的**：增量构建会认为 Core 已最新而跳过它，留下 `dotnet test` 构出的未优化 DLL，BenchmarkDotNet 会直接拒绝。

---

## 六、生成器产物**不会**落到 `obj/` 下，除非显式打开

`obj/Debug/*/generated/...` 里那份 `*_VeloxJson.g.cs` 可能是**很久以前的遗留**。Roslyn 默认只在内存里持有生成结果；要落盘得加 `-p:EmitCompilerGeneratedFiles=true`。

**别用文件时间戳判断生成器跑没跑**——2026-10-04 就因此误判过一次，差点把一次真实的产物改动当成没生效。
