using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VeloxDev.Generators.Base
{
    // 资源文件里对上的说明。成员名按目录里的名字与源码里的名字两边都认：
    // SaveCommand / Save、Payload / _payload，作者照着眼前的代码写就行。
    // 一条都没对上的条目会报出来 —— 资源是 Agent 面的第二份声明，写错了不能静默少一段说明。
    internal sealed class AIContextSupplement
    {
        private readonly Dictionary<string, List<AIContextXmlType>> _types =
            new(StringComparer.Ordinal);

        private AIContextSupplement() { }

        // 把资源里的类型名对到本程序集的符号上；对不上的直接报。
        internal static AIContextSupplement? Resolve(
            AIContextXml? xml,
            IReadOnlyList<INamedTypeSymbol> candidates,
            List<Diagnostic> notices)
        {
            if (xml is null || xml.Types.Count == 0) return null;

            var index = BuildTypeIndex(candidates);
            var supplement = new AIContextSupplement();

            foreach (var entry in xml.Types)
            {
                var name = NormalizeTypeName(entry.Name);

                if (!index.TryGetValue(name, out var symbol))
                {
                    notices.Add(Diagnostic.Create(
                        Diagnostics.UnknownAgentContextType,
                        entry.Location,
                        name));
                    continue;
                }

                var key = AIContextModelBuilder.ReflectionFullName(symbol);
                if (!supplement._types.TryGetValue(key, out var entries))
                {
                    entries = [];
                    supplement._types[key] = entries;
                }

                entries.Add(entry);
            }

            return supplement._types.Count == 0 ? null : supplement;
        }

        // 把属于 symbol 的资源文字接在特性文字后面；treeName 是目录里生成的名字。
        internal void AddTexts(ISymbol symbol, string treeName, List<AIContextText> texts)
        {
            if (symbol is INamedTypeSymbol type)
            {
                if (!TryEntries(type, out var entries)) return;

                foreach (var entry in entries)
                {
                    texts.AddRange(entry.Texts);
                }

                return;
            }

            if (symbol.ContainingType is null) return;
            if (!TryEntries(symbol.ContainingType, out var memberEntries)) return;

            foreach (var entry in memberEntries)
            {
                foreach (var member in entry.Members)
                {
                    if (!Matches(member.Name, treeName, symbol.Name)) continue;

                    member.Consumed = true;
                    texts.AddRange(member.Texts);
                }
            }
        }

        // 资源里有没有点名这个成员；只用来决定「描述-only 私有字段」要不要收进目录。
        internal bool HasMemberTexts(ISymbol member, string treeName)
        {
            if (member.ContainingType is null) return false;
            if (!TryEntries(member.ContainingType, out var entries)) return false;

            foreach (var entry in entries)
            {
                foreach (var candidate in entry.Members)
                {
                    if (Matches(candidate.Name, treeName, member.Name)) return true;
                }
            }

            return false;
        }

        // 只有确实进了目录的类型才认领条目；空 <Type> 不该因为被读了一次就算对上。
        internal void MarkConsumed(INamedTypeSymbol type)
        {
            if (!TryEntries(type, out var entries)) return;

            foreach (var entry in entries) entry.Consumed = true;
        }

        // 报出没被任何节点认领的条目。
        internal void Report(List<Diagnostic> notices)
        {
            foreach (var entries in _types.Values)
            {
                foreach (var entry in entries)
                {
                    if (!entry.Consumed)
                    {
                        // 类型没进目录时，逐条成员再报一遍只会把同一个原因说 N 次。
                        notices.Add(Diagnostic.Create(
                            Diagnostics.UnknownAgentContextType,
                            entry.Location,
                            entry.Name));
                        continue;
                    }

                    foreach (var member in entry.Members)
                    {
                        if (member.Consumed) continue;

                        notices.Add(Diagnostic.Create(
                            Diagnostics.UnknownAgentContextMember,
                            member.Location,
                            member.Name,
                            entry.Name));
                    }
                }
            }
        }

        private bool TryEntries(INamedTypeSymbol type, out List<AIContextXmlType> entries)
            => _types.TryGetValue(AIContextModelBuilder.ReflectionFullName(type), out entries!);

        private static bool Matches(string resourceName, string treeName, string sourceName)
            => string.Equals(resourceName, treeName, StringComparison.Ordinal)
               || string.Equals(resourceName, sourceName, StringComparison.Ordinal);

        private static string NormalizeTypeName(string name)
        {
            var trimmed = name.Trim();
            return trimmed.StartsWith("global::", StringComparison.Ordinal)
                ? trimmed.Substring("global::".Length)
                : trimmed;
        }

        // 元数据全名（A.B+C）优先，源码写法（A.B.C）只在没被占用时补进去。
        // Foo 与 Foo<T> 会拼出同一个源码名 —— 那种写法直接丢掉，元数据键不受影响。
        private static Dictionary<string, INamedTypeSymbol> BuildTypeIndex(
            IReadOnlyList<INamedTypeSymbol> candidates)
        {
            var index = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);

            foreach (var symbol in candidates)
                AddMetadataKey(index, AIContextModelBuilder.ReflectionFullName(symbol), symbol);

            var display = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            var ambiguous = new HashSet<string>(StringComparer.Ordinal);

            foreach (var symbol in candidates)
            {
                var key = AIContextModelBuilder.DisplayFullName(symbol);
                if (index.ContainsKey(key)) continue;

                if (display.TryGetValue(key, out var existing))
                {
                    if (SymbolEqualityComparer.Default.Equals(existing, symbol)) continue;

                    display.Remove(key);
                    ambiguous.Add(key);
                    continue;
                }

                display[key] = symbol;
            }

            foreach (var pair in display)
            {
                if (!ambiguous.Contains(pair.Key)) index[pair.Key] = pair.Value;
            }

            return index;
        }

        private static void AddMetadataKey(
            Dictionary<string, INamedTypeSymbol> index,
            string key,
            INamedTypeSymbol symbol)
        {
            if (!index.TryGetValue(key, out var existing))
            {
                index[key] = symbol;
                return;
            }

            if (!SymbolEqualityComparer.Default.Equals(existing, symbol)) index.Remove(key);
        }
    }
}
