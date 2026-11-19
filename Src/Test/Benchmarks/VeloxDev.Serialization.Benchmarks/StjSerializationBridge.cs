using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace VeloxDev.Serialization.Benchmarks;

/// <summary>
/// What <c>System.Text.Json</c> has to be told before it can read the archive's own graph — and nothing more.
/// </summary>
/// <remarks>
/// <para>
/// Both pieces are <b>configuration</b>: a creation policy and a derived-type table. Neither is serialization code
/// written on System.Text.Json's behalf. That is the line this file stays on, because the moment a hand-written
/// converter is needed the number stops being "System.Text.Json" and becomes "System.Text.Json plus us", and the
/// table would be claiming a comparison it cannot support. The counterpart is Newtonsoft.Json, which needs only
/// settings — <c>PreserveReferencesHandling.Objects</c> and <c>TypeNameHandling.Auto</c>.
/// </para>
/// <para>
/// Measured on the corpus as it stands, these two are enough. They are not enough in general: a graph whose
/// interface-keyed map — the tree's <c>LinksMap</c> — actually held entries would need a converter, because
/// System.Text.Json dictionary keys must be property names. The corpus leaves that map empty, so nothing here has
/// to spell one. If the corpus ever populates it, this file stops being configuration and the row stops being
/// comparable; see the module memory for why that matters.
/// </para>
/// </remarks>
internal static class StjSerializationBridge
{
    /// <summary>The discriminator both directions carry — the spelling the archive itself uses.</summary>
    private const string TypeDiscriminator = "$type";

    /// <summary>
    /// Adds both modifiers to a resolver, whichever kind it is — reflection or source-generated metadata.
    /// </summary>
    /// <remarks>
    /// Applied to both so the two System.Text.Json rows stay the same document: the source-generated row exists to
    /// isolate <i>where the metadata comes from</i>, and that comparison dies the moment the two write different
    /// documents.
    /// </remarks>
    /// <param name="resolver">The resolver the options would otherwise use.</param>
    /// <returns>The resolver with both modifiers attached.</returns>
    internal static IJsonTypeInfoResolver WithBridge(this IJsonTypeInfoResolver resolver)
        => resolver.WithAddedModifier(WithoutConstructors).WithAddedModifier(WithDerivedTypes);

    // 这些 ViewModel 是主构造器类，形参名与提升出来的属性名不同 —— `Offset(double left, double top)` 暴露
    // 的是 `Horizontal`/`Vertical`。System.Text.Json 只会在「公开无参构造」与「参数最多的构造」之间挑，
    // 挑到后者就要求每个形参都绑得上属性，绑不上当场抛。
    //
    // **有公开无参构造的类型一个都不动**（CreateObject 已非 null）—— 所以这一支只落在 Offset 这类叶子上，
    // 代价与归档生成器 `Create()` 里的 `new T()` 同阶，而不是每个对象都多一笔。
    private static void WithoutConstructors(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object) return;
        if (info.CreateObject is not null) return;
        if (info.Type.IsAbstract || info.Type.IsInterface) return;

        info.CreateObject = () => RuntimeHelpers.GetUninitializedObject(info.Type);
    }

    // 接口 / 抽象成员：System.Text.Json 默认拒绝读，因为它的文档里没有判别符。归档对多态成员写 `$type`，
    // Newtonsoft 用 `TypeNameHandling.Auto` —— 给 System.Text.Json 的对应物就是这张派生表。
    //
    // **必须同时配在写这一侧**：判别符是写进去的，不配写侧读侧就没有东西可判。配上之后 System.Text.Json
    // 写出的文档才与另两家同类（三家都写类型名），也才读得回来 —— 在那之前它写的是一份「写得出去、读不
    // 回来」的文档，拿它比大小与耗时对归档是不公平的。
    private static void WithDerivedTypes(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object) return;
        if (!info.Type.IsAbstract && !info.Type.IsInterface) return;

        var polymorphic = new JsonPolymorphismOptions
        {
            TypeDiscriminatorPropertyName = TypeDiscriminator,
            IgnoreUnrecognizedTypeDiscriminators = true,
        };

        foreach (var implementation in ImplementationsOf(info.Type))
        {
            polymorphic.DerivedTypes.Add(new JsonDerivedType(implementation, DiscriminatorOf(implementation)));
        }

        if (polymorphic.DerivedTypes.Count > 0) info.PolymorphismOptions = polymorphic;
    }

    private static string DiscriminatorOf(Type type) => type.FullName ?? type.Name;

    // 实现者从已加载的程序集里找 —— 语料的类型在 VeloxDev.Core 与基准程序集自己里。
    // 按名字排序，否则「派生表的顺序」会变成同一档两次运行之间的一个变量。
    //
    // **泛型一律排除**：STJ 拒绝泛型派生类型（实测它对着 `VeloxCommand`1[T1…T9,TResult]` 抛
    // `not a supported derived type … must not be generic`），而 `IVeloxCommand` 的实现者里就有一堆。
    private static IEnumerable<Type> ImplementationsOf(Type contract)
        => AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .SelectMany(TypesOf)
            .Where(type => !type.IsAbstract
                           && !type.IsInterface
                           && !type.IsGenericType
                           && contract.IsAssignableFrom(type))
            .Distinct()
            .OrderBy(static type => type.FullName, StringComparer.Ordinal);

    private static IEnumerable<Type> TypesOf(System.Reflection.Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (System.Reflection.ReflectionTypeLoadException partial)
        {
            // 一个加载不全的程序集不该让整次测量作废 —— 能拿到的那些就够找实现了。
            return partial.Types.Where(static type => type is not null).Select(static type => type!);
        }
    }
}
