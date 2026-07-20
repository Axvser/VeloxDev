# 快照过渡

通过 `SnapshotAll()` 捕获当前状态，修改属性后回放过渡。

```csharp
using VeloxDev.TransitionSystem;

// 1. 捕获当前状态
var snapshot = myElement.SnapshotAll();

// 2. 直接修改属性
myElement.Opacity = 0;
myElement.RenderTransform = new TranslateTransform(100, 0);

// 3. 设置效果并执行回放
snapshot.Effect(new TransitionEffect
{
    Duration = TimeSpan.FromMilliseconds(300),
    FPS = 60,
    Ease = Eases.Cubic.Out
});

Transition<FrameworkElement>.Execute(myElement, snapshot);
```

或使用 `Snapshot(x => x.Opacity, x => x.Width)` 选择性捕获。
