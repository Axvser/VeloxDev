using System.Drawing;
using System.Reflection;
using AdapterBrush = Easel.Brush;
using DrawingColor = System.Drawing.Color;
using AdapterSolidBrush = Easel.SolidColorBrush;
using AdapterThickness = Easel.Controls.Thickness;
using AdapterTransform3D = Easel.Transform3D;
using AdapterMatrix = System.Drawing.Drawing2D.Matrix;
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

    private sealed class MatrixTarget
    {
        public AdapterMatrix Value { get; set; } = new();
    }

    private static readonly AdapterMatrix MatrixStart = new(1f, 0f, 0f, 1f, 10f, 20f);
    private static readonly AdapterMatrix MatrixEnd = new(2f, 0.5f, -0.5f, 3f, 40f, 80f);

    // 六个仿射分量各自 start + (end - start) * t；不做任何钳制，时间越界时继续外推。
    private static AdapterMatrix MatrixExpected(double t)
    {
        var start = MatrixStart.Elements;
        var end = MatrixEnd.Elements;
        return new AdapterMatrix(
            Lerp(start[0], end[0], t),
            Lerp(start[1], end[1], t),
            Lerp(start[2], end[2], t),
            Lerp(start[3], end[3], t),
            Lerp(start[4], end[4], t),
            Lerp(start[5], end[5], t));
    }

    private static bool SameMatrix(object? expected, object? actual)
    {
        if (expected is not AdapterMatrix left || actual is not AdapterMatrix right)
        {
            return false;
        }

        var a = left.Elements;
        var b = right.Elements;
        for (var index = 0; index < 6; index++)
        {
            if (a[index] != b[index])
            {
                return false;
            }
        }

        return true;
    }

    private sealed class ClipTarget
    {
        public RectangleF? Value { get; set; }
    }

    private static readonly RectangleF ClipStart = new(0f, 0f, 10f, 20f);
    private static readonly RectangleF ClipEnd = new(10f, 20f, 30f, 40f);

    // 四条边各自线性外推；null 端点按空矩形处理，写回的始终是一个值。
    private static RectangleF? ClipExpected(double t)
        => new(
            Lerp(ClipStart.X, ClipEnd.X, t),
            Lerp(ClipStart.Y, ClipEnd.Y, t),
            Lerp(ClipStart.Width, ClipEnd.Width, t),
            Lerp(ClipStart.Height, ClipEnd.Height, t));

    private static float Lerp(float start, float end, double t)
        => (float)(start + ((end - start) * t));

    private static SamplerEntry MatrixEntry()
    {
        var samplerType = typeof(PaddingSampler).Assembly.GetType(
            "VeloxDev.Adapters.NativeSamplers.MatrixSampler",
            throwOnError: true)!;
        var sampler = (ISampler)Activator.CreateInstance(samplerType)!;
        var entry = EntryFactory.Create<ISampler, MatrixTarget, AdapterMatrix>(
            sampler,
            "WinForms",
            SamplerRule.Extrapolate,
            MatrixStart,
            MatrixEnd,
            target => target.Value,
            MatrixExpected,
            SameMatrix);

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

    private static SamplerEntry ClipEntry()
    {
        var samplerType = typeof(PaddingSampler).Assembly.GetType(
            "VeloxDev.Adapters.NativeSamplers.NullableRectangleFSampler",
            throwOnError: true)!;
        var sampler = (ISampler)Activator.CreateInstance(samplerType)!;

        // CS8714：条目按可空类型取键（ValueType 来自属性声明类型），而 TValue 的 notnull 约束只是为了让
        // 表达式树能取非空值 —— 这个属性的值本身可以为 null，这里显式说明。
#pragma warning disable CS8714
        var entry = EntryFactory.Create<ISampler, ClipTarget, RectangleF?>(
            sampler,
            "WinForms",
            SamplerRule.Extrapolate,
            ClipStart,
            ClipEnd,
            target => target.Value,
            ClipExpected);
#pragma warning restore CS8714

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

    private sealed class Transform3DTarget
    {
        public AdapterTransform3D Value { get; set; } = AdapterTransform3D.Identity;
    }

    private static readonly AdapterTransform3D TransformStart = new Easel.Rotate3DTransform
    {
        AngleX = 10d,
        AngleY = 20d,
        CenterX = 5d,
        CenterY = 7d,
        Depth = 600d,
    };

    private static readonly AdapterTransform3D TransformEnd = new Easel.Rotate3DTransform
    {
        AngleX = 70d,
        AngleY = -40d,
        CenterX = 25d,
        CenterY = 17d,
        Depth = 300d,
    };

    // 同类变换逐分量线性外推：角度、中心、景深各自 start + (end - start) * t，没有任何钳制。
    // 越过 [0,1] 时角度继续走，矩阵因此仍是正交旋转 —— 这正是它属于 Extrapolate 的原因。
    private static AdapterTransform3D TransformExpected(double t)
    {
        var start = (Easel.Rotate3DTransform)TransformStart;
        var end = (Easel.Rotate3DTransform)TransformEnd;
        return new Easel.Rotate3DTransform
        {
            AngleX = start.AngleX + ((end.AngleX - start.AngleX) * t),
            AngleY = start.AngleY + ((end.AngleY - start.AngleY) * t),
            CenterX = start.CenterX + ((end.CenterX - start.CenterX) * t),
            CenterY = start.CenterY + ((end.CenterY - start.CenterY) * t),
            Depth = start.Depth + ((end.Depth - start.Depth) * t),
        };
    }

    private static bool SameTransform(object? expected, object? actual)
    {
        if (expected is not AdapterTransform3D left || actual is not AdapterTransform3D right)
        {
            return false;
        }

        var a = left.Value;
        var b = right.Value;
        return a.M11 == b.M11 && a.M12 == b.M12 && a.M13 == b.M13 && a.M14 == b.M14
               && a.M21 == b.M21 && a.M22 == b.M22 && a.M23 == b.M23 && a.M24 == b.M24
               && a.M31 == b.M31 && a.M32 == b.M32 && a.M33 == b.M33 && a.M34 == b.M34
               && a.M41 == b.M41 && a.M42 == b.M42 && a.M43 == b.M43 && a.M44 == b.M44
               && left.PerspectiveDepth == right.PerspectiveDepth;
    }

    // 与 BrushSampler 同理：Jalium 也有一个同名同全名的 Transform3DSampler，直接写类型会 CS0433。
    private static SamplerEntry Transform3DEntry()
    {
        var samplerType = typeof(PaddingSampler).Assembly.GetType(
            "VeloxDev.Adapters.NativeSamplers.Transform3DSampler",
            throwOnError: true)!;
        var sampler = (ISampler)Activator.CreateInstance(samplerType)!;
        var entry = EntryFactory.Create<ISampler, Transform3DTarget, AdapterTransform3D>(
            sampler,
            "WinForms",
            SamplerRule.Extrapolate,
            TransformStart,
            TransformEnd,
            target => target.Value,
            TransformExpected,
            SameTransform);

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
        MatrixEntry(),
        ClipEntry(),
        Transform3DEntry(),
    ];
}
