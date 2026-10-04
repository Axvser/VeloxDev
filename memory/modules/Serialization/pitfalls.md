# Serialization — 坑

> 与 [architecture.md](architecture.md) / [extension.md](extension.md) 配套。
> 这里只放**写得出、读不回**这类静默不对称 —— 每一类都带代码锚点，读记忆的 agent 能当场核。

---

## 一、数组（含 `byte[]`）写得出去、读不回来

- **写侧**：`byte[]` / `int[]` 既不是标量也不是容器，落到 `WriteValue` 的 `IEnumerable` 分支，写成**数字数组**（`Src/Core/VeloxDev.Core/Serialization/VeloxJsonSerializer.cs:169`）。
- **读侧**：`Classify` 不认数组 —— 它要求 `INamedTypeSymbol` 且恰好一个类型参数（`Base/VeloxJsonModel.cs:954`），数组是 `IArrayTypeSymbol`，于是成员落 `VeloxJsonMemberKind.Object`，生成的 reader 走 `default` 分支调 `ReadValue`（`Writers/VeloxJsonCodeWriter.cs:259`）。
- `ReadValue` 只拦「声明类型不是数组的顶层数组」（`VeloxJsonSerializer.cs:407`），**声明类型就是数组时不拦**，于是进 `ReadObjectValue`，`BeginObject` 对着 `[` 抛 `expected '{'`（`VeloxJsonReader.cs:210`）。

**没有 `byte[]` 的 base64 通道** —— STJ / Json.NET 默认都写 base64，这里不是。

推论：要在文档里带一段定长数据，就把成员声明成 `List<T>`；二进制走 `string`（自己 base64 一下）。

---

## 二、只读集合是**静默**丢数据，不报错

`IReadOnlyList<T>` / `IReadOnlyCollection<T>` 被 `Classify` 认成 `Collection`（`Base/VeloxJsonModel.cs:998` 的 `IsSequenceType`），但生成 reader 的 Collection 分支要求声明类型实现 `System.Collections.IList`，不满足就直接 `SkipValue()`（`Writers/VeloxJsonCodeWriter.cs:241`）。
**文档里有值，读回来是空集合，没有任何异常。** 想往返就得声明成 `List<T>` / `ObservableCollection<T>`。

---

## 三、Map 的键，三种写法三种代价

| 键 | 写法 | 锚点 |
| --- | --- | --- |
| `string` / `object` | 键文本直接当属性名，读回是同一个字符串 | `VeloxJsonSerializer.cs:585` |
| 枚举 | 写 `ToString()` 的**名字**，读 `Enum.Parse(ignoreCase: true)` —— 一致 | `:586` |
| 其它（`int` / `double` / …） | 写 `entry.Key.ToString()` 用**当前区域性**，读 `Convert.ChangeType(..., InvariantCulture)` | 写 `:643`，读 `:588` |

**第三行是不对称的**：`double` / `decimal` 这类 `IFormattable` 键在非 invariant 区域写下、按 invariant 读回，会漂。

**接口键的字典是这套格式独有的形状**：键写成键对象的**引用 id**，而且映射本身不写 `$id`、不写 `$type`（`VeloxJsonSerializer.cs:621-633`）。STJ / Json.NET 都没有这种形状，互操作时对不上。

---

## 四、`DateTime` 的 `Kind` 不往返

写 `ToString("O")`（`VeloxJsonSerializer.cs:236`），读 `DateTime.Parse(text, InvariantCulture)` —— 生成器侧 `Writers/VeloxJsonCodeWriter.cs:298`，引擎侧 `VeloxJsonSerializer.cs:439-440`。
两边都**没有 `DateTimeStyles.RoundtripKind`**：`Kind = Utc` 的值（`…Z` 结尾）读回来是 `Local` 且时刻被平移，`Unspecified` 原样读回。STJ 与 Json.NET 默认都保留 `Kind`。

---

## 五、get-only 属性默认不进文档（现在有出口）

`ReadMembers` 要求 `property.SetMethod` 是 public（`Base/VeloxJsonModel.cs:813`）。只读计算属性默认**既不写出、也不读入** —— 与 STJ / Json.NET 的默认（会写出）相反。

`[Archive(ArchiveOptions.KeepProperty)]` 把它写进文档，但**读侧仍然跳过**：没有 setter 可赋值，生成器干脆不给它发读分支（`Writers/VeloxJsonCodeWriter.cs` 里 `member.WriteOnly` 跳过的就是它，于是那个值落到「读不懂的成员跳过」上）。**往返仍然丢这个成员** —— 那是诚实的结局。

**排除单个成员**用 `[Archive(ArchiveOptions.IgnoreField)]`。按**声明类型**整批排除仍然只有 `SerializationOptions.WithExcludedPropertyTypes`，它精确匹配声明类型（`ViewModelSerializer.cs:44` → `VeloxJsonSerializer.cs:44`），「排掉基类、留下派生」这种粒度做不到。

---

## 六、成员声明成接口 / 抽象类：实现者**跨程序集不会被自动收进来**

闭包那一趟（`Base/VeloxJsonModel.cs:301-332`）遇到接口或抽象基类时把它**跳过**（`:314`），改为收集「实现了这个契约」的具体类型（`:305-318`）。两点要记住：

- 抽象类型**自己永远没有条目** —— `IsWritableType` 只认 `Class`/`Struct` 且显式拒绝抽象（`:486`、`:490`），所以 `ReaderFor(I你的接口)` 恒为 `null`。
- 候选集只有 `candidates`，即**本程序集**的类型（`:272`）；跨程序集的具体类要 `IsWritableType` 放行，而它对外程序集只放**封闭泛型 + 公开无参构造**（`:498-501`）。所以一个普通具体类若在别的程序集、且在那儿不是 root，**不会被自动收进来**。

失败形态（都验过代码路径）：

| 情况 | 写 | 读 |
| --- | --- | --- |
| 实现者与抽象**同程序集**且可访问 | 自动计入，成功 | 成功 |
| 实现者在**别的程序集**且在那儿不是 root | 抛 `MissingWriter`（`VeloxJsonSerializer.cs:185`） | 抛 `MissingReader`（`:458`） |
| ↳ 例外：该类型实现了 `IEnumerable` | **静默写成数组**，不抛（`:169`） | — |
| 成员声明成 `object` | 同上 | **不抛**：`ReadByShape` 降级成 `Dictionary<string, object?>`（`:460-464`） |
| 被计入但**没有公开无参构造** | 成功 | `Create()` 抛 `NotSupportedException`（`Writers/VeloxJsonCodeWriter.cs:153-160`） |

两个异常的消息都自带线索：`MissingReader` 额外给出文档里那个 `$type` 名字，所以它能直接指出是哪个类型漏了。

**修法**：给具体类型贴 `[Archivable]`，让它在自己所在的程序集里成为 root（`Base/VeloxJsonModel.cs:430`）。前提是那个程序集引用了 `VeloxDev.MVVM`（`VeloxJson.cs` 的 `Applies` 判据），否则生成器在那一边根本不跑。**不要**加反射兜底。

---

## 七、`[Archive]` 与钩子的三条实现约束

前两条是源生成器的固有形状，第三条是 .NET 的。三条都实测过，撞上时不会报错 —— 只会静默少一个成员或一次回调。

1. **生成器不能引用它为之生成代码的程序集。** 所以 `ArchiveOptions` 在生成器里镜像成 `ArchiveFlags`（`Base/VeloxJsonModel.cs:74-84` 附近），枚举值只能手工保持同步 —— 那是这个文件里唯一一处手工对齐的数字。特性实参到达时是**底层整数**，不是枚举。
2. **源生成器看不见别的生成器的产物。** `[VeloxProperty]` 提升出来的属性在 VeloxJson 生成器的视图里**不存在**。所以「忽略字段、改用属性」不可表达 —— 两者产出的代码完全一样（同一个名字、同一个类型，都从字段那边推出来）。`IgnoreField` 因此取「该成员整个不进文档」这个唯一可实现、且此前真正缺位的语义。同理，`HasCorrespondingProperty` 对提升出来的属性找不到，只有作者手写的同名属性才会命中。
3. **引用程序集剥掉非 public 成员 —— 消费方看不见它们。** 实测：测试程序集看 `SlotEnumerator<SlotDefaultViewModel>` 得到 `members=51`，里面**没有** `internal` 的 `OnDeserializing`/`OnDeserialized`。所以**钩子方法要能被别的程序集调，就必须是 `public`**；`internal` 只在「类型由它自己的程序集序列化」时够用（`Anchor`/`Size`/`BranchSegment`/`BranchOption` 属于这种）。判据在 `Base/VeloxJsonModel.cs` 的 `IsReachableFromGeneratedCode`，它按**程序集**判。

**钩子就是 BCL 那四个**（`System.Runtime.Serialization` 的 `[OnSerializing]` / `[OnSerialized]` / `[OnDeserializing]` / `[OnDeserialized]`），生成器沿基类链先基后派生地调，带 `StreamingContext` 的传 `default(...)`。四个 `IVeloxJson*` 钩子接口已删。

> ⚠ **这四条特性在 2026-10-04 之前是死的**：它们一直写在这些方法上，而生成器只认接口 —— 每个钩子都拖着一个不生效的转发壳（`Anchor.cs` 里的 `private void OnSerializing(StreamingContext) => ((IVeloxJsonSerializing)this).OnSerializing();`）。别再照着那种写法加钩子。

**第三条还有一个陷阱**：诊断只能报在**看得见**的东西上。外程序集里 `internal`/`private` 的钩子在消费方编译里根本不存在，所以生成器**无法**为它出声 —— 症状是那个回调静默不跑。`VELOX_JSON_HOOK002` / `VELOX_JSON_MEMBER001` 只覆盖够得着却不可调的情况（同程序集的 `private`、跨程序集的 `protected` 之类）。
