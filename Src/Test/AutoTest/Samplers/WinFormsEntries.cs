using System.Drawing;
using System.Reflection;
using AdapterBrush = VeloxDev.WinForms.Xaml.Brush;
using DrawingColor = System.Drawing.Color;
using AdapterSolidBrush = VeloxDev.WinForms.Xaml.SolidColorBrush;
using AdapterThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;
using VeloxDev.Adapters.NativeSamplers;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.SamplerTest;

/// <summary>The samplers the WinForms adapter ships, with the closed form each one follows.</summary>
internal static class WinFormsEntries
{
    /// <summary>A target exposing one Padding property, so the entry has something real to write through.</summary>
    private sealed class Target
    {
        public System.Windows.Forms.Padding Pad { get; set; }
    }

    // PaddingSampler 的闭式解：四条边各自独立地随缓动时间线性外推，不经过任何钳制。
    // p1 + (int)(t * (p2 - p1))，左右上下四个算式之间没有任何耦合：没有共用进度、没有在 0 处停住、
    // 也没有别的上下限。因此 t 越过 [0,1] 时四条边照走不误 —— 收缩的一侧在 t > 1 处会算出负的 Padding，
    // 而 Padding 只是四个 int，接受负值，所以这里既没有饱和也没有切换，判定为 Extrapolate。
    private static System.Windows.Forms.Padding Expected(double t)
        => new(
            20 + (int)(t * 80d),
            20 + (int)(t * -15d),
            60 + (int)(t * -60d),
            60 + (int)(t * -60d));

    private sealed class BrushTarget
    {
        public AdapterBrush Paint { get; set; } = new AdapterSolidBrush(DrawingColor.Transparent);
    }

    private static readonly AdapterBrush BrushStart = new AdapterSolidBrush(DrawingColor.FromArgb(0x20, 0x10, 0x30, 0x50));
    private static readonly AdapterBrush BrushEnd = new AdapterSolidBrush(DrawingColor.FromArgb(0xE0, 0x90, 0x20, 0x70));

    // 每个通道独立四舍五入后夹到 0..255；t 越过 [0,1] 时只饱和，不继续外推。
    private static AdapterBrush BrushExpected(double t)
    {
        var start = BrushStart.ApproximateColor;
        var end = BrushEnd.ApproximateColor;
        return new AdapterSolidBrush(DrawingColor.FromArgb(
            Channel(start.A, end.A, t),
            Channel(start.R, end.R, t),
            Channel(start.G, end.G, t),
            Channel(start.B, end.B, t)));
    }

    private static int Channel(byte start, byte end, double t)
        => Math.Clamp((int)Math.Round(start + (end - start) * t), 0, 255);



    private sealed class ThicknessTarget
    {
        public AdapterThickness Value { get; set; }
    }

    private static readonly AdapterThickness ThicknessStart = new(1f, 2f, 3f, 4f);
    private static readonly AdapterThickness ThicknessEnd = new(11f, 22f, 33f, 44f);

    private static AdapterThickness ThicknessExpected(double t)
        => new(
            ThicknessStart.Left + (float)(t * (ThicknessEnd.Left - ThicknessStart.Left)),
            ThicknessStart.Top + (float)(t * (ThicknessEnd.Top - ThicknessStart.Top)),
            ThicknessStart.Right + (float)(t * (ThicknessEnd.Right - ThicknessStart.Right)),
            ThicknessStart.Bottom + (float)(t * (ThicknessEnd.Bottom - ThicknessStart.Bottom)));

    private static SamplerEntry ThicknessEntry()
    {
        var samplerType = typeof(PaddingSampler).Assembly.GetType(
            "VeloxDev.Adapters.NativeSamplers.ThicknessSampler",
            throwOnError: true)!;
        var sampler = (ISampler)Activator.CreateInstance(samplerType)!;
        var entry = EntryFactory.Create<ISampler, ThicknessTarget, AdapterThickness>(
            sampler,
            "WinForms",
            SamplerRule.Extrapolate,
            ThicknessStart,
            ThicknessEnd,
            target => target.Value,
            ThicknessExpected);

        return new SamplerEntry
        {
            SamplerType = samplerType,
            ValueType = entry.ValueType,
            Adapter = entry.Adapter,
            Rule = entry.Rule,
            Write = entry.Write,
            Expected = entry.Expected,
            Equivalent = entry.Equivalent,
        };
    }

    // 适配器之间共享 VeloxDev.Adapters.NativeSamplers.BrushSampler 这个全名；直接写类型会 CS0433，
    // 因此像 AvaloniaEntries 一样从 WinForms 程序集反射取类型，再把真实类型覆盖回条目。
    private static SamplerEntry BrushEntry()
    {
        var samplerType = typeof(PaddingSampler).Assembly.GetType(
            "VeloxDev.Adapters.NativeSamplers.BrushSampler",
            throwOnError: true)!;
        var sampler = (ISampler)Activator.CreateInstance(samplerType)!;
        var entry = EntryFactory.Create<ISampler, BrushTarget, AdapterBrush>(
            sampler,
            "WinForms",
            SamplerRule.Saturate,
            BrushStart,
            BrushEnd,
            target => target.Paint,
            BrushExpected,
            static (expected, actual) => Equals(
                (expected as AdapterBrush)?.ApproximateColor,
                (actual as AdapterBrush)?.ApproximateColor));

        return new SamplerEntry
        {
            SamplerType = samplerType,
            ValueType = entry.ValueType,
            Adapter = entry.Adapter,
            Rule = entry.Rule,
            Write = entry.Write,
            Expected = entry.Expected,
            Equivalent = entry.Equivalent,
        };
    }

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
        BrushEntry(),
        ThicknessEntry(),
    ];
}
