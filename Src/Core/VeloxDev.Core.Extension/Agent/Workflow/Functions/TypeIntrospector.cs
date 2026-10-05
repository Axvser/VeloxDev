using System;
using System.Collections.Generic;
using System.Linq;
using VeloxDev.Serialization;

namespace VeloxDev.AI.Workflow.Functions;

/// <summary>
/// Describes a type the agent context tree carries, as a JSON schema the Agent can read.
/// </summary>
/// <remarks>
/// <para>
/// Everything here comes from the compiled context tree and that type's generated
/// <see cref="IAIContextAccessor"/>: which members exist, what they are typed as, which enum members there are,
/// and whether a default instance can be built. No metadata is reflected over.
/// </para>
/// <para>
/// The schema is a description, not a contract: <c>defaultJson_runtimeOnly</c> is a zero-initialized instance
/// rather than the author's intended defaults, which is why <c>developerInstructions</c> is the field a caller
/// should trust.
/// </para>
/// </remarks>
public static class TypeIntrospector
{
    /// <summary>
    /// Resolves a <see cref="Type"/> by its full name from the context tree.
    /// </summary>
    /// <param name="fullTypeName">The type's full name, as <see cref="Type.FullName"/> reports it.</param>
    /// <returns>The type, or <see langword="null"/> when the tree carries no such type — a closed world.</returns>
    public static Type? ResolveType(string fullTypeName)
        => AgentTypeResolver.ResolveType(fullTypeName);

    /// <summary>
    /// Produces a JSON schema-like description of a type including its members, base type, interfaces,
    /// and (for enums) its values.
    /// </summary>
    /// <param name="type">The type to describe.</param>
    /// <returns>The schema as indented JSON.</returns>
    public static string GetTypeSchema(Type type)
    {
        var fullName = type.FullName ?? type.Name;
        var obj = new VeloxJsonObject
        {
            ["fullName"] = fullName,
            // 工具把 JSON 交回宿主时走的是归档序列化器，而它是闭世界：没有读写器的类型根本建不出来。
            // 这条就是「先调 GetTypeSchema」那一步要的答案 —— 让模型在构造 JSON 之前就知道，
            // 而不是构造完才发现宿主吃不下（那种类型要补 [Archivable]）。
            ["jsonReadable"] = VeloxJsonRegistry.ReaderFor(type) is not null,
        };

        var path = AIContextDirectory.Shared.PathFor(fullName);
        var entry = path is null ? null : AIContextDirectory.Shared.Entry(path);
        if (entry is null) return obj.ToJson(VeloxJsonFormat.Indented);

        obj["kind"] = entry.Kind switch
        {
            AIContextNodeKind.EnumType => "enum",
            AIContextNodeKind.InterfaceType => "interface",
            _ => entry.Has(AIContextFlags.IsValueType) ? "struct" : "class",
        };

        var baseType = ReferenceName(entry, AIContextRefKind.BaseType);
        if (baseType is not null) obj["baseType"] = baseType;

        obj["interfaces"] = VeloxJsonValue.From(
            entry.References
                .Where(static r => r.Kind == AIContextRefKind.BaseInterface)
                .Select(static r => r.DeclaredName)
                .ToArray());

        if (entry.Kind == AIContextNodeKind.EnumType)
        {
            var values = new VeloxJsonObject();
            foreach (var member in AIContextDirectory.Shared.Members(path!, "Members"))
            {
                values[member.Name] = member.Ordinal;
            }
            obj["values"] = values;
        }
        else
        {
            var accessor = AIContextTreeRegistry.FindAccessor(fullName);
            var props = new VeloxJsonArray();

            foreach (var member in AIContextDirectory.Shared.MembersAcross(fullName, "Properties"))
            {
                props.Add(new VeloxJsonObject
                {
                    ["name"] = member.Name,
                    ["type"] = FriendlyTypeName(accessor?.MemberType(member.Name)),
                    ["canRead"] = member.Has(AIContextFlags.CanRead),
                    ["canWrite"] = member.Has(AIContextFlags.CanWrite),
                });
            }

            obj["properties"] = props;
        }

        // 每条语言各取一次 —— 说明文字按语言分开存，这里不做回退挑选，全部倒出来。
        var agentDescs = new VeloxJsonArray();
        foreach (var lang in AgentLanguagesExtensions.AllLanguages)
        {
            foreach (var desc in AgentContextCollector.GetAgentContext(type, lang))
                agentDescs.Add(desc);
        }
        if (agentDescs.Count > 0)
            obj["developerInstructions"] = agentDescs;

        // Try to create a default instance and serialize it through the generated serializer.
        // NOTE: These are runtime zero-initialized values, NOT the intended defaults.
        // Always prefer developerInstructions over defaultJson.
        try
        {
            var instance = AIContextTreeRegistry.FindAccessor(fullName)?.Create();
            if (instance is not null)
                obj["defaultJson_runtimeOnly"] = VeloxJsonValue.From(instance);
        }
        catch { /* default instance not available */ }

        return obj.ToJson(VeloxJsonFormat.Indented);
    }

    /// <summary>The name a cross-link carries, or <see langword="null"/> when the entry has no such link.</summary>
    private static string? ReferenceName(AIContextNode entry, AIContextRefKind kind)
        => entry.References
                .Where(r => r.Kind == kind)
                .Select(static r => r.DeclaredName)
                .FirstOrDefault(static name => name.Length > 0);

    /// <summary>The short, C#-flavoured spelling of a type: <c>int</c>, <c>string</c>, <c>List&lt;int&gt;</c>.</summary>
    private static string FriendlyTypeName(Type? t)
    {
        if (t is null) return string.Empty;
        if (t == typeof(string)) return "string";
        if (t == typeof(int)) return "int";
        if (t == typeof(double)) return "double";
        if (t == typeof(bool)) return "bool";
        if (t == typeof(float)) return "float";
        if (t == typeof(long)) return "long";
        if (t == typeof(decimal)) return "decimal";
        if (t == typeof(void)) return "void";
        if (t.IsGenericType)
        {
            var baseName = t.Name.Split('`')[0];
            var args = string.Join(", ", t.GetGenericArguments().Select(FriendlyTypeName));
            return $"{baseName}<{args}>";
        }
        return t.FullName ?? t.Name;
    }
}
