using VeloxDev.Adapters.NativeSamplers;

namespace VeloxDev.SamplerTest;

/// <summary>The samplers the WinForms adapter ships, with the closed form each one follows.</summary>
internal static class WinFormsEntries
{
    /// <summary>A target exposing one Padding property, so the entry has something real to write through.</summary>
    private sealed class Target
    {
        public System.Windows.Forms.Padding Pad { get; set; }
    }

    /// <summary>
    /// PaddingSampler 的闭式解：四条边各自独立地随缓动时间线性外推，不经过任何钳制。
    /// </summary>
    /// <remarks>
    /// 源码（<c>PaddingSampler.InsertFrame</c>）对每条边算的都是
    /// <c>p1 + (int)(t * (p2 - p1))</c>，左右上下四个算式之间没有任何耦合：没有共用进度、没有在 0 处停住、
    /// 也没有别的上下限。因此 t 越过 [0,1] 时四条边照走不误 —— 收缩的一侧在 t &gt; 1 处会算出负的 Padding，
    /// 而 <c>Padding</c> 只是四个 int，接受负值，所以这里既没有饱和也没有切换，判定为 Extrapolate。
    /// </remarks>
    private static System.Windows.Forms.Padding Expected(double t)
        => new(
            20 + (int)(t * 80d),
            20 + (int)(t * -15d),
            60 + (int)(t * -60d),
            60 + (int)(t * -60d));

    internal static IReadOnlyList<SamplerEntry> All { get; } =
    [
        EntryFactory.Create<PaddingSampler, Target, System.Windows.Forms.Padding>(
            new PaddingSampler(),
            "WinForms",
            SamplerRule.Extrapolate,
            new System.Windows.Forms.Padding(20, 20, 60, 60),
            new System.Windows.Forms.Padding(100, 5, 0, 0),
            target => target.Pad,
            Expected),
    ];
}
