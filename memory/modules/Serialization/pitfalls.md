# Serialization — 坑

> 与 [architecture.md](architecture.md) / [extension.md](extension.md) 配套。
> 这里只放**不看代码就会踩**的东西 —— 每一条都带代码锚点，读记忆的 agent 能当场核。

---

## 一、数组与集合：支持到哪一层

**2026-10-04 之前这一段是「数组写得出、读不回」，现在是按声明类型分四种读法**（`Writers/VeloxJsonCodeWriter.cs` 的 `ReadCollection`）：

| 声明类型 | 读法 |
| --- | --- |
| `List<T>` / `ObservableCollection<T>` | 就地填；成员为 null 时先造一个再填 |
| `IReadOnlyList<T>` / `ICollection<T>` / `IEnumerable<T>` 等接口 | 读进 `new List<T>()` 再赋过去 |
| `T[]` | 读进 `new List<T>()` 再 `.ToArray()` |
| `HashSet<T>` / `Queue<T>` / `Stack<T>` | 读进 `new List<T>()` 再 `new 声明类型(list)` |

四种都由**声明类型**决定，类型实参在那一层是已知的，所以数组能在不碰 `Array.CreateInstance` 的前提下读回来 —— 那正是这套格式躲开的东西。

**仍然读不回来的是「嵌在容器里的数组」**：`List<int[]>`、`int[][]`。读到元素时手上只有一个 `Type`，按它造数组要反射。生成器**报 `VELOX_JSON_MEMBER001`**（`Base/VeloxJsonModel.cs` 的 `ReportNestedArray`），不是留到运行期炸。要改就声明成 `List<List<int>>`。

**`byte[]` 是标量，不是数组**：走 base64 字符串（`IsScalar` 里的 `IArrayTypeSymbol` 分支，排在数组那条分支**之前**，否则它会被当成普通序列）。与 STJ / Json.NET 一致。

**Map 同理**：成员为 null 时造实例再读，不再把文档里的项静默跳过（`ReadMap`）。

---

## 二、Map 的键，三种写法

| 键 | 写法 | 锚点 |
| --- | --- | --- |
| `string` / `object` | 键文本直接当属性名，读回是同一个字符串 | `VeloxJsonSerializer.cs` 的 `ReadMapKey` |
| 枚举 | 写 `ToString()` 的**名字**，读 `Enum.Parse(ignoreCase: true)` —— 一致 | 同上 |
| 其它（`int` / `double` / …） | 写读**同为不变区域性** | 写 `WriteMap`，读 `ReadMapKey` |

第三行曾经是不对称的（写用当前区域性、读用 invariant），`double` / `decimal` 这类 `IFormattable` 键在非 invariant 区域下会漂。**改任何一侧都要同时看另一侧。**

**接口键的字典是这套格式独有的形状**：键写成键对象的**引用 id**，而且映射本身不写 `$id`、不写 `$type`。STJ / Json.NET 都没有这种形状，互操作时对不上。

**键类型现在也进闭包**（2026-10-04 起 `Reachable` 交出 `VeloxJsonMember.KeyType`），走的是与其它成员类型**同一条**路 —— 接口键因此能拿到实现类的条目。但两件事**没解决，是已知契约**：

- **写侧只发 id、不写对象**（`VeloxJsonWriter.GetOrAddReference` 只分配 id）。所以每个键对象都必须在文档**别处以完整对象出现过**，`ResolveReference` 才解析得回来；不成立时 [读侧当场 `SkipValue`，条目静默丢掉](/Src/Core/VeloxDev.Core/Serialization/VeloxJsonSerializer.cs)。今天靠的是「键对象同时是图里别处的节点」这个约定，而它取决于遍历顺序。
- **`object` 键不走引用 id**：`Classify` 只在 `TypeArguments[0].TypeKind == Interface` 时置 `InterfaceKeyed`，而 `object` 的 `TypeKind` 是 `Class` —— 于是它走 `Convert.ToString`，读回 `ReadMapKey` 给的**字符串**，装箱对象降级。这是**故意的**：`SlotEnumerator.conditionMap` 的 `Dictionary<object, TSlot>` 重建就假定键是字符串，改它等于改格式契约。

---

## 三、`DateTime` 与值拼写

写 `ToString("O")`，读 `DateTime.Parse(text, InvariantCulture, DateTimeStyles.RoundtripKind)` —— 生成器侧与引擎侧各一处，**两边都要带 `RoundtripKind`**。不带它的时候 `Kind = Utc` 的值（`…Z`）读回来是 `Local` 且时刻被平移（这个洞 2026-10-04 修掉了）。

其余值拼写见 [architecture.md](architecture.md) §三，那四条是冻结契约：`double` 最短往返且整数值补 `.0`、NaN/±Infinity 写成字符串、枚举默认写底层整数（**要写名字用 `[Archive(ArchiveOptions.EnumName)]`**，逐成员生效）、非 ASCII 不转义。

---

## 四、get-only 属性默认不进文档（现在有出口）

`ReadMembers` 要求 `property.SetMethod` 是 public。只读计算属性默认**既不写出、也不读入**。

`[Archive(ArchiveOptions.KeepProperty)]` 把它写进文档，但**读侧仍然跳过**：没有 setter 可赋值，生成器干脆不给它发读分支（`Writers/VeloxJsonCodeWriter.cs` 里 `member.WriteOnly` 跳过的就是它）。**往返仍然丢这个成员** —— 那是诚实的结局。

**排除单个成员**用 `[Archive(ArchiveOptions.IgnoreField)]` 或 .NET 自带的 `[JsonIgnore]`（`Always` 排除、`Never` 放行、两个条件值条件写出）。按**声明类型**整批排除仍然只有 `SerializationOptions.WithExcludedPropertyTypes`（`ViewModelSerializer.cs` → `VeloxJsonSerializer.cs`），「排掉基类、留下派生」这种粒度做不到。

---

## 五、成员声明成基类：派生类**跨程序集不会被自动收进来**

闭包那一趟（`Base/VeloxJsonModel.cs`）按一张祖先索引（`BuildFamilyIndex`，键是「自己 + 基类链 + 全部接口」）收集**本程序集里派生 / 实现了这个类型**的具体类型。接口、抽象类、**具体基类**都走这一条 —— 2026-10-04 起具体基类也展开，此前只有接口与抽象类（那是「声明成 `Animal`、装的却是 `Dog`」写得出读不回来的原因）。两点要记住：

- 抽象类型与接口**自己永远没有条目** —— `IsWritableType` 只认 `Class`/`Struct` 且显式拒绝抽象，所以 `ReaderFor(I你的接口)` 恒为 `null`。
- 候选集只有 `candidates`，即**本程序集**的类型；跨程序集的具体类要 `IsWritableType` 放行，而它对外程序集只放**封闭泛型 + 公开无参构造**。所以一个普通具体类若在别的程序集、且在那儿不是 root，**不会被自动收进来**。

失败形态：

| 情况 | 写 | 读 |
| --- | --- | --- |
| 实现者与抽象**同程序集**且可访问 | 自动计入，成功 | 成功 |
| 实现者在**别的程序集**且在那儿不是 root | 抛 `MissingWriter` | 抛 `MissingReader` |
| ↳ 例外：该类型实现了 `IEnumerable` | **静默写成数组**，不抛 | — |
| 成员声明成 `object` | 同上 | **不抛**：`ReadByShape` 降级成 `Dictionary<string, object?>` |
| 被计入但**没有公开无参构造** | 成功 | `Create()` 抛 `NotSupportedException` |

**修法**：给具体类型贴 `[Archivable]`（或 `[Archivable(typeof(它))]`），让它在自己所在的程序集里成为 root。前提是那个程序集引用了 `VeloxDev.MVVM`，否则生成器在那一边根本不跑。**不要**加反射兜底。

---

## 五·五、`$type` 的名字是注册表的**键**，泛型要带上实参

`NameOf` / `TypeOf` 是一对字典查找，`ReadObjectValue` 靠 `TypeOf($type) ?? 声明类型` 落回一个类型。所以名字必须**唯一标识一个封闭类型**。

**2026-10-04 之前泛型名字不带类型实参**（`WrittenName` 用的是 `symbol.Name`），于是同一泛型定义的两个封闭实例注册了**同一个字符串** —— 实测两个程序集都写过 `"VeloxDev.WorkflowSystem.SlotEnumerator, VeloxDev.Core"`（一个给 `SlotEnumerator<SlotDefaultViewModel>`，一个给 `SlotEnumerator<Demo.ViewModels.SlotViewModel>`）。`TypesByName[那个名字]` 谁后注册谁赢。

- **写侧不受影响**：读写器表按 `Type` 索引；且 `$type` 只在声明类型 ≠ 运行期类型时才写。
- **读侧会受影响**，只在 `$type` 真的出现时：解析可能落到另一个封闭实例 → `Create()` 造出错类型 → 生成 reader 那句 `(SlotEnumerator<X>)` 转型失败。
- 今天没被触发：`SlotEnumerator` 只作为**具体声明类型**出现（声明类型 == 运行期类型 → 不写 `$type`），而四份黄金文件里的 `$type` **全是非泛型**。
- **`ConditionalSlot<T>` 曾经同样会撞**，不止 SlotEnumerator。

现在 `WrittenName` **逐层**带上实参：`SlotEnumerator<VeloxDev.WorkflowSystem.SlotDefaultViewModel, VeloxDev.Core>, VeloxDev.Core`。**非泛型类型的名字一个字节没变**，所以黄金文件不受影响（这是能安全改的前提）。

**嵌套泛型要逐层写，不能攒到最内层**：`A<int>.B<string>` 与 `A.B<int, string>` 是两个类型，挤平就拼成同一个字符串。所以是 `Probe.Envelope<…SlotDefaultViewModel, VeloxDev.Core>+Inner<…>, Probe` —— 每层的实参紧跟自己那层的名字，嵌套用 `+` 连（与 `Type.FullName` 一致）。

**Roslyn 的 `TypeArguments` 是平铺的**：包含类型的实参也在里面，**外层在前**。自己那一段靠 `Arity` 从尾部切（`Skip(Length - Arity)`）—— 实测证实了顺序，而且这个切法在「将来改成只含自己的实参」时仍然正确（那时 `Length == Arity`）。判定在 `OwnTypeArgumentsOf`。

**注意这个字符串没有任何人解析它** —— 所以它只需要稳定且单射，不必是 `Type.GetType` 认的形式。附带一个已知边界：实参若来自 BCL（`int`、`string`），它的程序集名会随 TFM 变（`System.Private.CoreLib` / `mscorlib`），跨 TFM 读旧文档时那一段名字可能对不上。

---

## 六、`[Archive]` 与钩子的实现约束

三条都实测过，撞上时不会报错 —— 只会静默少一个成员或一次回调。

1. **生成器不能引用它为之生成代码的程序集。** 所以 `ArchiveOptions` 在生成器里镜像成 `ArchiveFlags`，枚举值只能手工保持同步。特性实参到达时是**底层整数**，不是枚举。
   **`JsonIgnoreCondition` 故意没有镜像**：它按名字判（`ReadJsonIgnoreCondition` 拿常量值反查字段名再 `switch`）。理由是实测踩过 —— 它的顺序是 `Never=0, Always=1, WhenWritingDefault=2, WhenWritingNull=3`（.NET 11 又加了 `WhenWriting`/`WhenReading`），我第一版按 `Always/Never/WhenWritingNull/WhenWritingDefault` 记，**把后两个写反了**，症状是 `WhenWritingDefault` 静默不生效。**枚举顺序不是它表达的意思，别记它**。
2. **源生成器看不见别的生成器的产物。** `[VeloxProperty]` 提升出来的属性在 VeloxJson 生成器的视图里**不存在** —— 所以「忽略字段、改用属性」不可表达（两者产出的代码完全一样），`IgnoreField` 因此取「该成员整个不进文档」这个唯一可实现、且此前真正缺位的语义。
3. **引用程序集剥掉非 public 成员 —— 消费方看不见它们。** 实测：测试程序集看 `SlotEnumerator<SlotDefaultViewModel>` 得到 `members=51`，里面**没有** `internal` 的 `OnDeserializing`/`OnDeserialized`。所以**钩子方法要能被别的程序集调，就必须是 `public`**；`internal` 只在「类型由它自己的程序集序列化」时够用。判据是 `IsReachableFromGeneratedCode`，它按**程序集**判。
   **推论**：外程序集里 `internal`/`private` 的钩子生成器**根本看不到**，也就**无法**为它出声 —— 症状是那个回调静默不跑。`VELOX_JSON_HOOK002` / `VELOX_JSON_MEMBER001` 只覆盖够得着却不可调的情况。

**钩子就是 BCL 那四个**（`System.Runtime.Serialization` 的 `[OnSerializing]` / `[OnSerialized]` / `[OnDeserializing]` / `[OnDeserialized]`），生成器沿基类链先基后派生地调，带 `StreamingContext` 的传 `default(...)`。四个 `IVeloxJson*` 钩子接口已删。

> ⚠ **这四条特性在 2026-10-04 之前是死的**：它们一直写在这些方法上，而生成器只认接口 —— 每个钩子都拖着一个不生效的转发壳。别再照着那种写法加钩子。

---

## 七、编译器合成的特性，源码符号上看不到

**`[RequiredMember]` 是编译器在 emit 阶段合成的**，所以对**本次编译里声明的**类型，`ISymbol.GetAttributes()` **看不到它** —— 只有从元数据读回来的符号才有。我第一版按特性找 required 成员，实测静默失败：生成的 `Create()` 里没有对象初始化器，于是带 required 成员的类型**编不过**。

判据是编译器 API `IPropertySymbol.IsRequired` / `IFieldSymbol.IsRequired`（**Roslyn ≥ 4.4**，生成器已从 4.3.1 抬到 4.8.0；`VeloxDev.Core.Test` 那处按它自己的注释要同步抬）。判定与工厂初始化器在 `Base/RequiredMembers.cs`，**两个生成器共用** —— VeloxJson 与 AIContextTree 都要发工厂调用，那个洞曾经两边都有。

同类的还有一处：**`typeof(可空的枚举)` 拿到的是 `Nullable<T>`，不是枚举本身**，`Enum.Parse` 会当场抛「Type provided must be an Enum」。写 `EnumName` 的读法时，`typeof` 要用 `UnwrapNullable` 之后的类型，而赋值那侧的转型仍用可空类型。

---

## 八、两条链路是手写抄的两份，靠一条测试拴住

写侧有两张手写的标量表：`TryWriteScalar`（同步）与 `TryWriteScalarAsync`（异步）。**没有任何东西强制它们一致** —— 2026-10-04 给同步那份加了 `byte[]` 而忘了异步那份，异步链路就把 `byte[]` 写成了数字数组，而读侧按 base64 读，异步往返当场 `FormatException`。

没有合并两张表（那是横跨整个标量面的重构），而是加了 `ShapeRoundTripTests.TheTwoChains_SpellEveryScalarTheSameWay`：同一种对象过两条路，文档必须逐字节相同。**加新标量时它会红** —— 那就是它存在的意义。

---

## 九、与别家的边界：键名相同，格式不通

**和 System.Text.Json / Newtonsoft.Json 不兼容 —— 直接互读不了**（2026-10-05 核实）。三处结构差异：

| | 归档 | System.Text.Json | Newtonsoft |
| --- | --- | --- | --- |
| `$type` 的值 | 注册表键 `Namespace.Type, Assembly`（泛型带实参，`WrittenName`） | `FullName`，无程序集 | 程序集限定名 |
| 集合 | 裸数组 | `{"$id":…,"$values":[…]}` | 裸数组 |
| 成员集 | **生成契约** | 反射公开面 | 反射公开面 |

**键名却是同一套**（`$id` / `$ref` / `$type`），`$id` 也同样是带引号的字符串 —— 所以它**看起来**像同一份格式。
**但值对不上、形状也对不上；这个巧合不是互通。** 顺带记一句：STJ 那个 `$type` 是基准里的桥接**自己定的**
（`StjSerializationBridge.DiscriminatorOf` = `type.FullName`），不是 STJ 的任何标准 —— 换个人写就是另一个词表。

**喂进来会怎样**：`$type` 进不了注册表 → 退回**声明类型** —— 具体类**静默丢多态**，接口 / 抽象抛 `MissingReader`。

**语义层可转换，成员集那条不可逆**：枚举两边都是底层整数、`byte[]` 两边都是 base64、空容器两边都是
`{}` / `[]`；几处标量拼写不同（`double` 的整数值这里补 `.0`、`DateTime` 这里写 `"O"`、非 ASCII 这里不转义）
—— 都是文本差异，宽容的解析器吃得下。但**这里少写的那些（命令、`Helper`、`EventContext`、`IsBusy`）
在别家的文档里有、在这里没有**，所以转换是有损的。

**这是立场不是缺陷**：闭世界与零反射是能裁剪、能 AOT 的前提，代价就是不与别家同源；库从未承诺互操作。
报告那一章的同名节（§十二）就是写给读者的这一版。

---

## 十、两套实现的不对称：四处只在补覆盖时露出来的缺陷（2026-10-05 修）

补覆盖（行 81.4% → 91.0%、分支 67.2% → 77.8%）时新加的测试当场抓出四处。共同点都是
**同步面有测试、异步面没有**，或者**语料的形状没走到**：

1. **异步面的成员名扫描不跳转义字符**（`VeloxJsonReader.Async.cs` 的 `NextMemberAsync`）：扫到 `\` 只置标志就
   `_position++`，于是把被转义的 `"` 当结束引号。名字里的转义只来自 **map 的键**（生成器那条链比对标识符，永不转义），
   所以只有 shape 驱动的 map 会走到；同步面走的是另一条重载（`NextMember(out name)`，整串读），一直没露。
2. **`DecodeMemberName` 不能用在异步面**：它回退后读的是同步的 `ReadQuoted()`，流源上读不到只会返回 false
   （症状：只在恰好截断时报 unterminated）。异步面因此有自己的 `DecodeMemberNameAsync`（走 `ReadQuotedAsync`）。
3. **压缩下限钉晚了一格**（`VeloxJsonReader.cs` 的 `PrepareSpace`）：`_memberHeld` 只保到 `_memberStart`，而上面那次
   回退要到 `_memberStart - 1`（开引号）—— 单字符分块 + 转义名字时回退落在窗口原点之外，`CharAt` 越界。现钉 `_memberStart - 1`。
4. **`ReadArray`/`ReadArrayAsync` 只 `Add` 不清空**：文档说的是成员**现在有什么**，不是再添几个。成员的集合初值非空时
   （`Dictionary<string,List<int>>` 里塞了 `[1,2]`），加载会追加成 `[1,2,1,2]`。**语料里的集合初值都是空的**，所以
   幂等与黄金测试都看不见 —— 这是语料的一个盲区。现 `if (!target.IsFixedSize) target.Clear();`（定长数组跳过）。

5. **`ReadMap`/`ReadMapAsync` 逐键赋值，没有清空**（同日补齐）：成员的 map 里那些**文档没有的初值键**会活过加载，
   回写出来就是多出来的字节（实测一个 `{"stale":9}` 让文档长了 50 字节）。与第 4 条同族、方向相反（多一格而不是重复）：
   一起改成 `if (!target.IsFixedSize) target.Clear();`。判据是 `MemberInitializerTests` 的
   `write(read(document)) == document` —— 每个成员的初值都与文档**不重合**，所以「替换」和「合并」给不同答案。
6. **生成的 setter 用 `Object.Equals` 判「没变」，对浮点是错的**（生成器 `Analizer.cs`，同日修）：
   `(-0.0).Equals(0.0)` 为真，于是字段已经握着 `+0.0` 时赋 `-0.0` 会被当成没变、直接 `return` —— **符号永远进不去**，
   回写出来 `-0.0` 变 `0.0`。这在**生成器**里（每个消费方的每个属性都受影响），不是引擎。现在浮点（含 `Nullable<T>`）
   改比位模式（`BitConverter.DoubleToInt64Bits` 两边都接，`float` 按值拓宽、符号与 NaN 载荷都保得住）。
   判据 `NumberSpellingTests`：`-0.0`、`-0.0f`、`double?` 的 `-0.0` 各断言**位模式**（`==` 对它们为真，说明不了问题），
   并对照一个**集合元素**里的 `-0.0`（那里没有属性守卫，本来就是对的）。

`VeloxJsonObject.Reindex` 也在这轮修的（`VeloxJsonValue.cs`）：它原来 `_index.Clear()` 后只从 `from` 重建，
于是删掉中间一个成员，**它之前**的成员就从索引里消失（`Has` / 索引器都拿不到）。
