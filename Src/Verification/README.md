# Verification

本目录放的是**验证**，不是产品代码：不打包、不进任何 `PackageReference`。

| 东西 | 干什么 |
| --- | --- |
| `VeloxDev.Serialization.Benchmarks/` | 归档序列化引擎的基准（BenchmarkDotNet） |
| `agent-ui-harness.ps1` / `agent-web-harness.ps1` | 用真实输入驱动 demo 并截图 |
| `verify-*-item-templates*.ps1` | 打包/编译模板项，检查镜像漂移 |

---

## 跑序列化基准

```bash
# 1) 以「Debug 配置 + 优化过的代码」构建 —— 两个条件都不能少，理由见下
#    -t:Rebuild 是必需的：增量构建会认为 VeloxDev.Core 已是最新而跳过它，
#    于是产物里留下上一次 dotnet test 构建的未优化 DLL，BenchmarkDotNet 会直接拒绝运行。
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
**报告就是五张表**：环境一张，四个当量各一张。每个当量的表里**一行一个序列化器**，**耗时与存储同表**
（写/读各自的耗时与分配，加文档字符数），**本仓库那行加粗**，括号里是相对本仓库同方向的倍数。表后是
**备注**（这些数字是什么、不能推广到哪里）与一处**自校**（同一个量在两类里各量一次，差值就是这一档的噪声）。
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
