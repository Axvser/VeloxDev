using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
// MAUI 也有一个 Effect（Microsoft.Maui.Controls.Effect），裸名会 CS0104 —— 与 WpfEntries.cs 同一套别名写法。
using Effect = System.Windows.Media.Effects.Effect;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.SamplerTest;

/// <summary>
/// Puts the two halves of a registration together: which key a type is registered under, and which type the sampler
/// behind that key actually unboxes.
/// </summary>
/// <remarks>
/// 这一格此前没有任何测试看着。覆盖校验对的是采样器的<b>类型集合</b>，不看它注册在哪个键上；闭式解校验的端点
/// 由表自己给，于是表和采样器可以一起错、还错得一致。历史上 MAUI 的 <c>RectFSampler</c> 正是这样：键是
/// <c>Microsoft.Maui.Graphics.RectF</c>，体里解的却是 <c>System.Drawing.RectangleF</c> —— 编译通过、
/// 两个套件全绿，直到第一帧在采样器里抛 <c>InvalidCastException</c>、整条 run 被取消。
/// <para>
/// 断言的依据是<b>这个进程里真实的注册表</b>，不是表里重抄一遍的键：查之前先把七家（加 Core）的注册入口都
/// 跑起来，再问注册表"<c>ValueType</c> 这条类型解析到谁"。所以它能看见的正是适配器里那一行注册写成了什么。
/// </para>
/// </remarks>
[TestClass]
public class SamplerKeyTests
{
    /// <summary>
    /// 前提：每条条目归属的适配器，都有一份能在这个进程里跑起来的注册表。
    /// </summary>
    /// <remarks>
    /// 下面两条断言都建立在"注册表真的被填过"之上。填不上时 <c>TryGetInterpolator</c> 一律返回 false，
    /// 它们会以"没有键"的形式报红 —— 但那是在报症状。这一条先把前提本身钉死，并钉在正确的方向上：
    /// 漏掉一家，那家的采样器在下面两条里是被<b>跳过</b>的，而不是被验过的。
    /// </remarks>
    [TestMethod]
    public void EveryAdapterRegistrationTable_IsReachableFromHere()
    {
        foreach (var assembly in SamplerRegistry.Entries.Select(entry => entry.SamplerType.Assembly).Distinct())
        {
            // Core 没有这个类：它的注册写在抽象基类的静态构造里，任何一次 TryGetInterpolator 都会先触发它。
            if (assembly == typeof(InterpolatorCore).Assembly)
            {
                continue;
            }

            var name = assembly.GetName().Name;
            var adapter = assembly.GetType("VeloxDev.TransitionSystem.Interpolator");

            Assert.IsNotNull(adapter,
                $"{name} 里有采样器，却找不到 VeloxDev.TransitionSystem.Interpolator 这个注册入口 —— "
                + $"它的采样器是按什么键注册的？这个套件查不到。入口改名或搬走后要同步这里。");

            try
            {
                RuntimeHelpers.RunClassConstructor(adapter.TypeHandle);
            }
            catch (Exception exception)
            {
                Assert.Fail(
                    $"{name} 的注册入口 {adapter.FullName} 的静态构造在这个进程里跑不起来："
                    + $"{exception.GetType().Name}：{exception.Message}。注册表没被填上，"
                    + $"本文件其余两条断言对它的采样器一律看不见（而不是验不过）。");
            }
        }
    }

    /// <summary>
    /// 每条条目的声明类型，在真实注册表里必须解析到这条条目写的那条采样器。
    /// </summary>
    /// <remarks>
    /// 声明类型就是注册键：<c>InterpolatorCore.Prepare</c> 拿 <c>PropertyType</c> 去查表。所以这条断言问的是
    /// "用户的属性声明成这个类型，实际会跑谁" —— 表里写着谁不算数。
    /// </remarks>
    [TestMethod]
    public void EveryEntry_ValueTypeResolvesToTheSamplerItNames()
    {
        ForceEveryRegistrationTable();
        var failures = new List<string>();

        foreach (var entry in SamplerRegistry.Entries)
        {
            var resolved = InterpolatorCore.TryGetInterpolator(entry.ValueType, out var sampler);

            if (entry.UnregisteredReason is { } reason)
            {
                // 反过来证伪：既然声明"注册表里没有这条键"，就真去查一次。查出键来，这条理由已经过期，
                // 条目该像别的一样按闭式解验，而不是继续挂着一句不再成立的话。
                if (resolved)
                {
                    failures.Add(
                        $"{entry}：条目声明 {entry.ValueType.FullName} 不由注册表提供（{reason}），"
                        + $"但注册表里它有键、指向 {sampler!.GetType().FullName}。");
                }

                continue;
            }

            if (!resolved)
            {
                failures.Add(
                    $"{entry}：注册表里没有 {entry.ValueType.FullName} 的键 —— 声明成这个类型的属性一帧都动不了，"
                    + $"而条目写的采样器是 {entry.SamplerType.FullName}。");
            }
            else if (sampler!.GetType() != entry.SamplerType)
            {
                failures.Add(
                    $"{entry}：{entry.ValueType.FullName} 这条键指向 {sampler.GetType().FullName}，"
                    + $"不是条目写的 {entry.SamplerType.FullName}。两者同名不算数 —— 键是 Type，含程序集。");
            }
        }

        CollectionAssert.AreEqual(Array.Empty<string>(), failures.ToArray(),
            $"{failures.Count} 条条目的声明类型没有落在它自己写的采样器上："
            + $"{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    /// <summary>
    /// 注册表为这条键解析出的采样器，必须接得住"这个键类型的值" —— 逐条收集、一次报出。
    /// </summary>
    /// <remarks>
    /// 与上一条的分工：上一条比 <see cref="Type"/>，问"注册在哪条键上"；这一条把后果跑一遍，问"接得住吗"。
    /// 键对而体里解错时上一条照过，那时就是这里抛 <c>InvalidCastException</c>。但这不是新的检测面 ——
    /// 闭式解校验会撞上同一处，差别只在于它一抛就整体中断、只报第一条，而这里逐条收齐。
    /// 端点取条目自己的（就是闭式解校验用的那一对），采样器取注册表解析出的那个实例。
    /// </remarks>
    [TestMethod]
    public void EveryEntry_SamplerTheRegistryResolves_AcceptsAValueOfThatKey()
    {
        ForceEveryRegistrationTable();
        var failures = new List<string>();

        foreach (var entry in SamplerRegistry.Entries)
        {
            if (entry.UnregisteredReason is not null)
            {
                continue;   // 没有键，也就没有"经注册表走到它"这条路；上一条已经核过这句话。
            }

            if (!InterpolatorCore.TryGetInterpolator(entry.ValueType, out var sampler) || sampler is null)
            {
                continue;   // 没有键 —— 上一条测试负责报这一条，这里不重复。
            }

            foreach (var t in SamplerEntry.Times)
            {
                try
                {
                    _ = entry.Write(sampler, t);
                }
                catch (Exception exception)
                {
                    failures.Add(
                        $"{entry}：把 {entry.ValueType.FullName} 喂进注册表解析出的 {sampler.GetType().FullName} 时，"
                        + $"t={t} 抛了 {exception.GetType().Name}：{exception.Message}");
                    break;   // 一个条目的全部时间点会抛同一处，报一次就够。
                }
            }
        }

        CollectionAssert.AreEqual(Array.Empty<string>(), failures.ToArray(),
            $"{failures.Count} 条条目的采样器接不住自己那条键的类型："
            + $"{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    /// <summary>
    /// 注册到<b>基类型</b>上的采样器，会被交到手的类型不止它名字里那一种。
    /// </summary>
    /// <remarks>
    /// WPF 注册的是 <c>typeof(Effect)</c>（WPF 自己的 <c>UIElement.Effect</c> DP 就是按 <c>Effect</c> 声明的），
    /// 于是 <c>BlurEffect</c> 也会走到这条叫 <c>DropShadowEffectSampler</c> 的采样器上。这条钉的是它在那时
    /// <b>不冒充</b>：既不许把不认识的效果静默画成自己最熟的那一种，也不许抛。
    /// <para>
    /// 比的是实例（<c>AreSame</c>）而不是字段：这一对没有可插的公共面（<c>BlurEffect</c> 没有 Color/Direction/
    /// ShadowDepth），采样器该把调用方给的那个实例原样交回。造一个新对象、只把字段抄成端点的值，在这个断言下同样是
    /// 冒充 —— 那正是这条采样器旧兜底分支在做的事。
    /// </para>
    /// <para>
    /// 放在这个文件里，是因为这件事是"键选成了基类型"的直接后果；它进不了闭式解那张表 ——
    /// <c>SamplerCoverageTests.EveryRegistryEntry_IsListedOnce</c> 要求一个采样器只有一条条目。
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ASamplerRegisteredForABaseType_HandsBackTheFamilyItWasGiven()
    {
        ForceEveryRegistrationTable();

        Assert.IsTrue(InterpolatorCore.TryGetInterpolator(typeof(Effect), out var sampler) && sampler is not null,
            "WPF 没有给 Effect 注册采样器 —— 声明成 Effect 的属性一帧都动不了，而 WPF 自己的 UIElement.Effect DP "
            + "正是这么声明的。");

        Expression<Func<EffectSlot, Effect>> selector = x => x.Value;
        Assert.IsTrue(TransitionProperty.TryCreate(selector, out var property) && property is not null,
            "写一帧要用一条真实路径，而这条 lambda 没能成为路径。");

        var target = new EffectSlot();
        var start = new System.Windows.Media.Effects.BlurEffect { Radius = 4 };
        var end = new System.Windows.Media.Effects.BlurEffect { Radius = 40 };

        foreach (var t in SamplerEntry.Times)
        {
            object? working = null;
            sampler.InsertFrame(target, property, ref working, start, end, null, t);

            Assert.AreSame(t >= 0.5d ? end : start, property.GetValue(target),
                $"{sampler.GetType().Name} 在 t={t} 交出的不是端点实例本身：这一对 BlurEffect 没有可插的公共面，"
                + $"该按阈值切换并把收到的那个效果原样交出 —— 交出新对象就是把 Effect 家族里不认识的那一半冒充了。");
        }
    }

    /// <summary>一条声明成 <see cref="Effect"/> 的属性，只要够写一帧。</summary>
    private sealed class EffectSlot
    {
        public Effect Value { get; set; } = null!;
    }

    /// <summary>
    /// 把每条条目归属适配器的那份注册表都填上 —— 这是上面两条断言的前提。Core 那份没有独立入口：它的注册
    /// 写在抽象基类的静态构造里，第一次 <c>TryGetInterpolator</c> 就会触发。
    /// </summary>
    /// <remarks>
    /// 这里不缓存"已经跑过哪几家"：静态构造再跑一次是无操作，而一旦缓存，某条测试单独跑时就会漏填，
    /// 漏填的表现正是本文件要防的那种"安静变瞎"。
    /// </remarks>
    private static void ForceEveryRegistrationTable()
    {
        foreach (var assembly in SamplerRegistry.Entries.Select(entry => entry.SamplerType.Assembly).Distinct())
        {
            if (assembly.GetType("VeloxDev.TransitionSystem.Interpolator") is { } adapter)
            {
                RuntimeHelpers.RunClassConstructor(adapter.TypeHandle);
            }
        }
    }
}
