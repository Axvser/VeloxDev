using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace VeloxDev.Generators.Base
{
    // *.agentcontext.xml 是编译期输入：生成器读它，把文字写进目录；产物里没有 XML，目录仍只装字面量。
    internal sealed class AIContextXml
    {
        internal const string FileSuffix = ".agentcontext.xml";

        private const string RootElementName = "AgentContext";
        private const string TypeElementName = "Type";
        private const string MemberElementName = "Member";
        private const string TextElementName = "Text";
        private const string DefaultLanguageName = "English";

        internal static readonly AIContextXml Empty = new([], []);

        private AIContextXml(IReadOnlyList<AIContextXmlType> types, IReadOnlyList<Diagnostic> diagnostics)
        {
            Types = types;
            Diagnostics = diagnostics;
        }

        // 每个 <Type> 条目，按文件路径与文档顺序。
        internal IReadOnlyList<AIContextXmlType> Types { get; }

        // 读不出来的部分；静默丢弃会让作者以为写对了。
        internal IReadOnlyList<Diagnostic> Diagnostics { get; }

        // 按路径排序：诊断位置与文字顺序不能取决于编译器递文件的顺序。
        internal static AIContextXml Parse(
            IEnumerable<AdditionalText> files,
            Compilation compilation,
            System.Threading.CancellationToken cancellationToken)
        {
            var ordered = files
                .Where(static file => file.Path.EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static file => file.Path, StringComparer.Ordinal)
                .ToList();

            if (ordered.Count == 0) return Empty;

            var languages = BuildLanguageMap(compilation.GetTypeByMetadataName("VeloxDev.AI.AgentLanguages"));
            var types = new List<AIContextXmlType>();
            var diagnostics = new List<Diagnostic>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 包里的 targets 会按约定自动收文件，作者再显式加一次时同一个路径可能出现两遍。
                if (!seen.Add(file.Path)) continue;

                ParseFile(file, languages, types, diagnostics, cancellationToken);
            }

            return new AIContextXml(types, diagnostics);
        }

        private static void ParseFile(
            AdditionalText file,
            IReadOnlyDictionary<string, int> languages,
            List<AIContextXmlType> types,
            List<Diagnostic> diagnostics,
            System.Threading.CancellationToken cancellationToken)
        {
            var text = file.GetText(cancellationToken);
            if (text is null)
            {
                diagnostics.Add(Diagnostic.Create(
                    global::VeloxDev.Generators.Diagnostics.UnreadableAgentContextResource,
                    Location.None,
                    file.Path,
                    "the file has no text"));
                return;
            }

            XDocument document;
            try
            {
                var content = text.ToString();
                if (content.Length > 0 && content[0] == '\uFEFF') content = content.Substring(1);

                document = XDocument.Parse(content, LoadOptions.SetLineInfo);
            }
            catch (XmlException exception)
            {
                diagnostics.Add(Diagnostic.Create(
                    global::VeloxDev.Generators.Diagnostics.UnreadableAgentContextResource,
                    LocationAt(file.Path, text, exception.LineNumber, exception.LinePosition),
                    file.Path,
                    exception.Message));
                return;
            }

            var root = document.Root;
            if (root is null || root.Name.LocalName != RootElementName)
            {
                diagnostics.Add(Diagnostic.Create(
                    global::VeloxDev.Generators.Diagnostics.UnreadableAgentContextResource,
                    root is null ? Location.None : LocationOf(root, text, file.Path),
                    file.Path,
                    $"the root element must be <{RootElementName}>"));
                return;
            }

            WarnUnknownAttributes(root, allowed: null, text, file.Path, diagnostics);

            foreach (var child in root.Elements())
            {
                if (child.Name.LocalName != TypeElementName)
                {
                    diagnostics.Add(Unsupported(child, text, file.Path));
                    continue;
                }

                var type = ParseType(child, languages, text, file.Path, diagnostics);
                if (type is not null) types.Add(type);
            }
        }

        private static AIContextXmlType? ParseType(
            XElement element,
            IReadOnlyDictionary<string, int> languages,
            SourceText text,
            string path,
            List<Diagnostic> diagnostics)
        {
            WarnUnknownAttributes(element, "name", text, path, diagnostics);

            var name = (string?)element.Attribute("name");
            if (string.IsNullOrWhiteSpace(name))
            {
                diagnostics.Add(Diagnostic.Create(
                    global::VeloxDev.Generators.Diagnostics.UnreadableAgentContextResource,
                    LocationOf(element, text, path),
                    path,
                    "a <Type> element requires a non-empty 'name' attribute"));
                return null;
            }

            var texts = new List<AIContextText>();
            var members = new List<AIContextXmlMember>();

            foreach (var child in element.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case TextElementName:
                        var typeText = ParseText(child, languages, text, path, diagnostics);
                        if (typeText is not null) texts.Add(typeText);
                        break;

                    case MemberElementName:
                        var member = ParseMember(child, languages, text, path, diagnostics);
                        if (member is not null) members.Add(member);
                        break;

                    default:
                        diagnostics.Add(Unsupported(child, text, path));
                        break;
                }
            }

            return new AIContextXmlType(name!.Trim(), LocationOf(element, text, path), texts, members);
        }

        private static AIContextXmlMember? ParseMember(
            XElement element,
            IReadOnlyDictionary<string, int> languages,
            SourceText text,
            string path,
            List<Diagnostic> diagnostics)
        {
            WarnUnknownAttributes(element, "name", text, path, diagnostics);

            var name = (string?)element.Attribute("name");
            if (string.IsNullOrWhiteSpace(name))
            {
                diagnostics.Add(Diagnostic.Create(
                    global::VeloxDev.Generators.Diagnostics.UnreadableAgentContextResource,
                    LocationOf(element, text, path),
                    path,
                    "a <Member> element requires a non-empty 'name' attribute"));
                return null;
            }

            var texts = new List<AIContextText>();

            foreach (var child in element.Elements())
            {
                if (child.Name.LocalName != TextElementName)
                {
                    diagnostics.Add(Unsupported(child, text, path));
                    continue;
                }

                var memberText = ParseText(child, languages, text, path, diagnostics);
                if (memberText is not null) texts.Add(memberText);
            }

            return new AIContextXmlMember(name!.Trim(), LocationOf(element, text, path), texts);
        }

        private static AIContextText? ParseText(
            XElement element,
            IReadOnlyDictionary<string, int> languages,
            SourceText text,
            string path,
            List<Diagnostic> diagnostics)
        {
            WarnUnknownAttributes(element, "language", text, path, diagnostics);

            var languageName = (string?)element.Attribute("language");
            if (string.IsNullOrWhiteSpace(languageName)) languageName = DefaultLanguageName;

            if (!languages.TryGetValue(languageName!, out var language))
            {
                diagnostics.Add(Diagnostic.Create(
                    global::VeloxDev.Generators.Diagnostics.UnknownAgentContextLanguage,
                    LocationOf(element, text, path),
                    languageName));
                return null;
            }

            return new AIContextText(language, NormalizeText(element.Value));
        }

        private static void WarnUnknownAttributes(
            XElement element,
            string? allowed,
            SourceText text,
            string path,
            List<Diagnostic> diagnostics)
        {
            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration) continue;
                if (allowed is not null && attribute.Name.LocalName == allowed) continue;

                diagnostics.Add(Diagnostic.Create(
                    global::VeloxDev.Generators.Diagnostics.UnsupportedAgentContextResourceSyntax,
                    LocationOf(attribute, text, path),
                    attribute.Name.LocalName));
            }
        }

        private static Diagnostic Unsupported(XElement element, SourceText text, string path)
            => Diagnostic.Create(
                global::VeloxDev.Generators.Diagnostics.UnsupportedAgentContextResourceSyntax,
                LocationOf(element, text, path),
                element.Name.LocalName);

        private static Dictionary<string, int> BuildLanguageMap(INamedTypeSymbol? languages)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (languages is null) return map;

            // 按符号上的字段建表，不复制枚举值：Chinese 与 ChineseSimplified 这类别名一并认。
            foreach (var field in languages.GetMembers().OfType<IFieldSymbol>())
            {
                if (!field.HasConstantValue || field.ConstantValue is null) continue;
                map[field.Name] = Convert.ToInt32(field.ConstantValue, System.Globalization.CultureInfo.InvariantCulture);
            }

            return map;
        }

        // 去掉嵌套带来的缩进与首尾空行：说明原样交给模型，文件排版不该跟着进去。
        private static string NormalizeText(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            var normalized = raw!.Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = normalized.Split('\n');

            var start = 0;
            while (start < lines.Length && string.IsNullOrWhiteSpace(lines[start])) start++;

            var end = lines.Length - 1;
            while (end >= start && string.IsNullOrWhiteSpace(lines[end])) end--;

            if (start > end) return string.Empty;

            var indent = int.MaxValue;
            for (var i = start; i <= end; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var current = 0;
                while (current < line.Length && (line[current] == ' ' || line[current] == '\t')) current++;
                if (current < indent) indent = current;
            }

            if (indent == int.MaxValue) indent = 0;

            var builder = new StringBuilder();
            for (var i = start; i <= end; i++)
            {
                if (i > start) builder.Append('\n');
                var line = lines[i];
                builder.Append(line.Length > indent ? line.Substring(indent) : string.Empty);
            }

            return builder.ToString();
        }

        private static Location LocationOf(XObject node, SourceText text, string path)
            => node is IXmlLineInfo info && info.HasLineInfo()
                ? LocationAt(path, text, info.LineNumber, info.LinePosition)
                : Location.None;

        private static Location LocationAt(string path, SourceText text, int lineNumber, int linePosition)
        {
            if (lineNumber <= 0 || text.Lines.Count == 0) return Location.None;

            var lineIndex = Math.Min(lineNumber - 1, text.Lines.Count - 1);
            var line = text.Lines[lineIndex];
            var column = Math.Max(0, Math.Min(linePosition - 1, line.Span.Length));
            var position = new LinePosition(lineIndex, column);

            return Location.Create(path, new TextSpan(line.Start + column, 0), new LinePositionSpan(position, position));
        }
    }

    // 一个 <Type> 条目。
    internal sealed class AIContextXmlType
    {
        internal AIContextXmlType(
            string name,
            Location location,
            IReadOnlyList<AIContextText> texts,
            IReadOnlyList<AIContextXmlMember> members)
        {
            Name = name;
            Location = location;
            Texts = texts;
            Members = members;
        }

        internal string Name { get; }

        internal Location Location { get; }

        internal IReadOnlyList<AIContextText> Texts { get; }

        internal IReadOnlyList<AIContextXmlMember> Members { get; }

        // 类型没进目录时，这条要报出来。
        internal bool Consumed { get; set; }
    }

    // 一个 <Member> 条目。
    internal sealed class AIContextXmlMember
    {
        internal AIContextXmlMember(string name, Location location, IReadOnlyList<AIContextText> texts)
        {
            Name = name;
            Location = location;
            Texts = texts;
        }

        internal string Name { get; }

        internal Location Location { get; }

        internal IReadOnlyList<AIContextText> Texts { get; }

        // 没有成员节点认领时，这条要报出来。
        internal bool Consumed { get; set; }
    }
}
