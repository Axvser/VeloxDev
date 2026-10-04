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

**工具的形状（2026-10-04 起）**：两个基准类共享 `Scales.cs` 里的四档当量 —— 小 100 / 中 1 000 / 大 10 000 /
超大 30 000 个节点。**默认四档全跑**（报告因此恒为五张表），`--scale N` 收窄到一档；档位走 `[ParamsSource]` 而不是
`[Params]`，因为后者是编译期常量，会让每次改动都为最慢的一档付钱。`SerializationBenchmarks` 只量归档自己
（回归用），`ComparisonBenchmarks` 让**同一个对象图**过归档 / System.Text.Json / Newtonsoft（三家都开引用
保留）。**一条命令跑完并留一份 Markdown 报告**到本工程目录下的
`BenchmarkDotNet.Artifacts/serialization-performance.md`。**报告就是五张表**：环境一张，四个当量各一张 ——
（用户 2026-10-04 定的形状）每个当量的表**一行一个序列化器**，**耗时与存储同表**（写/读各自的耗时与分配，
加文档字符数），**本仓库那行加粗**，括号里是相对本仓库同方向的倍数。表后是一段备注（下面 §四·二 那些结论
就在里面）与一处自校。报告由 `PerformanceReport.cs` 从 BenchmarkDotNet 的**结构化结果**生成，不解析控制台
输出 —— 分配那一项尤其要注意：指标键是 `Allocated Memory`，取错不会报错、只会让整列变成「—」。

> ⚠ **产物必须落在工程目录，不是工作目录。** BenchmarkDotNet 默认 `BenchmarkDotNet.Artifacts` 相对**当前
> 工作目录**，从仓库根启动就会把输出散到根上；`Artifacts.cs` 从程序集位置（`bin/Debug/net10.0` 往上三层）
> 解析绝对路径，`BenchmarkConfig` 用 `WithArtifactsPath` 钉住它 —— 报告也写同一处。`.gitignore` 里那条是
> 任意深度匹配，所以不计入仓库。
> ⚠ **`超大` 停在 30 000 是有意的**：Newtonsoft 在 1 000 节点上单次操作就分配约 50 MB，再上一个数量级
> 量到的会是 GC 而不是序列化器。
> ⚠ **入口不能把空参数交给 `BenchmarkSwitcher`**：它会进交互式选择，无人应答就什么也不跑、**还不报错**
> （报告里会是空的）。空参数走 `BenchmarkRunner.Run([两个类])`。
> ⚠ **一次全量跑是分钟级，这是构造成本、不是可绕过的开销**：BenchmarkDotNet 每个用例要 jitting + pilot +
> warmup + actual，合计约 **10 次真实操作**，而大档的一次操作是 0.1–1.2 秒 —— 「24 个用例 × 约 10 次操作」
> 就决定了量级。**它是一次测量，不是一次测试**（单元测试约 45 秒），不要挂在每次改动上跑；调参时用
> `--scale N --filter`，那是一条 15 秒的路。语料按档缓存（`Corpus.Shared`），否则每个用例都重建一遍同样的
> 树（实测每个用例约 13 秒花在构建与准备上）。

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

### 四·一、最近一次复测：归档特性集落地之后（2026-10-04）

`[Archivable]` / `[Archive(ArchiveOptions)]` / `[JsonIgnore]` 的四种 Condition / `required` 与
`[JsonRequired]` / 集合与数组的四种读法 / `byte[]` 走 base64 —— 这一整套落地之后复测：

| 用例 | Mean | StdDev | Allocated |
| --- | --- | --- | --- |
| Serialize 2 | 17.09 µs | 0.65 | 34.16 KB |
| Deserialize 2 | 38.80 µs | 0.55 | 53.72 KB |
| Serialize 1000 | 7 872.38 µs | 151.08 | 7 726.12 KB |
| Deserialize 1000 | 33 373.15 µs | 738.45 | 14 604.16 KB |
| Serialize 10000 | 92 093.94 µs | 1 572.21 | 77 172.83 KB |
| Deserialize 10000 | 387 622.61 µs | 17 764.68 | 146 632.69 KB |

**与「流式之后」那张表逐项比：耗时 −2.2% ~ +7.1%，分配量逐字节相同**（34.16 / 53.70→53.72 / 7 726.11→7 726.12 /
14 604.12→14 604.16 / 77 173.39→77 172.83 / 146 632.75→146 632.69）。最大的两条落在噪声里
（Deserialize 10000 的 StdDev 是均值的 4.6%）。**那轮改造在默认路径上是零代价的**，这是设计使然：
条件写出、枚举名字、必填核对、数组与只读集合都**只在文档真的用到它们时**才多走一步，
而 `ObservableCollection` 这种本来就能就地填的集合走的仍是原来那条。

**测量环境** —— 数字要跨会话比就必须带上它，否则「第二次跑的人」和「第一次」可能不是同一台机器：

| | |
| --- | --- |
| CPU | 11th Gen Intel Core i7-11800H @ 2.30 GHz，8 物理核 / 16 逻辑核 |
| 内存 | 15.7 GB（2 × 8 GB DDR4-3200，Micron） |
| 系统 | Windows 11 家庭中文版，build 26200，64 位 |
| 运行时 | .NET 10.0.12，X64 RyuJIT x86-64-v4（SDK 10.0.401） |
| 工具链 | `InProcessEmitToolchain`，3 warmup + 10 iterations，LaunchCount 1，`MemoryDiagnoser` |

**语料**（`Corpus.BuildTree`）**不覆盖新特性**：没有条件写出、没有 `EnumName`、没有必填成员、没有数组。
它量的是**既有文档那条路**，而那正是该量的一维 —— 新特性的开销只由用到它们的文档付。要量新特性，
得先给 `Corpus` 加形状。

> 工具后来改成**四档当量**（100 / 1 000 / 10 000 / 30 000），所以上表「2 节点」那一行不再可复跑；
> 中与大两档的数字仍与 §四·二 对照得上（差在噪声内）。

### 四·二、与 STJ / Newtonsoft 的同图对比（2026-10-04）

`ComparisonBenchmarks` 让**同一个对象图**过三家 —— 这是「快不快」唯一能回答的方式。三家都开着引用保留（`ReferenceHandler.Preserve` / `PreserveReferencesHandling.Objects`），Newtonsoft 另加 `TypeNameHandling.Auto`，因为归档对每个对象都写 `$id`、对多态成员写 `$type`，不对齐这两项就是拿不同的活来比。**四档当量**、同机同会话：

| 当量 · 节点 | 归档写 | STJ 写 | NST 写 | 归档读 | NST 读 | 文档（归档 / STJ / NST） |
| --- | --- | --- | --- | --- | --- | --- |
| 小 · 100 | 0.80 ms | 1.24 ms | 4.43 ms | 3.34 ms | 7.05 ms | 133 K / 312 K / 643 K |
| 中 · 1 000 | 9.64 ms | 16.48 ms | 50.43 ms | 45.54 ms | 84.86 ms | 1.33 M / 3.12 M / 6.37 M |
| 大 · 10 000 | **102.6 ms** | 181.6 ms | 541.6 ms | **320.0 ms** | 697.7 ms | 13.3 M / 31.5 M / 63.8 M |
| 超大 · 30 000 | 321.9 ms | 616.2 ms | 1 720.9 ms | 1 037.7 ms | 2 239.7 ms | 40.2 M / 94.7 M / 191.5 M |

分配（中档）：归档写/读 **7.54 / 14.10 MB**，STJ 写 9.69 MB，NST 写/读 49.25 / 24.63 MB。

**五条结论**：

1. **写**：逐档稳定地比 STJ 快 **1.5–1.9×**、比 Newtonsoft 快 **5.2–5.5×** —— 倍率不随规模漂。
2. **读**：比 Newtonsoft 快 **1.9–2.2×**；而 **STJ 读不了这张图**（见下）。读侧是它相对最弱的一环（读写比 3.1–4.7×，Newtonsoft 只有 1.6–2.1×）。
3. **文档小 2.34–2.36×（STJ）/ 4.76–4.81×（NST）**，**逐档稳定** —— 比例与规模无关，所以这不是「小图上侥幸」。
4. **规模上是线性的**：大 → 超大是 3× 数据，归档写 **3.14×** 时间、读 **3.24×**，Newtonsoft 写 3.18×、读 3.21×。两家的缩放形状一样。
5. **STJ 的源生成与反射持平**（中档 15.93 vs 16.48 ms）—— 成本在引用保留与图的形状上，不在元数据查找上。所以「拿反射版 STJ 比不公平」这个担心实测不成立。

**噪声：报告自带自校，而它给出的大小每次都不一样。** 同一个归档写被两个类各量了一次，两者之差就是这一档的噪声，最近几次分别是
`12.9 / 5.4 / 15.2 / 15.0%`、`21.8 / 14.3 / 7.5 / 2.9%`（小 → 超大）—— **最大的一次落在小档，另一次落在超大档**。
所以「哪一档最可信」没有固定答案，**用哪一档做判断取决于当次自校给的那个数**；小于约 1.2 的比率在两档噪声之内，不要当真。

> ⚠ **别拿被污染的那次当结论。** 曾经记过一次「归档读在大档到超大档是 3× 数据换 6.8× 时间」，那来自一次**我同时还在跑 demo 与重建**的测量。干净跑出来是 3.24×（线性）。量基准时不要在机器上做别的事 —— 这条比任何一条数字都值得记。

**STJ 在这张图上的两处默认设置失败**（实测，不是推测）：

- **写**：默认设置对 NaN/±Infinity 抛 `ArgumentException`，要 `AllowNamedFloatingPointLiterals` 才写得出去 —— 与 §三 那条「非有限值写成字符串」正是同一个分歧。
- **读**：`Each parameter in the deserialization constructor on type 'Offset' must bind to an object property or field`。这些 ViewModel 是**主构造器类**，提升出来的属性名（`Horizontal`）与构造器形参名（`left`）不同，STJ 直接拒绝；`IncludeFields` 也救不了（字段是 `_horizontal`）。**要用 STJ 读这套库自己的图，得先给库的类型加注解或写转换器** —— 而那正是归档格式在编译期生成读写器所省掉的事。

**这些数字不能推广**：语料是一棵**带引用保留与多态**的工作流树，那正是这套格式存在的理由；换成「五个属性、不开引用保留的 POCO」，STJ 会近得多，源生成也会开始有收益。三家写的成员集也不同（归档写生成契约，另外两家写公开面），所以文档大小差里有一部分是「写得少」。

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
