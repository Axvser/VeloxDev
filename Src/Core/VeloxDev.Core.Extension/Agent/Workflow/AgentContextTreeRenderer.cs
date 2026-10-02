using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.AI.Workflow;

/// <summary>
/// Renders the prompt tables from the compiled context tree instead of from reflection.
/// </summary>
/// <remarks>
/// <para>
/// Output-compatible with the reflection renderers in <see cref="AgentContextCollector"/>, which stay as the
/// fallback for types the tree does not carry. The shapes are load-bearing — the model reads them — so they are
/// reproduced exactly, including the blank lines and the two different description joinings (the enum table
/// concatenates, every other table joins with <c>"; "</c>).
/// </para>
/// <para>
/// Two deliberate normalizations differ from the reflection path. Base interfaces are listed in a sorted,
/// deterministic order where reflection's is unspecified; and members are gathered by walking the base-type
/// chain the tree records, so a derived component still shows what it inherits. The second is why
/// <see cref="AIContextRefKind.BaseType"/> exists at all.
/// </para>
/// </remarks>
internal static class AgentContextTreeRenderer
{
    private static readonly AIContextDirectory Directory = AIContextDirectory.Shared;

    /// <summary>
    /// Finds the tree entry for a type.
    /// </summary>
    /// <param name="type">The type being rendered.</param>
    /// <param name="entry">Its entry, when the tree has one.</param>
    /// <returns><see langword="true"/> when the tree can render this type.</returns>
    internal static bool TryEntry(Type type, out AIContextNode entry)
    {
        entry = null!;

        var path = Directory.PathFor(type);
        if (path is null) return false;

        var found = Directory.Entry(path);
        if (found is null) return false;

        entry = found;
        return true;
    }

    // ── Enum ────────────────────────────────────────────────────────────────────────────────────────

    internal static string Enum(AIContextNode entry, AgentLanguages language)
    {
        var result = new StringBuilder();

        result.AppendLine("---");
        result.AppendLine();

        result.AppendLine("Enum");
        result.AppendLine();
        result.AppendLine($"Type: {entry.TypeName}");
        result.AppendLine();
        result.AppendLine("Descriptions:");
        foreach (var context in Texts(entry, language))
        {
            result.AppendLine($"- {context}");
        }
        result.AppendLine();
        result.AppendLine($"Member Value Type: {UnderlyingTypeOf(entry)}");
        result.AppendLine();
        result.AppendLine($"Member Value List:");
        result.AppendLine();
        result.AppendLine("| Name | Value | Description |");
        result.AppendLine("| ---- | ----- | ----------- |");
        foreach (var member in Sort(MembersOf(PathOf(entry), "Members")))
        {
            // 枚举表用拼接，不用 "; " —— 与反射那条逐字一致。
            result.AppendLine($"| {member.Name} | {member.Ordinal} | {ConcatDescriptions(member, language)} |");
        }
        result.AppendLine();

        return result.ToString();
    }

    // ── Interface ───────────────────────────────────────────────────────────────────────────────────

    internal static string Interface(AIContextNode entry, AgentLanguages language)
    {
        var result = new StringBuilder();

        result.AppendLine("---");
        result.AppendLine();

        result.AppendLine("Interface");
        result.AppendLine();
        result.AppendLine($"Type: {entry.TypeName}");
        result.AppendLine();
        result.AppendLine($"Base Interfaces:");
        result.AppendLine();
        foreach (var name in BaseInterfaceNames(entry))
        {
            result.AppendLine($"- {name}");
        }
        result.AppendLine();
        result.AppendLine("Descriptions:");
        foreach (var context in Texts(entry, language))
        {
            result.AppendLine($"- {context}");
        }
        result.AppendLine();

        // 命令属性住在 Commands 目录里，不在 Properties —— 两个都要取。
        // 反射那边是一次 `GetProperties()` 再按类型分流的，所以相对顺序在两边都保住了。
        var normalProps = MembersAcross(entry, "Properties").ToArray();
        if (normalProps.Length > 0)
        {
            result.AppendLine("Properties:");
            result.AppendLine();
            result.AppendLine("| Name | Description |");
            result.AppendLine("| ---- | ----------- |");

            foreach (var prop in normalProps)
            {
                result.AppendLine($"| {prop.Name} | {Descriptions(prop, language)} |");
            }
            result.AppendLine();
        }

        var commandProps = MembersAcross(entry, "Commands").ToArray();
        if (commandProps.Length > 0)
        {
            result.AppendLine("Commands:");
            result.AppendLine();
            result.AppendLine("| Name | ParameterType | Description |");
            result.AppendLine("| ---- | ------------- | ----------- |");

            foreach (var command in commandProps)
            {
                result.AppendLine($"| {command.Name} | {CommandParameterType(command)} | {Descriptions(command, language)} |");
            }
        }
        result.AppendLine();

        return result.ToString();
    }

    // ── Class ───────────────────────────────────────────────────────────────────────────────────────

    internal static string Class(AIContextNode entry, AgentLanguages language)
    {
        var result = new StringBuilder();

        result.AppendLine("---");
        result.AppendLine();

        result.AppendLine("Class");
        result.AppendLine();
        result.AppendLine($"Type: {entry.TypeName}");
        result.AppendLine();
        result.AppendLine($"Base Interfaces:");
        result.AppendLine();
        foreach (var name in BaseInterfaceNames(entry))
        {
            result.AppendLine($"- {name}");
        }
        result.AppendLine();
        result.AppendLine("Developer Instructions (AUTHORITATIVE — these override any runtime default values):");
        foreach (var context in Texts(entry, language))
        {
            result.AppendLine($"- {context}");
        }
        result.AppendLine();
        result.AppendLine("Properties:");
        result.AppendLine();
        result.AppendLine("| Type | Name | Description |");
        result.AppendLine("| ---- | ---- | ----------- |");

        var properties = MembersAcross(entry, "Properties");

        // [VeloxProperty] 字段提升出来的属性 —— 反射那条把它们当「字段」列在这里。
        foreach (var field in properties.Where(static p => p.Has(AIContextFlags.IsPromotedField) && p.Descriptions.Length > 0))
        {
            result.AppendLine($"| {field.TypeName} | {field.Name} | {Descriptions(field, language)} |");
        }

        // 提升出来的属性也在这里 —— 反射看到的是「属性」，`[VeloxProperty]` 标在字段上，
        // 所以它们靠 IsSlotEnumerator / IsSingleSlot 进这张表，不靠 HasVeloxProperty。
        foreach (var prop in properties.Where(static p =>
                     (p.Has(AIContextFlags.HasVeloxProperty) && p.Descriptions.Length > 0)
                     || p.Has(AIContextFlags.IsSlotEnumerator)
                     || p.Has(AIContextFlags.IsSingleSlot)))
        {
            result.AppendLine($"| {prop.TypeName} | {prop.Name} | {PropertyDescription(prop, language)} |");
        }

        result.AppendLine();

        result.AppendLine("Commands:");
        result.AppendLine();
        result.AppendLine("| Name | ParameterType | Description |");
        result.AppendLine("| ---- | ------------- | ----------- |");

        foreach (var method in MembersAcross(entry, "Methods")
                     .Where(static m => m.Has(AIContextFlags.HasVeloxCommand) && m.Descriptions.Length > 0))
        {
            // 反射那条打的是方法名去掉 "Async"，不是命令属性名 —— 两者都要照着来。
            var commandName = method.Name.Replace("Async", "");
            result.AppendLine($"| {commandName} | {CommandParameterType(method)} | {Descriptions(method, language)} |");
        }
        result.AppendLine();

        return result.ToString();
    }

    // ── Data ────────────────────────────────────────────────────────────────────────────────────────

    internal static string Data(AIContextNode entry, AgentLanguages language)
    {
        var result = new StringBuilder();

        result.AppendLine("---");
        result.AppendLine();
        result.AppendLine("Data Type");
        result.AppendLine();
        result.AppendLine($"Type: {entry.TypeName}");
        result.AppendLine();

        var contexts = Texts(entry, language);
        if (contexts.Length > 0)
        {
            result.AppendLine("Descriptions:");
            foreach (var ctx in contexts) result.AppendLine($"- {ctx}");
            result.AppendLine();
        }

        result.AppendLine("Fields / Properties:");
        result.AppendLine();
        result.AppendLine("| Type | Name | Description |");
        result.AppendLine("| ---- | ---- | ----------- |");

        foreach (var field in MembersAcross(entry, "Fields").Concat(MembersAcross(entry, "Properties"))
                     .Where(static m => m.Descriptions.Length > 0))
        {
            result.AppendLine($"| {field.TypeName} | {field.Name} | {Descriptions(field, language)} |");
        }

        result.AppendLine();
        return result.ToString();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A type entry's own members, plus those of every base type the tree can follow.</summary>
    private static IReadOnlyList<AIContextNode> MembersAcross(AIContextNode entry, string memberDirectory)
        => entry.TypeName is null ? [] : Directory.MembersAcross(entry.TypeName, memberDirectory);

    private static IReadOnlyList<AIContextNode> MembersOf(string entryPath, string memberDirectory)
        => Directory.Members(entryPath, memberDirectory);

    /// <summary>A type entry's path — the registry owns paths, a node only carries its own name.</summary>
    private static string PathOf(AIContextNode entry)
        => entry.TypeName is null ? string.Empty : Directory.PathFor(entry.TypeName) ?? string.Empty;

    /// <summary>
    /// The members in the order the tree holds them, which is declaration order.
    /// </summary>
    /// <remarks>
    /// Deliberately not sorted. Reflection reports declaration order, and the parity test compares the two
    /// character for character — an alphabetical sort here would differ on every type with more than one member.
    /// </remarks>
    private static IReadOnlyList<AIContextNode> Sort(IReadOnlyList<AIContextNode> nodes) => nodes;

    private static string[] Texts(AIContextNode node, AgentLanguages language)
        => AgentTextSelection.Select(node.Descriptions, language);

    /// <summary>The <c>[1-a]; [2-b]</c> form every table but the enum one uses.</summary>
    private static string Descriptions(AIContextNode node, AgentLanguages language)
        => string.Join("; ", Texts(node, language).Select(static (ctx, i) => $"[{i + 1}-{ctx}]"));

    /// <summary>The <c>[1-a][2-b]</c> form the enum table uses.</summary>
    private static string ConcatDescriptions(AIContextNode node, AgentLanguages language)
    {
        var builder = new StringBuilder();
        var texts = Texts(node, language);
        for (var i = 0; i < texts.Length; i++) builder.Append($"[{i + 1}-{texts[i]}]");
        return builder.ToString();
    }

    private static string PropertyDescription(AIContextNode node, AgentLanguages language)
    {
        if (node.Descriptions.Length > 0) return Descriptions(node, language);

        var typeName = LastSegment(node.TypeName ?? string.Empty);

        if (node.Has(AIContextFlags.IsSingleSlot))
        {
            return language == AgentLanguages.Chinese
                ? $"单插槽属性（{typeName}）— 通过 ResolveSlotId 按属性名解析，或用 ConnectByProperty 直接连接"
                : $"Single slot property ({typeName}) — resolve via ResolveSlotId by property name, or connect directly with ConnectByProperty.";
        }

        var baseText = language == AgentLanguages.Chinese
            ? "SlotEnumerator — 通过 SetEnumSlotCollection 工具配置选择器类型（枚举或 bool），禁止手动增删"
            : "SlotEnumerator — use SetEnumSlotCollection to configure the selector type (enum or bool). Do not add/remove slots manually.";

        var selectors = string.Join(", ", node.References
            .Where(static r => r.Kind == AIContextRefKind.SlotSelectorType)
            .Select(static r => r.DeclaredName));

        if (selectors.Length == 0) return baseText;

        return baseText + (language == AgentLanguages.Chinese
            ? $"; [允许的选择器类型 (allowedSelectorTypes): {selectors}]"
            : $"; [allowedSelectorTypes: {selectors}]");
    }

    private static string CommandParameterType(AIContextNode node)
    {
        var reference = node.References.FirstOrDefault(static r => r.Kind == AIContextRefKind.CommandParameterType);
        return reference.DeclaredName is null || reference.DeclaredName.Length == 0 ? "(none)" : reference.DeclaredName;
    }

    private static string UnderlyingTypeOf(AIContextNode entry)
    {
        var reference = entry.References.FirstOrDefault(static r => r.Kind == AIContextRefKind.MemberType);
        return reference.DeclaredName ?? "System.Int32";
    }

    private static IEnumerable<string> BaseInterfaceNames(AIContextNode entry)
        => entry.References
                .Where(static r => r.Kind == AIContextRefKind.BaseInterface)
                .Select(static r => r.DeclaredName);

    private static string LastSegment(string fullName)
    {
        var separator = fullName.LastIndexOf('.');
        return separator < 0 ? fullName : fullName.Substring(separator + 1);
    }
}
