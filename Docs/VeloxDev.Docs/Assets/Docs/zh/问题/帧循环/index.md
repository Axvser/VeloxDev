# 帧循环

由于不是所有运行时都支持 Thread，帧循环会走两个路径。保守起见，低于 .NET5 的版本默认使用异步而非 Thread，更高的版本已内置了平台检测，其检测逻辑遵顼下表

> **await Task**

| 平台 |
| --- |
| Browser |
| iOS |

> **Thread**

| 平台 |
| --- |
| Windows |
| Linux |
| Android |

> **手动配置**

MonoBehaviourManager 存在一个名为 UseAsyncLoop 的静态属性，true 表示启用异步来获取最大兼容性，您可在执行此类提供的 Start（）函数之前修改 UseAsyncLoop

```csharp
        public static bool UseAsyncLoop { get; set; } =
#if NET5_0_OR_GREATER
        OperatingSystem.IsBrowser() || OperatingSystem.IsIOS();
#else
        true;
#endif
```