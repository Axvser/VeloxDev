using System.Numerics;
using System.Reflection;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;
using VeloxDev.TransitionSystem.NativeSamplers;

namespace VeloxDev.SamplerTest;

/// <summary>The samplers Core ships, with the closed form each one follows.</summary>
internal static class CoreSamplerEntries
{
    /// <summary>A target exposing one property per sampler, so each entry has something real to write through.</summary>
    private sealed class Target
    {
        public double D { get; set; }
        public float F { get; set; }
        public int I { get; set; }
        public long L { get; set; }
        public System.Drawing.Point P { get; set; }
        public System.Drawing.PointF PF { get; set; }
        public System.Drawing.Size S { get; set; }
        public System.Drawing.SizeF SF { get; set; }
        public Vector2 V2 { get; set; }
        public Vector3 V3 { get; set; }
        public Vector4 V4 { get; set; }
        public System.Drawing.Color C { get; set; }
        public System.Drawing.Rectangle R { get; set; }
        public System.Drawing.RectangleF RF { get; set; }
        public Quaternion Q { get; set; }
    }

    /// <summary>
    /// 一个成员各自可动画的复合值类型。两个成员类型不同（<c>double</c> 与 <c>int</c>），正好覆盖
    /// "每个成员走自己的采样器"这半条规则。
    /// </summary>
    private readonly struct SampleablePair : ISampleable
    {
        public SampleablePair(double a, int b)
        {
            A = a;
            B = b;
        }

        public double A { get; }

        public int B { get; }

        public IReadOnlyList<ITransitionProperty> GetAnimatableMembers()
            => TransitionProperty.ReadableMembers<SampleablePair>(pair => pair.A, pair => pair.B);

        public object? CreateFrameValue(IReadOnlyList<object?> memberValues)
            => new SampleablePair((double)memberValues[0]!, (int)memberValues[1]!);
    }

    /// <summary>承载一个复合值类型的属性，让组装出来的整值有一条真实的写入路径。</summary>
    private sealed class StructTarget
    {
        public SampleablePair Pair { get; set; }
    }

    /// <summary>
    /// One entry: write <paramref name="start"/> to a fresh target, run one frame at t, read back what landed.
    /// </summary>
    private static readonly Func<object?, object?, bool> ExactEquivalent = static (expected, actual) => object.Equals(expected, actual);

    private static SamplerEntry Entry<TValue>(
        ISampler sampler, string propertyName, string adapter, SamplerRule rule,
        object start, object end,
        Func<object, double, TValue> expected,
        Func<object?, object?, bool>? equivalent = null)
        where TValue : notnull
    {
        var property = TransitionProperty.FromProperty(typeof(Target).GetProperty(propertyName)!);

        return new SamplerEntry
        {
            SamplerType = sampler.GetType(),
            Adapter = adapter,
            Rule = rule,
            Equivalent = equivalent ?? ExactEquivalent,
            Write = (_, t) =>
            {
                var target = new Target();
                object? working = null;
                sampler.InsertFrame(target, property, ref working, start, end, null, t);
                return property.GetValue(target);
            },
            Expected = t => expected(start, t),
        };
    }

    private static double Lerp(double start, double end, double t) => start + (end - start) * t;

    /// <summary>
    /// 一组通道共用一个进度：谁先出界就停在谁那里 —— 与库里的规则一致，但这里是独立重述的。
    /// </summary>
    /// <param name="maximum">该组的上界：尺寸是 +∞（只有下界 0），颜色是 255。</param>
    private static double SharedProgress(double t, double maximum, params (double Start, double End)[] channels)
    {
        var progress = t;
        foreach (var (start, end) in channels)
        {
            var delta = end - start;
            if (delta == 0d) continue;

            var byMaximum = (maximum - start) / delta;
            var byMinimum = (0d - start) / delta;
            var lower = Math.Min(byMaximum, byMinimum);
            var upper = Math.Max(byMaximum, byMinimum);

            if (upper < progress) progress = upper;
            if (lower > progress) progress = lower;
        }

        return progress;
    }

    internal static IReadOnlyList<SamplerEntry> All { get; } =
    [
        Entry(new DoubleSampler(), nameof(Target.D), "Core", SamplerRule.Extrapolate, 10d, 110d, (s, t) => Lerp(10d, 110d, t)),
        Entry(new FloatSampler(), nameof(Target.F), "Core", SamplerRule.Extrapolate, 10f, 110f, (s, t) => 10f + 100f * (float)t),
        Entry(new IntSampler(), nameof(Target.I), "Core", SamplerRule.Extrapolate, 10, 110, (s, t) => (int)Math.Round(10 + t * 100)),
        Entry(new LongSampler(), nameof(Target.L), "Core", SamplerRule.Extrapolate, 10L, 110L, (s, t) => (long)Math.Round(10 + t * 100)),

        Entry(new PointSampler(), nameof(Target.P), "Core", SamplerRule.Extrapolate,
            new System.Drawing.Point(10, 20), new System.Drawing.Point(110, 220),
            (s, t) => new System.Drawing.Point(10 + (int)Math.Round(100d * t), 20 + (int)Math.Round(200d * t))),
        Entry(new PointFSampler(), nameof(Target.PF), "Core", SamplerRule.Extrapolate,
            new System.Drawing.PointF(10, 20), new System.Drawing.PointF(110, 220),
            (s, t) => new System.Drawing.PointF(10f + 100f * (float)t, 20f + 200f * (float)t)),

        Entry(new Vector2Sampler(), nameof(Target.V2), "Core", SamplerRule.Extrapolate,
            new Vector2(10, 20), new Vector2(110, 220),
            (s, t) => new Vector2(10f + 100f * (float)t, 20f + 200f * (float)t)),
        Entry(new Vector3Sampler(), nameof(Target.V3), "Core", SamplerRule.Extrapolate,
            new Vector3(10, 20, 30), new Vector3(110, 220, 330),
            (s, t) => new Vector3(10f + 100f * (float)t, 20f + 200f * (float)t, 30f + 300f * (float)t)),
        Entry(new Vector4Sampler(), nameof(Target.V4), "Core", SamplerRule.Extrapolate,
            new Vector4(10, 20, 30, 40), new Vector4(110, 220, 330, 440),
            (s, t) => new Vector4(10f + 100f * (float)t, 20f + 200f * (float)t, 30f + 300f * (float)t, 40f + 400f * (float)t)),

        // 尺寸：宽高共用一个进度，在 0 处停止。
        Entry(new SizeSampler(), nameof(Target.S), "Core", SamplerRule.Saturate,
            new System.Drawing.Size(100, 50), new System.Drawing.Size(0, 150),
            (s, t) =>
            {
                var progress = SharedProgress(t, double.PositiveInfinity, (100d, 0d), (50d, 150d));
                return new System.Drawing.Size(
                    100 + (int)Math.Round(-100d * progress),
                    50 + (int)Math.Round(100d * progress));
            }),
        Entry(new SizeFSampler(), nameof(Target.SF), "Core", SamplerRule.Saturate,
            new System.Drawing.SizeF(100, 50), new System.Drawing.SizeF(0, 150),
            (s, t) =>
            {
                var progress = SharedProgress(t, double.PositiveInfinity, (100d, 0d), (50d, 150d));
                return new System.Drawing.SizeF(
                    100f - 100f * (float)progress,
                    50f + 100f * (float)progress);
            }),

        // 矩形：位置外推，宽高在 0 处停止。
        Entry(new RectangleSampler(), nameof(Target.R), "Core", SamplerRule.Saturate,
            new System.Drawing.Rectangle(0, 0, 100, 50), new System.Drawing.Rectangle(100, 200, 0, 150),
            (s, t) =>
            {
                var progress = SharedProgress(t, double.PositiveInfinity, (100d, 0d), (50d, 150d));
                return new System.Drawing.Rectangle(
                    (int)Math.Round(100d * t),
                    (int)Math.Round(200d * t),
                    100 + (int)Math.Round(-100d * progress),
                    50 + (int)Math.Round(100d * progress));
            }),
        Entry(new RectangleFSampler(), nameof(Target.RF), "Core", SamplerRule.Saturate,
            new System.Drawing.RectangleF(0, 0, 100, 50), new System.Drawing.RectangleF(100, 200, 0, 150),
            (s, t) =>
            {
                var progress = SharedProgress(t, double.PositiveInfinity, (100d, 0d), (50d, 150d));
                return new System.Drawing.RectangleF(
                    100f * (float)t,
                    200f * (float)t,
                    100f - 100f * (float)progress,
                    50f + 100f * (float)progress);
            }),

        // 颜色：R/G/B 共用一个进度、在边界停止；Alpha 自成一界。
        Entry(new ColorSampler(), nameof(Target.C), "Core", SamplerRule.Saturate,
            System.Drawing.Color.FromArgb(200, 200, 100, 50), System.Drawing.Color.FromArgb(250, 240, 180, 120),
            (s, t) =>
            {
                var progress = SharedProgress(t, 255d, (200d, 240d), (100d, 180d), (50d, 120d));
                static byte Channel(double value) => value <= 0d ? (byte)0 : value >= 255d ? (byte)255 : (byte)value;
                return System.Drawing.Color.FromArgb(
                    Channel(200d + 50d * t),
                    Channel(200d + 40d * progress),
                    Channel(100d + 80d * progress),
                    Channel(50d + 70d * progress));
            }),

        // 四元数：沿大圆的参数化，模长对任意 t 恒为 1。
        Entry(new QuaternionSampler(), nameof(Target.Q), "Core", SamplerRule.Extrapolate,
            Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1f),
            (s, t) =>
            {
                var q1 = Quaternion.Identity;
                var q2 = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1f);
                var dot = Math.Clamp(Quaternion.Dot(q1, q2), -1f, 1f);
                var angle = MathF.Acos(dot);
                if (angle < 1e-6f) return q2;

                var bySine = 1f / MathF.Sin(angle);
                return new Quaternion(
                    MathF.Sin((1f - (float)t) * angle) * bySine * q1.X + MathF.Sin((float)t * angle) * bySine * q2.X,
                    MathF.Sin((1f - (float)t) * angle) * bySine * q1.Y + MathF.Sin((float)t * angle) * bySine * q2.Y,
                    MathF.Sin((1f - (float)t) * angle) * bySine * q1.Z + MathF.Sin((float)t * angle) * bySine * q2.Z,
                    MathF.Sin((1f - (float)t) * angle) * bySine * q1.W + MathF.Sin((float)t * angle) * bySine * q2.W);
            },
            QuaternionEquivalent),

        // 复合值类型：整值每帧由成员拼回，端点直接短路成 start/end。
        StructEntry(),
    ];

    /// <summary>
    /// 组合型采样器。它不由宿主按名字注册，而是 <c>StructAssembler.Create</c> 每次动画现造、端点已经烘进构造函数，
    /// 所以既没有无参构造，也不能用 <see cref="Activator"/> 起。这里走产品的真实入口（反射进那两个 internal 类型），
    /// 条目验的仍是生产路径，而不是一条测试专用的捷径。
    /// </summary>
    /// <remarks>
    /// 规则判为 <see cref="SamplerRule.Saturate"/>：中间帧确实随缓动时间走（成员各自插值），但 t 一旦越过
    /// [0,1] 就整体停在调用方给的端点上，不跟着成员外推 —— "停在界上"而不是"继续走"。
    /// </remarks>
    private static SamplerEntry StructEntry()
    {
        var start = new SampleablePair(10d, 10);
        var end = new SampleablePair(110d, 110);

        var property = TransitionProperty.FromProperty(typeof(StructTarget).GetProperty(nameof(StructTarget.Pair))!);
        var core = typeof(ISampler).Assembly;

        var assemble = core.GetType("VeloxDev.TransitionSystem.Abstractions.StructAssembler", throwOnError: true)!
            .GetMethod("Create", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException("StructAssembler.Create 不在了，条目需要跟着改。");

        var arguments = new object?[] { property, start, start, end };

        return new SamplerEntry
        {
            SamplerType = core.GetType("VeloxDev.TransitionSystem.Abstractions.StructAssemblerSampler", throwOnError: true)!,
            Adapter = "Core",
            Rule = SamplerRule.Saturate,
            Create = () => (ISampler)(assemble.Invoke(null, arguments)
                ?? throw new InvalidOperationException("StructAssembler.Create 拒绝了这个复合类型：SampleablePair 已经不是一条可组装的形状了。")),
            Write = (sampler, t) =>
            {
                var target = new StructTarget();
                object? working = null;
                sampler.InsertFrame(target, property, ref working, start, end, null, t);
                return property.GetValue(target);
            },
            // 成员各自按自己的采样器走：double 线性，int 四舍五入 —— 与 Core 表里那两条条目同一套闭式解。
            Expected = t => t <= 0d
                ? start
                : t >= 1d
                    ? end
                    : new SampleablePair(Lerp(10d, 110d, t), (int)Math.Round(10 + t * 100)),
            Equivalent = static (expected, actual)
                => expected is SampleablePair e && actual is SampleablePair a && e.A == a.A && e.B == a.B,
        };
    }

    /// <summary>
    /// 四元数的闭式解与库里的算式写法不同（这里显式展开大圆公式），最后一个 bit 可能不同，所以逐分量按容差比，
    /// 而其余条目一律逐位相等。
    /// </summary>
    private static bool QuaternionEquivalent(object? expected, object? actual)
    {
        if (expected is not Quaternion e || actual is not Quaternion a) return false;

        return Math.Abs(e.X - a.X) < 1e-5f
            && Math.Abs(e.Y - a.Y) < 1e-5f
            && Math.Abs(e.Z - a.Z) < 1e-5f
            && Math.Abs(e.W - a.W) < 1e-5f;
    }
}
