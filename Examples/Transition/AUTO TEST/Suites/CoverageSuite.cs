using System.Reflection;
using VeloxDev.AT.Conformance;
using VeloxDev.AT.Drivers;
using VeloxDev.AT.LoadMode;

namespace VeloxDev.AT.Suites;

/// <summary>
/// The guards that keep the suite honest about what it actually covers.
/// </summary>
/// <remarks>
/// Not belt-and-braces. A case was once lost by an edit that used the case above it as the anchor and did not put it
/// back, and nothing went red — that platform simply stopped being verified while the suite stayed green. The catalogs
/// are the assertion; these check that every entry in them is reachable.
/// <para>
/// Reflected over the **assembly** rather than over one suite class, because the cases now live one class per platform.
/// </para>
/// </remarks>
[TestClass]
public class CoverageSuite
{
    /// <summary>Every platform AT drives must have cases tagged for it.</summary>
    [TestMethod]
    public void EveryDrivenPlatform_HasARunningCase()
    {
        var covered = CoveredPlatforms();

        var missing = DemoCatalog.Platforms
            .Where(platform => !covered.Contains(platform))
            .OrderBy(platform => platform, StringComparer.Ordinal)
            .ToList();

        CollectionAssert.AreEqual(Array.Empty<string>(), missing.ToArray(),
            $"这些平台在 DemoCatalog 里注册了，却没有任何 AT.<平台> 分类的用例在跑它：{string.Join(", ", missing)}");
    }

    /// <summary>
    /// Every platform with a table must be one AT drives — the mirror of the guard above, and the more likely mistake:
    /// adding a table for a platform whose driver was never registered leaves it silently unrun.
    /// </summary>
    [TestMethod]
    public void EveryTable_IsForADrivenPlatform()
    {
        var driven = DemoCatalog.Platforms;

        var orphans = LoadModeCatalog.Platforms
            .Concat(ConformanceCatalog.Platforms)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(platform => !driven.Contains(platform, StringComparer.OrdinalIgnoreCase))
            .OrderBy(platform => platform, StringComparer.Ordinal)
            .ToList();

        CollectionAssert.AreEqual(Array.Empty<string>(), orphans.ToArray(),
            $"这些平台有表，却不在 DemoCatalog 里，没有任何用例会跑它们：{string.Join(", ", orphans)}");
    }

    /// <summary>
    /// Every suite that drives a demo must open and close it itself.
    /// </summary>
    /// <remarks>
    /// This is the load-bearing condition for the one-window-at-a-time shape: a class that opens a demo without a
    /// class-cleanup leaves it on the desktop for the rest of the run, and one that relies on the assembly teardown
    /// holds a window open across every later platform. Both look fine in a report and are obvious to anyone watching,
    /// so it is worth failing over rather than noticing.
    /// </remarks>
    [TestMethod]
    public void EverySuiteThatDrivesADemo_OpensAndClosesIt()
    {
        var offenders = typeof(CoverageSuite).Assembly
            .GetTypes()
            .Where(type => type.GetCustomAttribute<TestClassAttribute>() is not null)
            .Where(DrivesADemo)
            .Where(type => !HasStaticMethod<ClassInitializeAttribute>(type) || !HasStaticMethod<ClassCleanupAttribute>(type))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        CollectionAssert.AreEqual(Array.Empty<string>(), offenders.ToArray(),
            $"这些测试类驱动 demo，却没有自己开或自己关：{string.Join(", ", offenders)}");
    }

    private static HashSet<string> CoveredPlatforms() =>
        typeof(CoverageSuite).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods())
            .SelectMany(method => method.GetCustomAttributes<TestCategoryAttribute>())
            .SelectMany(attribute => attribute.TestCategories)
            .Where(category => category.StartsWith("AT.", StringComparison.Ordinal))
            .Select(category => category[3..])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool DrivesADemo(Type type) =>
        type.GetMethods()
            .SelectMany(method => method.GetCustomAttributes<TestCategoryAttribute>())
            .SelectMany(attribute => attribute.TestCategories)
            .Any(category => category.StartsWith("AT.", StringComparison.Ordinal));

    private static bool HasStaticMethod<TAttribute>(Type type) where TAttribute : Attribute =>
        type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(method => method.GetCustomAttribute<TAttribute>() is not null);
}
