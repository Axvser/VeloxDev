# Frame Loop

Since not all runtimes support Thread, the frame loop follows two paths. To be conservative, versions below .NET5 default to using asynchronous instead of Thread; higher versions have built-in platform detection, and the detection logic follows the table below.

> **await Task**

| Platform |
| --- |
| Browser |
| iOS |

> **Thread**

| Platform |
| --- |
| Windows |
| Linux |
| Android |

> **Manual configuration**

MonoBehaviourManager has a static property called UseAsyncLoop. Setting it to true indicates enabling asynchronous behavior for maximum compatibility. You can modify UseAsyncLoop before calling the Start() function provided by this class.

```csharp
public static bool UseAsyncLoop { get; set; } =
#if NET5_0_OR_GREATER
        OperatingSystem.IsBrowser() || OperatingSystem.IsIOS();
#else
        true;
#endif
```