# VeloxDev TransitionSystem

C# 动画框架（支持 MAUI / WinUI / Avalonia / WPF）  

**核心理念：一切皆状态。动画 = 状态快照 → 插值过渡**

---

## ✍️ 1. 代码怎么写？

### 基础用法
```csharp
// 创建动画：移动 + 缩放 + 颜色变化
var anim = Transition<Rectangle>.Create()
    .Property(r => r.TranslationX, 200)     // 目标属性 & 值
    .Property(r => r.Scale, 1.5)
    .Property(r => r.Fill, Colors.Red)
    .Effect(e => e.Duration = TimeSpan.FromSeconds(1));

// 执行
anim.Execute(myRect);
```

### 高级组合
```csharp
Transition<Rectangle>.Create()
    .Await(TimeSpan.FromSeconds(1))         // 延迟1秒
    .Property(r => r.Rotation, 180)
    .Effect(new TransitionEffect { LoopTime = 2, IsAutoReverse = true })
    .AwaitThen(TimeSpan.FromSeconds(0.5))   // 接着等0.5秒
    .Property(r => r.Opacity, 0)
    .Execute(target);
```

### 控制动画
```csharp
// 终止对象上所有动画
Transition.Exit(myElement);

// 重置到初始快照
snapshot.Effect(TransitionEffects.Empty).Execute(myElement);
```

---

## 📚 2. 核心 API 列表

### `Transition<T>.Create()`
- `.Property(Expression<Func<T, object>> property, object targetValue)`  
  指定要动画的属性和目标值
- `.Await(TimeSpan delay)`  
  在当前段前插入延迟
- `.AwaitThen(TimeSpan delay)`  
  在当前段后插入延迟（用于拼接下一段）
- `.Effect(Action<TransitionEffect> config)`  
  配置效果（推荐）
- `.Effect(TransitionEffect effect)`  
  传入完整效果对象

### `TransitionEffect` 属性
| 属性 | 类型 | 说明 |
|------|------|------|
| `Duration` | `TimeSpan` | 动画时长（必设） |
| `FPS` | `int` | 帧率，默认60 |
| `Ease` | `IEaseCalculator` | 缓动函数，如 `Eases.Sine.InOut` |
| `LoopTime` | `int` | 循环次数（0=不循环） |
| `IsAutoReverse` | `bool` | 是否自动反向播放 |

### 缓动函数（`Eases`）
```csharp
Eases.Sine.In / Out / InOut
Eases.Quad.In / Out / InOut
Eases.Cubic.In / Out / InOut
Eases.Circ.InOut
Eases.Elastic.Out
Eases.Bounce.In
// ... 共10种类型，每种含 In/Out/InOut
```

### 工具方法
- `obj.Snapshot()` → 记录当前所有可动画属性
- `obj.Snapshot(x => x.Prop1, x => x.Prop2)` → 只记录指定属性
- `obj.SnapshotExcept(x => x.IsVisible)` → 排除某些属性
- `Transition.Exit(obj, IncludeMutual, IncludeNoMutual)` → 终止动画

---

> 💡 **一行原则**：动画 = 声明“从哪来 → 到哪去 + 怎么去”，框架自动处理插值、线程、平台差异。