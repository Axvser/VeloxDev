# TimeLine 测试

针对 `VeloxDev.TimeLine` 命名空间的单元测试 —— 时间线事件参数与 MonoBehaviour 属性。

| 测试文件 | 测试目标 | 关键场景 |
|---|---|---|
| `TimeLineEventArgsTests.cs` | `TransitionEventArgs`、`FrameEventArgs`、`ThreadSafeFrameEventArgs` | 默认值、Handled 读写、线程安全并发访问 |
| `MonoBehaviourAttributeTests.cs` | `MonoBehaviourAttribute` | AttributeUsage 约束（仅限 Class、不可重复、不可继承）、实例化 |
