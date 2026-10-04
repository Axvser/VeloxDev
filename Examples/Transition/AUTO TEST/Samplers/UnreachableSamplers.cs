namespace VeloxDev.SamplerTest;

// 随产品发布、但**这个纯数据套件**驱动不起来的采样器，以及它需要而这里造不出来的那个端点值。
// 框架对象（画刷 / 投影 / 变换 / 阴影），而那种对象在没有 XAML/MAUI 运行时的进程里激活不了 —— 没有端点，
// 测试立刻失败，条目必须搬回 SamplerRegistry 接受闭式解校验 —— 那时 AT 侧那份就成了重复，
// 直接可造，余下五个只剩 RPC_E_WRONG_THREAD（线程亲和性）。真去 Application.Start 起那条线程
// 会把 MSTest 宿主进程整个带崩 —— 这也正是这六个改由 AT 侧覆盖、而不是在这里硬凑的原因。
internal static class UnreachableSamplers
{
    // 一个驱动不起来的采样器，它需要的端点值，以及为什么造不出来。
    internal sealed record Entry(Type SamplerType, Func<object> RepresentativeValue, string Reason);

    internal static IReadOnlyList<Entry> All { get; } =
    [
        // WinRT 的类激活要在进程里有一个真正的 XAML 运行时：没有它，拿到的是
        // COMException(REGDB_E_CLASSNOTREG)，连一个 SolidColorBrush 都造不出来。
        new(Resolve("VeloxDev.WinUI", "BrushSampler"),
            static () => new Microsoft.UI.Xaml.Media.SolidColorBrush(),
            "端点值必须是 WinRT 的 SolidColorBrush（DependencyObject），无 XAML 运行时时类激活失败。"),
        new(Resolve("VeloxDev.WinUI", "ProjectionSampler"),
            static () => new Microsoft.UI.Xaml.Media.PlaneProjection(),
            "端点值必须是 WinRT 的 PlaneProjection（DependencyObject），同上。"),
        new(Resolve("VeloxDev.WinUI", "TransformSampler"),
            static () => new Microsoft.UI.Xaml.Media.TranslateTransform(),
            "端点值必须是 WinRT 的 TranslateTransform（DependencyObject），同上。"),

        // MAUI 的 BindableObject 走的是另一条路：类型能激活，但静态构造要求平台件已经就位。
        new(Resolve("VeloxDev.MAUI", "BrushSampler"),
            static () => new Microsoft.Maui.Controls.SolidColorBrush(),
            "端点值必须是 MAUI 的 SolidColorBrush（BindableObject），其 Element 静态构造要求平台件已就位。"),
        new(Resolve("VeloxDev.MAUI", "ShadowSampler"),
            static () => new Microsoft.Maui.Controls.Shadow(),
            "端点值必须是 MAUI 的 Shadow（BindableObject），同上。"),
        new(Resolve("VeloxDev.MAUI", "TransformSampler"),
            static () => new Microsoft.Maui.Controls.Shapes.Transform(),
            "端点值必须是 MAUI 的 Transform（BindableObject），同上。"),
    ];

    private static Type Resolve(string adapterAssembly, string samplerName)
        => Type.GetType(
            $"VeloxDev.Adapters.NativeSamplers.{samplerName}, {adapterAssembly}",
            throwOnError: true)!;
}
