# Verification

本目录放的是**验证**，不是产品代码：不打包、不进任何 `PackageReference`。

| 东西 | 干什么 |
| --- | --- |
| `VeloxDev.Serialization.Benchmarks/` | 归档序列化引擎的基准（BenchmarkDotNet） |
| `HeadlessAdapters/` | 适配器的无头回归（WPF/Avalonia/WinForms/Razor/Jalium）：不显示窗口，普通 CI runner 可跑 |
| `agent-ui-harness.ps1` / `agent-web-harness.ps1` | 用真实输入驱动 demo 并截图（前者桌面、后者 CDP 驱动浏览器） |
| `agent-ui-probe.ps1` | 包一层：构建 → 杀残留 → 跑 harness → **校验窗口标题** → 裁 HUD → 与基线差分 |
| `verify-*-item-templates*.ps1` | 打包/编译模板项，检查镜像漂移；`verify-workflow-item-templates-all.ps1` 还统计**生成集的构建警告**（`warnings` 列），`-Strict` 时警告与漂移都会失败 |

---

## 跑一次 UI 探针：用 `agent-ui-probe.ps1`

```
./Src/Verification/agent-ui-probe.ps1 `
  -Project "Examples/Workflow/WPF Trimmed/Demo/Demo.csproj" `
  -Exe     "Examples/Workflow/WPF Trimmed/Demo/bin/Debug/net10.0-windows/Demo.exe" `
  -ExpectTitle "WPF Trimmed" -Crop "60,560,340,120" `
  -Actions "wheel:640,400,-120; wait:700; shot:plain" -SaveBaseline
```

基线是**按截图各自命名**的（`<shot>.baseline.png`）—— 改完源码后把最后一行换成 `-CompareBaseline`，它会打出每一张与**自己那份**基线的**差分像素数**。（一次运行里用同一个基线名会让每张都覆盖同一份文件，差分就变成「拿最后一张当基准」，别那么用。）

它替你把下面**五条纪律**跑掉。每一条都是**至少花掉过一轮错误结论**换来的（2026-10-11 那一轮七家探针）：

| # | 坑 | 症状 | 挡法 |
|---|---|---|---|
| 1 | **构建失败留下旧二进制** | 旧二进制与代码 bug **无法区分** —— 你会去查一段根本没跑过的代码 | 先构建，失败就**拒绝探针** |
| 2 | **七家 Trimmed demo 的可执行文件都叫 `Demo.exe`** | harness 按**进程名**找窗口 ⇒ 别家遗留的实例应答，**截到另一个应用、数字全是它的**，而且不报错 | 跑前杀残留；发现别家的 `Demo` 在跑就**大声警告** |
| 3 | **不核窗口标题就读数字** | 同上，只是你没发现 | 标题写进 `harness.log`，`-ExpectTitle` 不匹配就**失败** |
| 4 | **HUD 在 1280×800 截图的角落里** | 读一次数要开一张大图找那个小面板 | `-Crop` 裁出来 |
| 5 | **用眼睛判断「变没变」** | 48 与 50 的差别，肉眼靠不住 | `-SaveBaseline` 存、`-CompareBaseline` 出**差分像素数**（**按截图各自命名**，见下） |

**没被脚本覆盖、必须自己守的几条**（它们不是流程问题，是机制问题）：

- ⚠ **同一时刻只让一个 demo 在跑。** 并行驱动两家会互相污染 —— 脚本只能对**已知名字**（`Demo` / `Demo.Desktop`）报警，改过名的副本它认不出。
- ⚠ **`ScrollTo*` / `Offset` 赋值之后立刻读回，拿到的是旧值**（WPF 把 ScrollInfo 推到下一次布局）。判定一律以 **HUD / 下一帧**为准。
- ⚠ **涉及窗口/竞态的探针，先量一下自己动作之间的真实间隔。** harness 的动作**自带睡眠**（`wheel:` 220ms + `keyup:` 120ms + `ToClient` 70ms ≈ 410ms）⇒ 打不进更窄的窗口时会得到**假阴性**（「未复现」其实是「没打进去」）。需要时临时加无睡眠变体，用完还原。
- ⚠ **探针也可能给出「结构 ≠ 行为」的假前提**：拿 DOM/视觉树结构推行为，两次都被实测推翻（标尺看着在滚动区之外、因 `pointer-events:none` 其实照常响应；卡片里有 `ScrollViewer`、默认数据却根本不溢出所以缺陷不可见）。**能测就别推。**
- ⚠ **读位移要挑语义对齐的量具**：七家的滚动偏移符号**不统一**（WinForms 的 `panOffset` 就是反的），而 HUD 的 `视口(世界)`（视口原点的世界坐标）七家同一。跨家断言必须用后者。
- ⚠ **临时插桩与临时数据，用完还原**，并在报告里写清「种了什么 / 观察到什么 / 何时还原」。

---

## 跑适配器无头测试

```powershell
./Src/Verification/HeadlessAdapters/run-headless-tests.ps1
./Src/Verification/HeadlessAdapters/check-coverage.ps1
```

与上面的 UI 探针相反：这些套件**不打开窗口**，验证的是适配器把平台事件/布局翻译成模型状态的那一层。
今天覆盖 WPF / Avalonia / WinForms / Razor / Jalium；WinUI 与 MAUI 没有真正的无头宿主，
仍由 [`AUTO TEST`](../../Examples/Transition/AUTO%20TEST/AGENTS.md) 覆盖真实窗口那一条路。
细节与可行性见 [`HeadlessAdapters/README.md`](HeadlessAdapters/README.md)。

**还有一类「不在默认测试里跑」的验证：真模型测试。** 它们在 `VeloxDev.Core.Extension.Test` 里，由
`VELOXDEV_LIVE` 开关控制（`Agent/LiveModelGate.cs`）—— **不设就是 `Inconclusive`**，默认套件因此确定、免费、
2 秒；设了（任何非空、非 `0` 的值）才连上真模型，凭据仍取环境里的 `API_KEY_DEEPSEEK`。

```bash
# 默认：真模型用例全部跳过
dotnet test Src/Core/VeloxDev.Core.Extension.Test/VeloxDev.Core.Extension.Test.csproj -c Debug

# 只跑真模型那批（花模型调用；也要 key 在环境里）
VELOXDEV_LIVE=1 dotnet test Src/Core/VeloxDev.Core.Extension.Test/VeloxDev.Core.Extension.Test.csproj -c Debug \
  --filter "FullyQualifiedName~LiveTests"
```

**闸门为什么不是「环境里有没有 key」**：那样一来，手里有 key 的人（也就是唯一跑得动它们的人）每次全量跑都要付
七次真模型调用，而且要红要绿取决于模型当次怎么答 —— 套件对最该跑它的人反而不确定。key 是凭据，不是开关。

---

## 跑序列化基准

```bash
# 1) 以「Debug 配置 + 优化过的代码」构建 —— 两个条件都不能少，理由见下
#    -t:Rebuild 是必需的：增量构建会认为 VeloxDev.Core 已是最新而跳过它，
#    于是产物里留下上一次 dotnet test 构建的未优化 DLL，BenchmarkDotNet 会直接拒绝运行。
#    症状很安静：它只在 BenchmarkRun-*.log 里写两行 Validating benchmarks，
#    报告照样生成、但「结果」那一节是「本次运行没有产出可用的数据。」—— 不是崩，是空。
dotnet build Src/Verification/VeloxDev.Serialization.Benchmarks/VeloxDev.Serialization.Benchmarks.csproj \
             -c Debug -p:Optimize=true -t:Rebuild

# 2) 直接运行产物（不要用 dotnet run，它会再构建一次）
#    跑完之前不要再跑 dotnet test —— 它会把 Core 重新构回未优化版本。
#    不带参数 = 四档当量全跑 + 出一份 Markdown 报告
./Src/Verification/VeloxDev.Serialization.Benchmarks/bin/Debug/net10.0/VeloxDev.Serialization.Benchmarks.exe
```

**四档当量**（`Scales.cs` 一处定义，两个基准类共用）：小 `100` / 中 `1 000` / 大 `10 000` / 超大 `30 000`
个节点。`超大` 停在 30 000 是有意的：Newtonsoft 在 1 000 节点上单次操作就分配约 50 MB，
再上一个数量级量到的会是 GC 而不是序列化器。

**默认四档全跑**，所以报告恒为五张表。档位由命令行决定（`[ParamsSource]` 读 `Scales.Selected`），
因为 `[Params]` 是编译期常量 —— 那样收窄档位就得改代码。

| 怎么跑 | 花多久 | 什么时候用 |
| --- | --- | --- |
| 不带参数 | ~9 分钟 | 完整五张表的报告 |
| `--scale 10000` | 一档 | 只关心某一档 |
| `--scale 10000 --filter "*Stj_Serialize*"` | ~15 秒 | **只量一个数字** —— 调参时用这个 |

**为什么不能更快**：BenchmarkDotNet 每个用例要跑 jitting + pilot + warmup + actual，合计约 **10 次真实操作**；
而大档的一次操作是 0.1–1.2 秒。所以「24 个用例 × 约 10 次操作」就是分钟级，这是基准的构造成本，不是可以
绕过的开销。**它是一次测量，不是一次测试 —— 不要挂在每次改动上跑。** 单元测试是另一回事（约 45 秒）。
语料按档缓存（`Corpus.Shared`），否则每个用例都会重建一遍同样大的树。

**报告**：跑完写本工程目录下的 `BenchmarkDotNet.Artifacts/serialization-performance.md`（该目录被 `.gitignore`
按任意深度匹配，不计入仓库）。**产物落在工程目录而不是工作目录** —— BenchmarkDotNet 的默认是后者，从仓库根
启动就会把输出散到根上；`BenchmarkConfig` 因此用 `WithArtifactsPath`，路径从程序集位置（`bin/Debug/net10.0`
往上三层）解析，与从哪里启动无关。
**报告 = 五张表 + 一处自校 + 一章行为说明**：环境一张，四个当量各一张。每个当量的表里**一行一个序列化器**，
共四行 —— 本仓库、System.Text.Json（反射）、System.Text.Json（源生成）、Newtonsoft.Json。
**耗时与存储同表**（写/读各自的耗时与分配，加文档字符数），**本仓库那行加粗**，括号里是相对本仓库同方向的倍数。
**两行 System.Text.Json 都带 `StjSerializationBridge.cs` 的桥接配置**（跳过构造器建实例 + 一张派生表），
不配它两行连读都读不了这张图 —— 主构造器形参绑不上属性、接口成员没有判别符。桥接只是**配置**，
不是替它写的转换器；这条线守住，比较才成立（Newtonsoft 读同一张图只需要开两个开关）。
**耗时一律取中位数**（不取均值：这套测量天然右偏，GC 只往上加时间，均值会被尖峰拉高）；每一档的**噪声**
（同一个量在两类里各量一次之差）印在那张表下面，并且**写进每一格** —— 小于它的倍率标「噪声内」，那不是
结论是抖动。表后是**自校**（同一组数，外加每一侧自己的四分位距，用来看那个差是漂移还是尖峰），
再往后是一章**《归档序列化 · 行为与支持》**
—— 分节讲这套序列化本身（收录条件、API、成员规则、`[Archive]` / `[JsonIgnore]` / 钩子、容器与键、泛型、
拼写契约、失败形态、诊断），**以用例代码为主**，与本次测量的数字无关，所以是常量。
它由 `PerformanceReport.cs` 从 BenchmarkDotNet 的结构化结果生成，不解析控制台输出。

**只想看回归（不跑另外两家）**：`--filter "*SerializationBenchmarks*"`。报告仍会写，只是没有文档大小那一节。

### 为什么不写 `-c Release`

**本仓库目前根本无法在 Release 下构建 `VeloxDev.Core`。** 它在非 Debug 下引用
`VeloxDev.Core.Generator` 包（`Src/Core/VeloxDev.Core/VeloxDev.Core.csproj`），而 10.0.0 从未发布
（nuget.org 上最新是 `9.0.153`）。即使手工把它喂给还原，包里的分析器也不产出任何东西，Core 会以
**数百个「不实现接口成员」错误**失败。这与基准无关，是既有状态。

所以基准改用 **Debug 配置**构建——生成器走工程引用，这条链是通的——同时用 `-p:Optimize=true`
把优化打开（它是全局属性，会传给所有被引用工程，所以 `VeloxDev.Core` 也是优化过的 IL）。

### 为什么不另起进程

BenchmarkDotNet 默认会为每个基准新建工程并以 Release 重编译整个依赖图，正好撞上上面那条路。
`BenchmarkConfig` 因此改用 `InProcessEmitToolchain`，直接量已经构建好的程序集。

**代价**：进程内测量共享 JIT 与 GC，绝对值不如独立进程干净。它够用来做**同一台机器上的前后对比**，
不要把它当成可对外引用的绝对数字。

### 数字怎么用

同一台机器、同一次会话里比 Stage 前后的变化。`MemoryDiagnoser` 的分配列是这里最可靠的一维：
分配量不受 JIT 进度影响，耗时会。

**跨会话比就要带上机器**：把 CPU / 内存 / 系统 / 运行时 / 工具链和数字记在一起。测出来的数字
（连同环境）落在 [`memory/modules/Serialization/architecture.md`](../../memory/modules/Serialization/architecture.md) §四。

### 与 System.Text.Json / Newtonsoft 比

```bash
./Src/Verification/VeloxDev.Serialization.Benchmarks/bin/Debug/net10.0/VeloxDev.Serialization.Benchmarks.exe \
             --filter "*ComparisonBenchmarks*"
```

`ComparisonBenchmarks` 让**同一个对象图**过三家，且三家都开着引用保留（`ReferenceHandler.Preserve` /
`PreserveReferencesHandling.Objects`，Newtonsoft 另加 `TypeNameHandling.Auto`）—— 归档对每个对象都写
`$id`、对多态成员写 `$type`，不对齐这两项就是拿不同的活来比。`GlobalSetup` 会打印三份文档的字符数。

两处 **实测** 的 STJ 默认设置失败（不是推测）：写不了含 NaN/±Infinity 的图（要
`AllowNamedFloatingPointLiterals`），以及读不了主构造器类（构造器形参名与提升出来的属性名不同）。
