# WinUI 动画

> **适配**

为了确保线程安全，您在使用 VeloxDev.WinUI 时，必须让 UIThreadInspector 获取窗口

```csharp
public MainWindow()
{
    InitializeComponent();

    // VeloxDev.WinUI 强制要求执行这行代码
    UIThreadInspector.SetWindow(this);
}
```