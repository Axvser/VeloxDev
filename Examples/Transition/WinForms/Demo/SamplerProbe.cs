using System;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using VeloxDev.Adapters.NativeSamplers;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace Demo
{
    /// <summary>
    /// 把该适配器发布的每一个采样器各推一帧序列，把结果写成机器可读的载荷。
    /// </summary>
    /// <remarks>
    /// 这是采样式验收在真 app 里的落点：采样器要写的对象需要一个活着的框架运行时，而这里正是有运行时的进程。
    /// <para>
    /// 被写对象是 <see cref="SamplerSubject"/> —— 一个**真正在窗体里的控件**，每条采样器一条同类型的属性。
    /// 采样器直接写在它上面，载荷再从该属性读回，所以断言依据的是界面上那个控件实际持有的值。控件自己按属性
    /// 重绘，于是"画出来"这件事不需要另一套映射代码。
    /// </para>
    /// <para>
    /// 每条采样器由界面上的一个把手点击驱动，一次只跑一条 —— 验收侧因此走的是"点控件 → 触发处理函数 →
    /// 采样器写值 → 读回"这条真实的 UI 交互路径，而不是让 app 在启动时闷头算完一遍。
    /// </para>
    /// <para>
    /// 端点是工厂而不是实例：引用类型的端点若跨帧复用，一个就地改动端点的采样器会把后面的帧一起带偏，
    /// 而「不得改动交给 InsertFrame 的 start/end」正是库对采样器的硬约束 —— 每帧一对全新端点才看得见它。
    /// </para>
    /// </remarks>
    internal static class SamplerProbe
    {
        /// <summary>
        /// 每个采样器被推的缓动时间。与纯数据套件用同一组值，两边的失败信息才好对照；都是二进制可精确表示的
        /// 数，所以时间本身不引入误差。
        /// </summary>
        private static readonly double[] Times = [0d, 0.5d, 1d, 1.5d, -0.5d];

        /// <summary>
        /// 每条采样器的名字 —— 取自采样器类型名，所以把手令牌与载荷键两处说的是同一件事。
        /// </summary>
        internal static class Kinds
        {
            internal const string PaddingSampler = nameof(PaddingSampler);
        }

        /// <summary>一条采样器：把手令牌、它在被写控件上的属性，以及一对端点工厂。</summary>
        private sealed record ProbeSpec(string Name, string Property, Func<ISampler> Create, Func<object> Start, Func<object> End);

        /// <summary>
        /// 顺序即界面顺序，也是套件点击的顺序。加一条采样器只需在这里加一行 —— 把手是从这张表生成的。
        /// 端点与 Samplers/WinFormsEntries.cs 里的一致。
        /// </summary>
        private static readonly ProbeSpec[] Probes =
        [
            new(Kinds.PaddingSampler, nameof(SamplerSubject.Inset), () => new PaddingSampler(),
                () => new Padding(20, 20, 60, 60), () => new Padding(100, 5, 0, 0)),
        ];

        /// <summary>每条采样器的名字，也是它把手的令牌后缀。界面由它生成。</summary>
        internal static IReadOnlyList<string> SamplerNames { get; } = [.. Probes.Select(probe => probe.Name)];

        /// <summary>
        /// 在被写控件上跑一条采样器的五个固定缓动时间，产出载荷文本，形如
        /// <c>v=1;seq=1;n=5;s.PaddingSampler.0=Padding,20,20,60,60;</c>。
        /// </summary>
        /// <param name="subject">这一格的在屏控件 —— 采样器写在它上面，值也从它读回。</param>
        /// <param name="samplerName">要跑的那条采样器，取 <see cref="SamplerNames"/> 里的名字。</param>
        /// <param name="sequence">激活次数，由调用方递增 —— 载荷靠它证明这一次是新的。</param>
        internal static string Run(SamplerSubject subject, string samplerName, long sequence)
        {
            var payload = new StringBuilder($"v=1;seq={sequence};n={Times.Length};");

            for (var index = 0; index < Times.Length; index++)
            {
                payload.Append($"s.{samplerName}.{index}={Describe(Frame(subject, samplerName, Times[index]))};");
            }

            return payload.ToString();
        }

        /// <summary>
        /// 在被写控件上跑一条采样器的一帧，返回**从控件读回**的值。
        /// </summary>
        /// <remarks>
        /// 验收与演示台走的是同一个入口：载荷报的就是这个返回值，屏幕上那个控件持有的也是它 ——
        /// 界面上看到的和断言里读的必然是同一个数，不可能各说各话。
        /// </remarks>
        internal static object? Frame(SamplerSubject subject, string samplerName, double t)
        {
            var probe = Probes.FirstOrDefault(candidate => candidate.Name == samplerName)
                ?? throw new InvalidOperationException($"没有名为 {samplerName} 的采样器；把手与探针表不同步了。");

            var property = TransitionProperty.FromProperty(typeof(SamplerSubject).GetProperty(probe.Property)!);

            // 每帧一对全新端点：端点实例跨帧复用会被采样器原地改动污染。
            object? working = null;
            probe.Create().InsertFrame(subject, property, ref working, probe.Start(), probe.End(), null, t);

            return property.GetValue(subject);
        }

        /// <summary>
        /// 值的规范文本：类型名打头，后面是该类型的分量，顺序固定。
        /// </summary>
        /// <remarks>
        /// 打头带上类型名是有意的 —— 采样器一旦换了产物的类型，或者这里的分量顺序被改动，验收侧会立刻发现，
        /// 而不是把两种情况都读成"数值对不上"。
        /// </remarks>
        private static string Describe(object? value) => value switch
        {
            Padding padding => $"Padding,{Number(padding.Left)},{Number(padding.Top)},{Number(padding.Right)},{Number(padding.Bottom)}",
            _ => throw new InvalidOperationException(
                $"SamplerProbe 不认识产物类型 {value?.GetType().Name ?? "null"}，序列化要跟着采样器一起改。"),
        };

        /// <summary>往返格式 + 不变文化：文本要能无损地还原成同一个 double，且不受区域设置影响。</summary>
        private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
