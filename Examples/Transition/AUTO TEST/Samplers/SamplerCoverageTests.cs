using System.Reflection;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.SamplerTest;

/// <summary>
/// Enforces that the registry is complete, which is the only part of "every sampler is covered" that a test can
/// actually guarantee.
/// </summary>
/// <remarks>
/// The registry cannot check itself: a sampler missing from it simply never runs, and the suite stays green while
/// the gap grows. So the set is compared against reflection over the product assemblies — a sampler added to any of
/// them without a table entry fails here.
/// </remarks>
[TestClass]
public class SamplerCoverageTests
{
    /// <summary>
    /// 产品适配器程序集。少一个就说明引用被摘掉了，而那正是这个套件最容易安静变瞎的方式：
    /// 程序集不加载 → 反射看不见它的采样器 → 覆盖校验在"少验一批"的情况下显示绿色。
    /// </summary>
    private static readonly string[] ExpectedAdapterAssemblies =
    [
        "VeloxDev.Core",
        "VeloxDev.WPF",
        "VeloxDev.WinForms",
        "VeloxDev.Avalonia",
        "VeloxDev.Razor",
        "VeloxDev.Jalium",
        "VeloxDev.WinUI",
        "VeloxDev.MAUI",
    ];

    /// <summary>The assemblies whose samplers are in scope.</summary>
    private static IEnumerable<Assembly> ProductAssemblies()
        => AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(assembly => assembly.GetName().Name is { } name
                               && name.StartsWith("VeloxDev.", StringComparison.Ordinal)
                               && name is not ("VeloxDev.SamplerTest" or "VeloxDev.AT"))
            .Distinct();

    private static HashSet<Type> ShippedSamplers() =>
        [.. ProductAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(ISampler).IsAssignableFrom(type))];

    [TestMethod]
    public void EveryAdapterAssembly_IsLoadedSoItsSamplersAreCounted()
    {
        // 读一次两张表：各适配器的采样器类型（前者）与 WinUI/MAUI 的程序集限定名（后者）就是让程序集加载的东西。
        _ = SamplerRegistry.Entries;
        _ = UnreachableSamplers.All;

        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name).ToHashSet();
        var missing = ExpectedAdapterAssemblies.Where(name => !loaded.Contains(name)).OrderBy(name => name).ToList();

        CollectionAssert.AreEqual(Array.Empty<string>(), missing.ToArray(),
            $"这些适配器程序集没有加载，它们发布的采样器这一次全都数不到、也就没被验证：" +
            $"{string.Join(", ", missing)}。多半是 VeloxDev.SamplerTest.csproj 里的引用被摘掉了。");
    }

    [TestMethod]
    public void EveryShippedSampler_IsAccountedFor()
    {
        var registered = SamplerRegistry.Entries.Select(entry => entry.SamplerType).ToHashSet();
        var unreachable = UnreachableSamplers.All.Select(entry => entry.SamplerType).ToHashSet();
        var shipped = ShippedSamplers();

        var missing = shipped
            .Except(registered)
            .Except(unreachable)
            .Select(type => type.FullName)
            .OrderBy(name => name)
            .ToList();

        CollectionAssert.AreEqual(Array.Empty<string>(), missing.ToArray(),
            $"这些采样器既不在注册表里、也不在 UnreachableSamplers 名单里，等于从未被验证：" +
            $"{string.Join(", ", missing)}");
    }

    [TestMethod]
    public void EveryUnreachableSampler_NeedsAValueThatCannotBeBuiltHere()
    {
        // 名单上那句话必须是可证伪的：这里真的去造那个端点值，造得出来就说明理由已经不成立，
        // 条目该搬回注册表接受闭式解校验，而不是继续挂着一个过期的理由。
        foreach (var entry in UnreachableSamplers.All)
        {
            try
            {
                _ = entry.RepresentativeValue();
                Assert.Fail(
                    $"{entry.SamplerType.FullName} 需要的端点值在纯数据进程里构造成功了：{entry.Reason} " +
                    $"这条理由不再成立，应该把它搬进 SamplerRegistry 并写上闭式解。");
            }
            catch (AssertFailedException)
            {
                throw;
            }
            catch
            {
                // 造不出来才对 —— 正是这条清单存在的意义。
            }
        }
    }

    [TestMethod]
    public void EveryRegistryEntry_IsASamplerThatShips()
    {
        // 反向检查：注册表里也不能留已经删掉的类型（重命名或删除后没同步表）。
        var shipped = ShippedSamplers();
        var stale = SamplerRegistry.Entries
            .Select(entry => entry.SamplerType)
            .Where(type => !shipped.Contains(type))
            .Select(type => type.FullName)
            .OrderBy(name => name)
            .ToList();

        CollectionAssert.AreEqual(Array.Empty<string>(), stale.ToArray(),
            $"这些注册表条目指向的采样器已经不在产品里了：{string.Join(", ", stale)}");
    }

    [TestMethod]
    public void EveryRegistryEntry_IsListedOnce()
    {
        var duplicates = SamplerRegistry.Entries
            .GroupBy(entry => entry.SamplerType)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.FullName)
            .OrderBy(name => name)
            .ToList();

        CollectionAssert.AreEqual(Array.Empty<string>(), duplicates.ToArray(),
            $"同一个采样器登记了多次：{string.Join(", ", duplicates)}");
    }

    [TestMethod]
    public void NoSampler_IsBothRegisteredAndDeclaredUnreachable()
    {
        // 两张表是两个互斥的断言："验过闭式解" 与 "根本起不来"。同一个采样器同时出现在两边，
        // 说明其中一条已经不成立了 —— 而两份记录会互相掩护，谁也看不出来。
        var registered = SamplerRegistry.Entries.Select(entry => entry.SamplerType).ToHashSet();
        var overlap = UnreachableSamplers.All
            .Select(entry => entry.SamplerType)
            .Where(registered.Contains)
            .Select(type => type.FullName)
            .OrderBy(name => name)
            .ToList();

        CollectionAssert.AreEqual(Array.Empty<string>(), overlap.ToArray(),
            $"这些采样器同时在注册表和 UnreachableSamplers 名单里：{string.Join(", ", overlap)}");
    }

    [TestMethod]
    public void EveryUnreachableSampler_IsCoveredByTheAcceptanceTables()
    {
        // 这个套件验不了的六个，由 VeloxDev.AT 在真跑起来的 app 里验。两边没有编译期联系（AT 刻意不引用
        // 任何适配器，这个工程也不该反过来引用 AT 的测试工程），所以按名字对一次：AT 侧删掉一条，这里就红。
        // 少了这道对账，"75 个采样器全覆盖"会随着一次删除悄悄变成假话，而两个套件都还是绿的。
        var directory = ConformanceDirectory();
        var tables = string.Join(
            Environment.NewLine,
            directory.EnumerateFiles("*.cs").Select(file => File.ReadAllText(file.FullName)));

        var missing = UnreachableSamplers.All
            .Where(entry => !tables.Contains($"\"{entry.SamplerType.Name}\"", StringComparison.Ordinal))
            .Select(entry => entry.SamplerType.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        CollectionAssert.AreEqual(Array.Empty<string>(), missing.ToArray(),
            $"这些采样器这个套件验不了，而 VeloxDev.AT 的表里也没有它们，等于两边都没验：{string.Join(", ", missing)}");
    }

    /// <summary>
    /// Locates <c>AUTO TEST/Conformance</c> by walking up for the solution file, the same way <c>AtConfig</c> finds
    /// the demos — so the two projects survive being moved together.
    /// </summary>
    private static DirectoryInfo ConformanceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VeloxDev.slnx")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName
            ?? throw new InvalidOperationException($"VeloxDev.slnx was not found above {AppContext.BaseDirectory}.");

        return new DirectoryInfo(Path.Combine(root, "Examples", "Transition", "AUTO TEST", "Conformance"));
    }

    [TestMethod]
    public void EveryUnreachableEntry_NamesASamplerThatShips()
    {
        // 名单是按程序集限定名解析的，指向一个已经不存在的类型会直接解析失败 —— 但指向一个存在却不再是
        // ISampler 的类型不会，所以这里再对一次。
        var shipped = ShippedSamplers();
        var notShipped = UnreachableSamplers.All
            .Select(entry => entry.SamplerType)
            .Where(type => !shipped.Contains(type))
            .Select(type => type.FullName)
            .OrderBy(name => name)
            .ToList();

        CollectionAssert.AreEqual(Array.Empty<string>(), notShipped.ToArray(),
            $"这些 UnreachableSamplers 条目指向的类型已经不随产品发布了：{string.Join(", ", notShipped)}");
    }
}
