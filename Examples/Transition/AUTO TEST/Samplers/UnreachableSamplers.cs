namespace VeloxDev.SamplerTest;

/// <summary>
/// 随产品发布、但**这个纯数据套件**驱动不起来的采样器，以及它需要而这里造不出来的那个端点值。
/// </summary>
/// <remarks>
/// 它们不是没被验证 —— 只是不在这里被验证。这六个的闭式解由 <c>VeloxDev.AT</c> 在真跑起来的 app 里验：
/// WinUI 的三条在 <c>Conformance/WinUiConformance.cs</c>，MAUI 的三条在 <c>Conformance/MauiConformance.cs</c>，
/// 与这个套件用同一组缓动时间、同一组端点。两个套件合起来，75 个采样器全部覆盖。
/// <para>
/// 卡住的是端点值，不是采样器本身：这六个都是普通的托管类，<c>Activator</c> 造得出来，但它们要写的是一个
/// 框架对象（画刷 / 投影 / 变换 / 阴影），而那种对象在没有 XAML/MAUI 运行时的进程里激活不了 —— 没有端点，
/// 一帧都跑不了，也就无从在这里验它的闭式解。
/// </para>
/// <para>
/// 名单里的类型按<b>程序集限定名</b>解析，这一点是承重的：WinUI 与 MAUI 两个适配器只在这里出现，
/// 靠它才把两个程序集拉进进程，覆盖校验才数得到它们那 22 个采样器。少了这一层，两个程序集永远不会加载，
/// 覆盖校验会在"少验 22 个采样器"的情况下显示绿色。
/// </para>
/// <para>
/// <b>这是一句可证伪的话，不是垃圾桶</b>：<see cref="SamplerCoverageTests"/> 会真的去构造
/// <see cref="RepresentativeValue"/>，一旦某个框架允许在无 UI 运行时的进程里造出它，那条理由就不再成立、
/// 测试立刻失败，条目必须搬回 <see cref="SamplerRegistry"/> 接受闭式解校验 —— 那时 AT 侧那份就成了重复，
/// 也该一并删掉。
/// </para>
/// <para>
/// 顺带记下探到过的那层底（2026-09-12）：六个失败是<b>同一个根因</b> <c>REGDB_E_CLASSNOTREG</c>，
/// 而 <c>Bootstrap.TryInitialize(0x00010007)</c> 返回 True 就能抬掉它；抬掉之后 MAUI 的 <c>Shapes.Transform</c>
/// 直接可造，余下五个只剩 <c>RPC_E_WRONG_THREAD</c>（线程亲和性）。真去 <c>Application.Start</c> 起那条线程
/// 会把 MSTest 宿主进程整个带崩 —— 这也正是这六个改由 AT 侧覆盖、而不是在这里硬凑的原因。
/// </para>
/// </remarks>
internal static class UnreachableSamplers
{
    /// <summary>一个驱动不起来的采样器，它需要的端点值，以及为什么造不出来。</summary>
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
