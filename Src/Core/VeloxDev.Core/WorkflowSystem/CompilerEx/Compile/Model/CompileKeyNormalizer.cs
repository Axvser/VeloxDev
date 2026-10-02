using System;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// Keeps a branch key's <b>type</b> across a round trip.
/// <para>
/// A key is held as <see cref="object"/> because an <see cref="ICompileTimeRouter"/> may key its routes by anything,
/// and a number comes back from JSON as <see cref="long"/>: an enum in an <c>object</c> member returns as its
/// underlying number even with <c>TypeNameHandling.All</c> — measured, and pinned by
/// <c>ComponentModelExTests.AnEnumInAnObjectMember_ComesBackAsItsNumber</c>. A <i>static</i> branch survives that by
/// luck (both sides degrade to <see cref="long"/> and still compare equal), but a <i>dynamic</i> branch re-resolves
/// its key as a real enum at run time and would then match no option at all — the run would end as if the branch had
/// no downstream.
/// </para>
/// <para>
/// So the compiler records the key's type name beside it whenever the key is an enum, and
/// <see cref="BranchSegment"/> / <see cref="BranchOption"/> restore the value when the document is loaded. Nothing
/// else needs a side channel: a string, a number or a bool round-trips as itself.
/// </para>
/// </summary>
internal static class CompileKeyNormalizer
{
    /// <summary>The type name to record for <paramref name="key"/>, or <c>null</c> when none is needed.</summary>
    internal static string? TypeNameOf(object? key)
        => key is not null && key.GetType().IsEnum ? key.GetType().AssemblyQualifiedName : null;

    /// <summary>
    /// Puts <paramref name="key"/> back into its original enum, or returns it unchanged when there is nothing to
    /// restore — an enum needs nothing, and a number with no recorded type (or a type that will not resolve) is left
    /// alone rather than guessed at.
    /// <para>
    /// A number matching no member becomes an undefined enum value rather than an error: that is what a live run does
    /// with a key no option names — it selects nothing and the flow ends where it ends, with no fabricated result.
    /// </para>
    /// </summary>
    // 按名字还原路由键的类型。与 SlotEnumerator 那处同因：键是宿主的枚举，没有成员的声明类型是它，
    // 所以它不在 Agent 目录里，只能按名字找。宿主必须自己保住那个枚举的元数据。
#pragma warning disable IL2057, IL2026 // 按名字解析类型：见上
    internal static object? Normalize(object? key, string? typeName)
    {
        if (key is null || typeName is not { Length: > 0 }) return key;
        if (key.GetType().IsEnum) return key;             // already restored, or never degraded
        if (key is not long and not int) return key;      // strings, bools, … round-trip as themselves

        var type = Type.GetType(typeName);
        return type is null || !type.IsEnum ? key : Enum.ToObject(type, Convert.ToInt64(key));
    }
}

#pragma warning restore IL2057, IL2026
