# VeloxDev MonoBehaviour 系统文档

## 概述

VeloxDev MonoBehaviour 让你在一些UI框架（ 如 WPF、WinUI ）中实现类似 Unity MonoBehaviour 的组件生命周期管理和帧更新机制。它提供了一个时间线，支持组件的 Awake、Start、Update、LateUpdate 和 FixedUpdate 方法调用

> 注意这和游戏引擎没什么关系 ( 特指性能方面 )，仅仅只是让一些场景的实现变得更优雅，且由于该系统占有一个线程，你的UI操作需要合理调度

---

## 快速开始

### 1. 基本用法
```csharp
[MonoBehaviour]
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (s, e) =>
        {
            InitializeMonoBehaviour();  // 每个实例都需要执行
            MonoBehaviourManager.Start();  // 全局执行一次
        };
    }

    partial void Update(FrameEventArgs e)
    {
        // 每帧更新逻辑
        UpdatePerformanceDisplay(e);
        
        // 业务逻辑处理
        if (e.DeltaTime > 100)
        {
            Debug.WriteLine("帧率过低警告");
        }
    }

    partial void Awake()
    {
        // 组件初始化逻辑
        Debug.WriteLine("组件已唤醒");
    }

    partial void Start()
    {
        // 启动逻辑
        Debug.WriteLine("组件已启动");
    }
}
```

### 2. 其它功能
```csharp
// 设置自定义回调
MonoBehaviourManager.SetPreUpdateCallback(args =>
{
    // 在每帧更新前执行
    if (args.CurrentFPS < 30)
    {
        Debug.WriteLine("性能警告：帧率低于30");
    }
});

// 动态调整帧率
MonoBehaviourManager.SetTargetFPS(144);  // 设置为144Hz

// 暂停/恢复系统
MonoBehaviourManager.Pause();    // 暂停所有更新
// ... 执行一些操作 ...
MonoBehaviourManager.Resume();   // 恢复更新
```

---

## 核心组件

### 1. FrameEventArgs
帧事件参数类，包含帧相关的性能数据：

```csharp
public sealed class FrameEventArgs : TimeLineEventArgs
{
    public int DeltaTime { get; internal set; }      // 上一帧的增量时间（毫秒）
    public int TotalTime { get; internal set; }      // 时间线启动后的总时间（毫秒）
    public int CurrentFPS { get; internal set; }     // 当前帧率
    public int TargetFPS { get; internal set; }       // 目标帧率
}
```

### 2. MonoBehaviourAttribute
标记类启用 TimeLine 系统的生命周期方法：

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MonoBehaviourAttribute : Attribute
{
    // 用于标记需要参与 TimeLine 生命周期的类
}
```

### 3. IMonoBehaviour 接口
定义组件生命周期方法：

```csharp
public interface IMonoBehaviour
{
    void InitializeMonoBehaviour();                    // 初始化组件
    void InvokeAwake();                               // 唤醒方法
    void InvokeStart();                               // 启动方法
    void InvokeUpdate(FrameEventArgs e);              // 每帧更新
    void InvokeLateUpdate(FrameEventArgs e);          // 延迟更新
    void InvokeFixedUpdate(FrameEventArgs e);         // 固定更新
}
```

## 核心管理器：MonoBehaviourManager

### 主要功能
- **线程安全**：使用并发集合处理组件注册和配置变更
- **性能优化**：对象复用、帧率控制、性能统计
- **动态配置**：运行时修改目标帧率和回调函数
- **生命周期管理**：完整的组件生命周期管理

### 公共属性
```csharp
public static int TargetFPS { get; set; }            // 目标帧率（1-1000）
public static int CurrentFPS { get; }                 // 当前帧率
public static long TotalTimeMs { get; }              // 总运行时间
public static long TotalFrames { get; }               // 总帧数
public static bool IsRunning { get; }                 // 运行状态
public static int ActiveBehaviorCount { get; }        // 活跃组件数量
```

### 配置管理 API
```csharp
// 帧率控制
SetTargetFPS(int fps)                 // 设置目标帧率

// 回调函数管理
SetPreUpdateCallback(Action<FrameEventArgs> callback)    // 预更新回调
SetPostUpdateCallback(Action<FrameEventArgs> callback)   // 后更新回调
ClearCallbacks()                      // 清除所有回调

// 运行控制
Pause()                               // 暂停时间线（设置 Handled = true）
Resume()                              // 恢复时间线
```

### 生命周期管理 API
```csharp
Start()                               // 启动时间线系统
Stop()                                // 停止时间线系统
RegisterBehavior(IMonoBehaviour behavior)    // 注册组件
UnregisterBehavior(IMonoBehaviour behavior)  // 注销组件
```