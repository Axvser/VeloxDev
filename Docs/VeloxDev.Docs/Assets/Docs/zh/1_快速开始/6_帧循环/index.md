# 帧循环

使用 `[MonoBehaviour]` 为任意类添加帧循环生命周期 — **控制台项目也能用**（无需 GUI）。

---

## 第一步 — 安装

```shell
dotnet add package VeloxDev.Core
```

## 第二步 — 粘贴到 `Program.cs`

```csharp
using VeloxDev.MonoBehaviour;
using VeloxDev.TimeLine;

[MonoBehaviour(channel: "demo", fps: 30)]
public partial class MyBehaviour
{
    private int _frameCount;

    private void Awake() => Console.WriteLine("Awake — 初始化");
    private void Start() => Console.WriteLine("Start — 第一帧");

    private void Update(FrameEventArgs e)
    {
        _frameCount++;
        if (_frameCount % 30 == 0)
            Console.WriteLine($"帧 {_frameCount} | Δ={e.DeltaTime.TotalMilliseconds:F0}ms | 总计={e.TotalTime.TotalSeconds:F1}s");
    }

    private void LateUpdate(FrameEventArgs e) { }
    private void FixedUpdate(FrameEventArgs e) { }
}

var behaviour = new MyBehaviour();
MonoBehaviourManager.RegisterBehaviour(behaviour, "demo");
MonoBehaviourManager.Start("demo");

Console.WriteLine("帧循环已启动。按 Enter 停止。");
Console.ReadLine();

await MonoBehaviourManager.StopAsync("demo");
Console.WriteLine("已停止。");
```

## 第三步 — 运行

```shell
dotnet run
```

输出示例：
```
Awake — 初始化
Start — 第一帧
帧 30 | Δ=33ms | 总计=1.0s
帧 60 | Δ=33ms | 总计=2.0s
```

## 通道控制

```csharp
MonoBehaviourManager.Pause("demo");           // 暂停
MonoBehaviourManager.Resume("demo");           // 恢复
MonoBehaviourManager.SetTargetFPS(60, "demo");  // 改帧率
MonoBehaviourManager.SetTimeScale(2.0f, "demo"); // 2 倍速
```

## FrameEventArgs

| 属性 | 类型 | 描述 |
|------|------|------|
| `DeltaTime` | `TimeSpan` | 距上一帧的时间差 |
| `TotalTime` | `TimeSpan` | 通道总运行时间 |
| `CurrentFPS` | `int` | 实际帧率 |
| `TargetFPS` | `int` | 目标帧率 |
