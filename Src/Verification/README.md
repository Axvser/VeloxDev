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
./Src/Verification/VeloxDev.Serialization.Benchmarks/bin/Debug/net10.0/VeloxDev.Serialization.Benchmarks.exe \
             --filter "*SerializationBenchmarks*"
```

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
