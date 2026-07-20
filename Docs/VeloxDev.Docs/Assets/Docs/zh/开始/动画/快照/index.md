# 快照

动画基于在两个状态之间插值，快照机制是其实现之一，VeloxDev 将它公开了出来，因此您也可以使用该机制创建动画，或者，用于状态保存与还原 （ 仅在动画场景下使用 ）

> **拍摄快照**

```csharp
// 记录所有可动画属性，内部会递归搜索所有深层路径
var snapshot0 = Rec0.SnapshotAll();

// 白名单模式
var snapshot1 = Rec0.Snapshot(x => ((TranslateTransform)x.RenderTransform!).X, x => x.Fill);

// 黑名单模式
var snapshot2 = Rec0.SnapshotExcept(x => x.Width, x => x.Height, x => x.Fill, x => x.Opacity);
```

> **使用快照**

```csharp
// 本质仍是动画，可配置效果
snapshot.Effect(TransitionEffects.Empty).Execute(view);
```