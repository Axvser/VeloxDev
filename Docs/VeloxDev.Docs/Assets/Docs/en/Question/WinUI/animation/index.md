# WinUI Animation

> **Adapt**

To ensure thread safety, when using VeloxDev.WinUI, you must have UIThreadInspector obtain the window.

```csharp
public MainWindow()
{
    InitializeComponent();

    // VeloxDev.WinUI mandates execution of this line of code
    UIThreadInspector.SetWindow(this);
}
```