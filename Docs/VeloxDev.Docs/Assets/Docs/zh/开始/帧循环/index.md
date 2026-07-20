# 帧循环

帧循环行为常见于物理模拟、状态监听等场景，现在，你可以用一个特性来构建

> **构建**

您可以基于 channel 参数创建多个分组，可以使用 fps 参数指定刷新率

```csharp
[MonoBehaviour(channel:"game", fps:60)]
private partial class Component
{
    public Component() => InitializeMonoBehaviour();


    partial void Awake() { }

    partial void Start() { }

    partial void Update(FrameEventArgs e) { }

    partial void LateUpdate(FrameEventArgs e) { }

    partial void FixedUpdate(FrameEventArgs e) { }
}
```

> **控制**

可以基于这些静态函数实现基础控制

```csharp
MonoBehaviourManager.Start("game"); // 启动

MonoBehaviourManager.Pause("game"); // 暂停（不等于停止）

MonoBehaviourManager.Resume("game"); // 继续

await MonoBehaviourManager.StopAsync("game"); // 停止
```

可以随时修改时间倍率与刷新率

```csharp
MonoBehaviourManager.SetTimeScale(0.5f, "game"); // 时间倍率

MonoBehaviourManager.SetTargetFPS(30, "game"); // 刷新率
```

> **状态查询**

```csharp
bool isRun = MonoBehaviourManager.IsRunning("game")；

bool isPaused = MonoBehaviourManager.IsPaused("game")；
```

> **FrameEventArgs**

| 属性 | 类型 | 描述 |
| --- | --- | --- |
| DeltaTime | TimeSpan | 距离上一帧的时间 |
| TotalTime | TimeSpan | 总时间 |
| TargetFPS | int | 目标刷新率 |
| CurrentFPS | int | 实时刷新率 |