# TransitionSystem 测试

针对 `VeloxDev.TransitionSystem` 命名空间的单元测试 —— 缓动函数、插值器、状态管理与过渡属性解析。

| 测试文件 | 测试目标 | 关键场景 |
|---|---|---|
| `EasesTests.cs` | `Eases` / 全部 `IEaseCalculator` 实现 | 21 种缓动函数的边界值（t=0→0, t=1→1）、单调性、InOut 对称性 |
| `NativeInterpolatorsTests.cs` | `DoubleInterpolator`、`FloatInterpolator`、`LongInterpolator` | 线性插值、单帧/零帧、null 起始值、中点精度 |
| `TransitionPropertyTests.cs` | `TransitionProperty` | `FromProperty`、表达式 `TryCreate`、`GetValue`/`SetValue`、相等性、null/空段守卫 |
| `StateCoreTests.cs` | `StateCore` | 表达式/PropertyInfo 方式的值与插值器存储、缺失键查询、覆写、`Clone` 独立性 |
| `InterpolatorCoreTests.cs` | `InterpolatorCore`（静态注册表） | 注册/注销/覆盖/查询原生插值器 |
| `NativeInterpolatorsExtendedTests.cs` | `ColorInterpolator`、`PointInterpolator`、`PointFInterpolator`、`SizeInterpolator`、`SizeFInterpolator`、`RectangleInterpolator`、`RectangleFInterpolator`、`Vector2/3/4Interpolator`、`QuaternionInterpolator` | 线性插值、单帧、null 起始值、边界/端点验证 |
| `TransitionEffectCoreTests.cs` | `TransitionEffectCore` | 默认属性、属性设置往返、7 种生命周期事件触发、Clone 独立性、事件移除 |
