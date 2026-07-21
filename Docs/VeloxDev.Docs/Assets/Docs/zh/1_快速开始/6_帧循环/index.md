# 帧循环

使用 `[MonoBehaviour]` 为任意类注入游戏式的帧循环生命周期 —— **控制台项目也能用**，无需 GUI。

---

## Demo 效果

```
Awake — 初始化
Start — 第一帧
帧 30 | Δ=33ms | 总计=1.0s    ← 每 30 帧打印一次
帧 60 | Δ=33ms | 总计=2.0s
```

按 Enter 停止，控制台友好退出。

## 操作步骤

### 1. 安装

```shell
dotnet add package VeloxDev.Core
```

### 2. 编写

`Program.cs`：

```csharp
using VeloxDev.MonoBehaviour;
using VeloxDev.TimeLine;

[MonoBehaviour(channel: "demo", fps: 30)]
public partial class MyBehaviour
{
    private int _frameCount;

    // 编译器生成 partial 声明，用户实现 partial 方法体
    partial void Awake() => Console.WriteLine("Awake — 初始化");
    partial void Start() => Console.WriteLine("Start — 第一帧");

    partial void Update(FrameEventArgs e)
    {
        _frameCount++;
        if (_frameCount % 30 == 0)
            Console.WriteLine($"帧 {_frameCount} | Δ={e.DeltaTime.TotalMilliseconds:F0}ms | 总计={e.TotalTime.TotalSeconds:F1}s");
    }
}

var behaviour = new MyBehaviour();
behaviour.InitializeMonoBehaviour();  // [MonoBehaviour] 生成器注入：注册到 channel
MonoBehaviourManager.Start("demo");   // 启动通道帧循环

Console.WriteLine("帧循环已启动。按 Enter 停止。");
Console.ReadLine();

await MonoBehaviourManager.StopAsync("demo");
Console.WriteLine("已停止。");
```

### 3. 运行

```shell
dotnet run
```

## 生命周期方法

| 方法 | 调用时机 |
|------|----------|
| `Awake()` | 通道启动时（仅一次） |
| `Start()` | 第一帧 Update 之前（仅一次） |
| `Update(FrameEventArgs e)` | 每帧 |
| `LateUpdate(FrameEventArgs e)` | Update 之后 |
| `FixedUpdate(FrameEventArgs e)` | 固定时间间隔（默认 16ms） |

## 运行时控制

```csharp
MonoBehaviourManager.Pause("demo");            // 暂停
MonoBehaviourManager.Resume("demo");            // 恢复
MonoBehaviourManager.SetTargetFPS(60, "demo");  // 改帧率
MonoBehaviourManager.SetTimeScale(2.0f, "demo"); // 2 倍速
```

## FrameEventArgs

| 属性 | 描述 |
|------|------|
| `DeltaTime` | 距上一帧的时间差 |
| `TotalTime` | 通道总运行时间 |
| `CurrentFPS` | 实际帧率 |
| `TargetFPS` | 目标帧率 |
