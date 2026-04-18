# Serialization 测试

针对 `VeloxDev.MVVM.Serialization` 命名空间（扩展库）的单元测试 —— 基于 Newtonsoft.Json 对 `INotifyPropertyChanged` 对象的 JSON 序列化/反序列化。

| 测试文件 | 测试目标 | 关键场景 |
|---|---|---|
| `ComponentModelExTests.cs` | `ComponentModelEx` | 同步/异步序列化 + 反序列化往返、流式变体、无效 JSON 的 `TryDeserialize`、null/空参数守卫 |
