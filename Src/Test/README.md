# Test

本目录放的是**测试与验证**，不是产品代码：不打包、不进任何 `PackageReference`。

| 子目录 | 放什么 | 怎么跑 |
| --- | --- | --- |
| `Unit/` | 产品自身的 MSTest 套件（`Core/`、`Cli/`）；CLI 的 `Test.Copy` / `Test.Plugin` 两个夹具工程也在里面 | `dotnet test <csproj>` |
| `Headless/` | 适配器的无头回归（WPF/Avalonia/WinForms/Razor/Jalium）与共享夹具 `VeloxDev.AdapterTestKit`：不显示窗口，普通 CI runner 可跑 | `Headless/run-headless-tests.ps1` |
| `Benchmarks/` | BenchmarkDotNet 基准与有头视图管线基准（含 WinUI/MAUI/Razor 独立宿主） | 见下文各节 |
| `AutoTest/` | 维护中的真实窗口验收套件：七家 demo 的端到端行为（UI Automation / Playwright），刻意不在 `VeloxDev.slnx` 里 | `AutoTest/run-autotest.ps1`（一键构建 demo + 跑验收） |
| `Harness/` | 一次性验证脚本：UI 探针、CDP 浏览器探针、模板项校验 | `Harness/agent-ui-probe.ps1` 等 |

归属判据：**能在普通测试进程里断言** → `Unit/`；**不打开窗口、但要拉 UI 框架** → `Headless/`；**要量性能** → `Benchmarks/`；**要维护的端到端验收** → `AutoTest/`；**一次性的桌面/浏览器探针** → `Harness/`。

| 脚本 | 干什么 |
| --- | --- |
| `Harness/agent-ui-harness.ps1` / `Harness/agent-web-harness.ps1` | 用真实输入驱动 demo 并截图（前者桌面、后者 CDP 驱动浏览器） |
| `Harness/agent-ui-probe.ps1` | 包一层：构建 → 杀残留 → 跑 harness → **校验窗口标题** → 裁 HUD → 与基线差分 |
| `Harness/verify-*-item-templates*.ps1` | 打包/编译模板项，检查镜像漂移；`verify-workflow-item-templates-all.ps1` 还统计**生成集的构建警告**（`warnings` 列），`-Strict` 时警告与漂移都会失败 |

---

## 跑一次 UI 探针：用 `agent-ui-probe.ps1`

```
./Src/Test/Harness/agent-ui-probe.ps1 `
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
./Src/Test/Headless/run-headless-tests.ps1
./Src/Test/Headless/check-coverage.ps1
```

与上面的 UI 探针相反：这些套件**不打开窗口**，验证的是适配器把平台事件/布局翻译成模型状态的那一层。
今天覆盖 WPF / Avalonia / WinForms / Razor / Jalium；WinUI 与 MAUI 没有真正的无头宿主，
仍由 [`AUTO TEST`](AutoTest/AGENTS.md) 覆盖真实窗口那一条路；下一节就是它的一键入口。
细节与可行性见 [`Headless/README.md`](Headless/README.md)。

**还有一类「不在默认测试里跑」的验证：真模型测试。** 它们在 `VeloxDev.Core.Extension.Test` 里，由
`VELOXDEV_LIVE` 开关控制（`Agent/LiveModelGate.cs`）—— **不设就是 `Inconclusive`**，默认套件因此确定、免费、
2 秒；设了（任何非空、非 `0` 的值）才连上真模型，凭据仍取环境里的 `API_KEY_DEEPSEEK`。

```bash
# 默认：真模型用例全部跳过
dotnet test Src/Test/Unit/Core/VeloxDev.Core.Extension.Test/VeloxDev.Core.Extension.Test.csproj -c Debug

# 只跑真模型那批（花模型调用；也要 key 在环境里）
VELOXDEV_LIVE=1 dotnet test Src/Test/Unit/Core/VeloxDev.Core.Extension.Test/VeloxDev.Core.Extension.Test.csproj -c Debug \
  --filter "FullyQualifiedName~LiveTests"
```

**闸门为什么不是「环境里有没有 key」**：那样一来，手里有 key 的人（也就是唯一跑得动它们的人）每次全量跑都要付
七次真模型调用，而且要红要绿取决于模型当次怎么答 —— 套件对最该跑它的人反而不确定。key 是凭据，不是开关。

---

## 跑真实窗口验收：一键 `run-autotest.ps1`

```powershell
# 全部七家，快速判定（脚本自动构建对应 demo）
./Src/Test/AutoTest/run-autotest.ps1

# 单家迭代、或想看窗口
./Src/Test/AutoTest/run-autotest.ps1 -Platform WPF
./Src/Test/AutoTest/run-autotest.ps1 -Watch

# 刚构建过 demo，跳过构建
./Src/Test/AutoTest/run-autotest.ps1 -NoBuild
```

脚本把 [AutoTest/AGENTS.md](AutoTest/AGENTS.md) §1 原先手写的步骤合成一条命令：按平台构建 `Examples/Transition/<平台>/Demo`（Blazor 用它的 `Demo/Demo`）、设置 `VELOXDEV_AT=1` 与快速档节奏、按路径跑 `VeloxDev.AT.csproj`。双击 `run-autotest.cmd` 等价于从仓库根执行同一脚本，结束时停住窗口便于读结论。

验收套件**不在 `VeloxDev.slnx`**：根 `dotnet test` 不会拉起真实窗口。直接 `dotnet test` 该工程而不设 `VELOXDEV_AT=1` 时所有用例都会跳过并退 0 —— 一键脚本替你设好这个门。

---

## 跑有头视图管线基准

`VeloxDev.Adapters.Benchmarks` 另外带一个**有头**（真实窗口）的视图管线基准，不走 BenchmarkDotNet。宿主无关的选项、合成场景、报告和运行循环在 `VeloxDev.Adapters.Benchmarks.Headed`；各平台只实现 `IHeadedBenchmarkHost`。

```bash
# 1) 用优化过的 Debug 产物构建（理由与序列化基准相同：非 Debug 会去拿未发布的生成器包）
dotnet build Src/Test/Benchmarks/VeloxDev.Adapters.Benchmarks/VeloxDev.Adapters.Benchmarks.csproj \
             -c Debug -p:Optimize=true -t:Rebuild

# 2) 直接运行产物；不要用 dotnet run，它会再构建一次
./Src/Test/Benchmarks/VeloxDev.Adapters.Benchmarks/bin/Debug/net10.0-windows/VeloxDev.Adapters.Benchmarks.exe \
  headed --adapter avalonia --policy adaptive \
         --nodes 2000 --links 3000 --iterations 5 --steps 20

# 同一进程还宿主 WPF / WinForms / Jalium：换 --adapter 即可
#   --adapter wpf | winforms | jalium
```

WinForms 的连线现在和 MAUI 一样不池化：`WorkflowLinkLayer` 在画布的 Paint 里一趟画完并发布曲线。基准里 `Active / Created` 只数节点视图；`Visible` 仍是树的可见项（含连线）。full-visibility 因此只创建节点窗口，不再创建每线一个 HWND。

WinUI 与 MAUI 是独立可执行宿主（各自真实窗口，输出同一份 JSON）：

```bash
# WinUI 3（unpackaged；宿主运行时用 Windows App SDK 1.8，适配器自身仍编译在 1.6）
dotnet build Src/Test/Benchmarks/VeloxDev.Adapters.Benchmarks.WinUI/VeloxDev.Adapters.Benchmarks.WinUI.csproj \
             -c Debug -p:BuildProjectReferences=false
./Src/Test/Benchmarks/VeloxDev.Adapters.Benchmarks.WinUI/bin/Debug/net10.0-windows10.0.19041.0/win-x64/VeloxDev.Adapters.Benchmarks.WinUI.exe \
  --adapter winui --policy adaptive --nodes 2000 --links 3000 --iterations 5 --steps 20

# MAUI（Windows 目标）
dotnet build Src/Test/Benchmarks/VeloxDev.Adapters.Benchmarks.MAUI/VeloxDev.Adapters.Benchmarks.MAUI.csproj \
             -c Debug -p:BuildProjectReferences=false
./Src/Test/Benchmarks/VeloxDev.Adapters.Benchmarks.MAUI/bin/Debug/net10.0-windows10.0.19041.0/win-x64/VeloxDev.Adapters.Benchmarks.MAUI.exe \
  --adapter maui --policy adaptive --nodes 2000 --links 3000 --iterations 5 --steps 20
```

> 上面 WinUI / MAUI 的 `-p:BuildProjectReferences=false` 只是本轮为了避免多个基准进程同时重写共享 `obj/`；首次构建或要拿代表性数字时去掉它，并按前面的纪律加 `-p:Optimize=true`。Razor 基准必须在构建依赖图时加 `-p:Optimize=true`；BDN 的 optimizer validator 现在保持开启，依赖未优化会直接拒绝运行。

Razor 的 `ViewPool` 是 renderer-native 的，没有可驱动的 `ViewSourcePipeline`；它单独跑 `HtmlRenderer` 的**无头渲染基准**，不能与上面的窗口数字直接横比：

```bash
dotnet build Src/Test/Benchmarks/VeloxDev.Adapters.Benchmarks.Razor/VeloxDev.Adapters.Benchmarks.Razor.csproj \
             -c Debug -p:BuildProjectReferences=false
./Src/Test/Benchmarks/VeloxDev.Adapters.Benchmarks.Razor/bin/Debug/net10.0/VeloxDev.Adapters.Benchmarks.Razor.exe --probe
./Src/Test/Benchmarks/VeloxDev.Adapters.Benchmarks.Razor/bin/Debug/net10.0/VeloxDev.Adapters.Benchmarks.Razor.exe
```

默认 `--policy adaptive`：初始 64/8ms，按每批实测吞吐收敛，pending 大于 1024/256 时目标预算 16/12ms，否则 8ms。需要固定参数对照时用 `--policy fixed --batch 1024 --budget 16`。

它测的是**可见集 → 视图池 → 真窗口布局**这一段：`Initial materialization`（空视口开始）、`Scroll step`（对角扫视口，逐步计时）、`Scroll sweep final`、`Full visibility`。每步之后等池的 pending 批次排空，再等一次 `TopLevel.RequestAnimationFrame`，所以数字包含真实的 dispatcher + layout，不是「只调方法」的微基准。

**怎么读数字**：

- 每轮先 warmup（默认 1 轮，不计），再测 `--iterations` 轮；表里是**中位数 / P95**，不是均值。
- `Created / Reused / Destroyed` 是池的累计计数；`Active / Pending / Recycled / Visible` 是当前状态。
- 分配量是最稳的一维；耗时是同机同会话的前后对比，不要当跨机器绝对基准。
- 输出写在 `BenchmarkDotNet.Artifacts/view-pipeline/`（工程目录下，已被 `.gitignore` 的任意深度规则覆盖），同时有 `.md` 与原始 `.json`。

**适配器范围**：`avalonia` / `wpf` / `winforms` / `jalium` / `winui` / `maui` 都测真实窗口里的新 Core 管线；`razor` 测的是框架 renderer 构造 `ViewPool` 渲染树（无头），不测窗口布局。**一次只跑一家**，别并行驱动多个 UI 进程。

**纪律**（与 `agent-ui-probe.ps1` 同源）：构建失败不许跑；跑前确认没有残留 benchmark/旧 demo 进程；不要在基准运行期间跑 `dotnet test`（它会把 Core 重新构回未优化版本）；报告里带机器/运行时/参数。

## 跑序列化基准

```bash
# 1) 以「Debug 配置 + 优化过的代码」构建 —— 两个条件都不能少，理由见下
#    -t:Rebuild 是必需的：增量构建会认为 VeloxDev.Core 已是最新而跳过它，
#    于是产物里留下上一次 dotnet test 构建的未优化 DLL，BenchmarkDotNet 会直接拒绝运行。
#    症状很安静：它只在 BenchmarkRun-*.log 里写两行 Validating benchmarks，
#    报告照样生成、但「结果」那一节是「本次运行没有产出可用的数据。」—— 不是崩，是空。
dotnet build Src/Test/Benchmarks/VeloxDev.Serialization.Benchmarks/VeloxDev.Serialization.Benchmarks.csproj \
             -c Debug -p:Optimize=true -t:Rebuild

# 2) 直接运行产物（不要用 dotnet run，它会再构建一次）
#    跑完之前不要再跑 dotnet test —— 它会把 Core 重新构回未优化版本。
#    不带参数 = 四档当量全跑 + 出一份 Markdown 报告
./Src/Test/Benchmarks/VeloxDev.Serialization.Benchmarks/bin/Debug/net10.0/VeloxDev.Serialization.Benchmarks.exe
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
`VeloxDev.Core.Generator` 包（`Src/Core/VeloxDev.Core/VeloxDev.Core.csproj`），而 11.0.0 从未发布
（`10.0.0` 与更早的也没有；nuget.org 上最新仍是 `9.0.153`）。即使手工把它喂给还原，包里的分析器也不产出任何东西，Core 会以
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
./Src/Test/Benchmarks/VeloxDev.Serialization.Benchmarks/bin/Debug/net10.0/VeloxDev.Serialization.Benchmarks.exe \
             --filter "*ComparisonBenchmarks*"
```

`ComparisonBenchmarks` 让**同一个对象图**过三家，且三家都开着引用保留（`ReferenceHandler.Preserve` /
`PreserveReferencesHandling.Objects`，Newtonsoft 另加 `TypeNameHandling.Auto`）—— 归档对每个对象都写
`$id`、对多态成员写 `$type`，不对齐这两项就是拿不同的活来比。`GlobalSetup` 会打印三份文档的字符数。

两处 **实测** 的 STJ 默认设置失败（不是推测）：写不了含 NaN/±Infinity 的图（要
`AllowNamedFloatingPointLiterals`），以及读不了主构造器类（构造器形参名与提升出来的属性名不同）。
