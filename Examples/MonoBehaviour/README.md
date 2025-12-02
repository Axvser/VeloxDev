# VeloxDev TimeLine System

C# 生命周期与帧驱动

---

## ✍️ 1. 代码怎么写？

### 步骤 1：标记组件
用 `[MonoBehaviour]` 标记类，并实现生命周期方法：
```csharp
[MonoBehaviour]
public partial class PhysicsComponent
{
    public PhysicsComponent() => InitializeMonoBehaviour(); // 必须调用！

    partial void Update(FrameEventArgs e) 
    {
        // 每帧执行（可变帧率）
    }

    partial void FixedUpdate(FrameEventArgs e) 
    {
        // 固定频率执行（默认每16ms）
    }
}
```

> 💡 所有 `partial void` 方法自动由代码生成器注入调用逻辑。

---

### 步骤 2：注册与控制
```csharp
// 创建实例（自动注册到 TimeLine）
var physics = new PhysicsComponent();

// 启动系统（全局）
MonoBehaviourManager.Start();

// 控制时间
MonoBehaviourManager.SetTargetFPS(120);     // 设置目标帧率
MonoBehaviourManager.SetTimeScale(0.5f);    // 时间减慢一半
MonoBehaviourManager.Pause();               // 暂停所有 Update/FixedUpdate
MonoBehaviourManager.Resume();
MonoBehaviourManager.Stop();                // 停止并清理
```

### 步骤 3：响应事件（可选）
```csharp
MonoBehaviourManager.OnSystemStarted += (s, e) => { /* 启动回调 */ };
MonoBehaviourManager.OnSystemPaused += (s, e) => { /* 暂停回调 */ };
```

---

## 📚 2. 核心 API 列表

### `MonoBehaviourManager`（全局控制）
| 方法/属性 | 说明 |
|----------|------|
| `Start()` | 启动 Update + FixedUpdate 线程 |
| `Stop()` | 停止系统并重置统计 |
| `Pause()` / `Resume()` | 暂停/恢复逻辑更新 |
| `SetTargetFPS(int fps)` | 设置目标帧率（1~1000） |
| `SetFixedUpdateInterval(int ms)` | 设置 FixedUpdate 间隔（毫秒） |
| `SetTimeScale(float scale)` | 时间缩放（0=暂停，1=正常，2=2倍速） |
| `IsRunning` / `IsPaused` | 系统状态 |
| `CurrentFPS` / `TotalFrames` | 性能统计 |

### `FrameEventArgs`（传递给 Update/FixedUpdate）
| 属性 | 类型 | 说明 |
|------|------|------|
| `DeltaTime` | `int` | 上一帧耗时（毫秒，已应用 TimeScale） |
| `TotalTime` | `int` | 系统累计运行时间（毫秒） |
| `CurrentFPS` | `int` | 当前帧率 |
| `TargetFPS` | `int` | 目标帧率 |
| `Handled` | `bool` | 设为 `true` 可中断后续行为执行 |

### 生命周期方法（在 `[MonoBehaviour]` 类中实现）
| 方法 | 调用时机 |
|------|--------|
| `Awake()` | 组件注册后立即调用（主线程） |
| `Start()` | 第一次 Update 前调用（主线程） |
| `Update(FrameEventArgs e)` | 每渲染帧调用（可变频率） |
| `LateUpdate(FrameEventArgs e)` | Update 后调用 |
| `FixedUpdate(FrameEventArgs e)` | 固定频率调用（独立物理线程） |

### 特性与类型
| 名称 | 作用 |
|------|------|
| `[MonoBehaviour]` | 标记类为可注册组件 |
| `InitializeMonoBehaviour()` | 构造函数中必须调用，用于注册 |
| `ThreadSafeFrameEventArgs` | 线程安全版 FrameEventArgs（内部使用） |

---

> 💡 **一行原则**：标记 `[MonoBehaviour]` → 实现 `Update`/`FixedUpdate` → 调用 `InitializeMonoBehaviour()` → 用 `MonoBehaviourManager` 控制全局生命周期。