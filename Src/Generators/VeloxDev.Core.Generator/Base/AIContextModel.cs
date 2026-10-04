using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;

namespace VeloxDev.Generators.Base
{
    /// <summary>
    /// Everything one assembly contributes to the agent context tree, read off the compilation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built by one walk over <c>compilation.Assembly</c> rather than through <see cref="Analizer.Filters.Targets"/>:
    /// that pipeline only admits partial class declarations, while the tree's first-class citizens are interfaces
    /// and enums, and it is driven by attribute triggers while a component is recognised by the interface it
    /// implements and by members it does not annotate.
    /// </para>
    /// <para>
    /// The model holds symbols, not text. Rendering happens once, in one place, so the tree and the accessors
    /// cannot disagree about a name.
    /// </para>
    /// </remarks>
    internal sealed class AIContextAssembly
    {
        internal AIContextAssembly(string assemblyName, IReadOnlyList<AIContextType> types, IReadOnlyList<Diagnostic> notices)
        {
            AssemblyName = assemblyName;
            Types = types;
            Notices = notices;
        }

        /// <summary>The assembly the fragment describes.</summary>
        internal string AssemblyName { get; }

        /// <summary>Every type the fragment carries an entry for.</summary>
        internal IReadOnlyList<AIContextType> Types { get; }

        /// <summary>What the walk could not represent faithfully — reported rather than dropped in silence.</summary>
        internal IReadOnlyList<Diagnostic> Notices { get; }
    }

    /// <summary>Which of the collector's shapes a type entry is rendered as.</summary>
    internal enum AIContextTypeKind
    {
        /// <summary>Rendered member by member.</summary>
        Enum,

        /// <summary>Rendered as the interface an Agent may call through.</summary>
        Interface,

        /// <summary>Rendered as a workflow component.</summary>
        Component,

        /// <summary>Rendered as data.</summary>
        Data,
    }

    /// <summary>A member of a type entry.</summary>
    internal sealed class AIContextMember
    {
        internal AIContextMember(
            string name,
            ITypeSymbol declaredTypeSymbol,
            string declaredType,
            bool isMethod,
            bool isCommand,
            bool canRead,
            bool canWrite,
            bool isStatic,
            bool hasVeloxProperty,
            bool hasVeloxCommand,
            bool isPromotedField,
            bool hasSlotSelectors,
            IReadOnlyList<AIContextText> descriptions,
            IReadOnlyList<string> slotSelectorNames,
            IReadOnlyList<AIContextParameter> parameters)
        {
            Name = name;
            DeclaredTypeSymbol = declaredTypeSymbol;
            DeclaredType = declaredType;
            IsMethod = isMethod;
            IsCommand = isCommand;
            CanRead = canRead;
            CanWrite = canWrite;
            IsStatic = isStatic;
            HasVeloxProperty = hasVeloxProperty;
            HasVeloxCommand = hasVeloxCommand;
            IsPromotedField = isPromotedField;
            HasSlotSelectors = hasSlotSelectors;
            Descriptions = descriptions;
            SlotSelectorNames = slotSelectorNames;
            Parameters = parameters;
        }

        internal string Name { get; }

        /// <summary>The declared type's symbol — kept so conversions can name the destination at compile time.</summary>
        internal ITypeSymbol DeclaredTypeSymbol { get; }

        internal string DeclaredType { get; }
        internal bool IsMethod { get; }

        /// <summary>Whether the member is a field rather than a property — the two render differently.</summary>
        internal bool IsField { get; set; }

        /// <summary>Whether a method returns nothing. Read off the symbol, not the rendered type name.</summary>
        internal bool ReturnsVoid { get; set; }

        /// <summary>The symbol the member came from, kept so a notice can point at the author's own line.</summary>
        internal ISymbol? Symbol { get; set; }

        /// <summary>A command's declared parameter type, or null when it takes none — what the tables print.</summary>
        internal string? CommandParameterTypeName { get; set; }

        /// <summary>
        /// The command parameter's symbol — kept so the accessor can name the type in a <c>typeof</c> literal.
        /// </summary>
        internal ITypeSymbol? CommandParameterTypeSymbol { get; set; }

        /// <summary>Whether the member's type is a <c>SlotEnumerator&lt;T&gt;</c>.</summary>
        internal bool IsSlotEnumerator { get; set; }

        /// <summary>Whether the member's type is a single slot.</summary>
        internal bool IsSingleSlot { get; set; }

        /// <summary>Whether the member's type is a collection of slots.</summary>
        internal bool IsSlotCollection { get; set; }

        /// <summary>An enum member's underlying value; zero for anything else.</summary>
        internal long Ordinal { get; set; }
        internal bool IsCommand { get; }
        internal bool CanRead { get; }
        internal bool CanWrite { get; }
        internal bool IsStatic { get; }
        internal bool HasVeloxProperty { get; }
        internal bool HasVeloxCommand { get; }
        internal bool IsPromotedField { get; }
        internal bool HasSlotSelectors { get; }
        internal IReadOnlyList<AIContextText> Descriptions { get; }

        /// <summary>Types named by <c>[SlotSelectors]</c> in their string form, or empty.</summary>
        internal IReadOnlyList<string> SlotSelectorNames { get; }

        /// <summary>Declared parameters, for a method.</summary>
        internal IReadOnlyList<AIContextParameter> Parameters { get; }
    }

    /// <summary>A method parameter.</summary>
    internal sealed class AIContextParameter
    {
        internal AIContextParameter(string name, ITypeSymbol declaredTypeSymbol, string declaredType, bool isOptional, RefKind refKind)
        {
            Name = name;
            DeclaredTypeSymbol = declaredTypeSymbol;
            DeclaredType = declaredType;
            IsOptional = isOptional;
            RefKind = refKind;
        }

        /// <summary>
        /// How the parameter is passed. <c>out</c> and <c>ref</c> need a local declared at the parameter's own
        /// type — an <c>object</c> local cannot be passed to them.
        /// </summary>
        internal RefKind RefKind { get; }

        /// <summary>Whether the parameter needs a caller-side local — <c>out</c> or <c>ref</c>.</summary>
        internal bool NeedsLocal => RefKind is RefKind.Out or RefKind.Ref;

        internal string Name { get; }

        /// <summary>The parameter's declared type, so a conversion can name its destination at compile time.</summary>
        internal ITypeSymbol DeclaredTypeSymbol { get; }

        internal string DeclaredType { get; }
        internal bool IsOptional { get; }
    }

    /// <summary>One <c>[AgentContext]</c> description with the language it was written in.</summary>
    internal sealed class AIContextText
    {
        internal AIContextText(int language, string text)
        {
            Language = language;
            Text = text;
        }

        /// <summary>The enum's underlying value, not its name — <c>Chinese</c> aliases <c>ChineseSimplified</c>.</summary>
        internal int Language { get; }

        internal string Text { get; }
    }

    /// <summary>One type entry.</summary>
    internal sealed class AIContextType
    {
        internal AIContextType(
            INamedTypeSymbol symbol,
            string fullName,
            string path,
            AIContextTypeKind kind,
            IReadOnlyList<string> segments,
            IReadOnlyList<AIContextText> descriptions,
            IReadOnlyList<AIContextMember> members,
            IReadOnlyList<string> baseInterfaceNames,
            string? enumUnderlyingType)
        {
            Symbol = symbol;
            FullName = fullName;
            Path = path;
            Kind = kind;
            Segments = segments;
            Descriptions = descriptions;
            Members = members;
            BaseInterfaceNames = baseInterfaceNames;
            EnumUnderlyingType = enumUnderlyingType;
        }

        internal INamedTypeSymbol Symbol { get; }

        /// <summary>The type's full name, as <c>Type.FullName</c> reports it — the name index's key.</summary>
        internal string FullName { get; }

        /// <summary>Where the entry sits in the tree.</summary>
        internal string Path { get; }

        internal AIContextTypeKind Kind { get; }

        /// <summary>
        /// The full names of every interface the type implements.
        /// </summary>
        /// <remarks>
        /// Full names rather than simple ones: a renderer prints only the last segment, but it needs the full name
        /// to resolve the interface's own entry and pick up the members this type inherits from it.
        /// </remarks>
        internal IReadOnlyList<string> BaseInterfaceNames { get; }

        /// <summary>For an enum, the display name of its underlying integral type; null otherwise.</summary>
        internal string? EnumUnderlyingType { get; }

        /// <summary>The base type's full name, or null when it is <see cref="object"/> or there is none.</summary>
        internal string? BaseTypeName { get; set; }

        /// <summary>
        /// The directories this entry sits under, between its root and its own name —
        /// <c>Components/Nodes</c>, <c>Enums</c>, <c>Interfaces</c>, <c>Data</c>.
        /// </summary>
        internal IReadOnlyList<string> Segments { get; }
        internal IReadOnlyList<AIContextText> Descriptions { get; }
        internal IReadOnlyList<AIContextMember> Members { get; }
    }

    /// <summary>Builds the fragment model for one assembly.</summary>
    internal static class AIContextModelBuilder
    {
        private const string AgentContextAttributeName = "VeloxDev.AI.AgentContextAttribute";
        private const string AgentCommandParameterAttributeName = "VeloxDev.AI.AgentCommandParameterAttribute";
        private const string SlotSelectorsAttributeName = "VeloxDev.AI.SlotSelectorsAttribute";
        private const string VeloxPropertyAttributeName = "VeloxDev.MVVM.VeloxPropertyAttribute";
        private const string VeloxCommandAttributeName = "VeloxDev.MVVM.VeloxCommandAttribute";

        private static readonly string[] ComponentInterfaces =
        [
            "VeloxDev.WorkflowSystem.IWorkflowTreeViewModel",
            "VeloxDev.WorkflowSystem.IWorkflowNodeViewModel",
            "VeloxDev.WorkflowSystem.IWorkflowSlotViewModel",
            "VeloxDev.WorkflowSystem.IWorkflowLinkViewModel",
        ];

        /// <summary>
        /// True when the compilation takes part in the agent surface at all.
        /// </summary>
        /// <remarks>
        /// A project that never references <c>VeloxDev.AI</c> gets no walk and no output — the generator is inert
        /// rather than merely empty.
        /// </remarks>
        internal static bool Applies(Compilation compilation)
            => compilation.GetTypeByMetadataName(AgentContextAttributeName) is not null;

        /// <summary>
        /// Walks <paramref name="compilation"/>'s own assembly and builds its fragment.
        /// </summary>
        /// <param name="compilation">The compilation being generated for.</param>
        /// <param name="root">The tree root this fragment contributes under — <c>Framework</c> or <c>Customer</c>.</param>
        /// <returns>The fragment, or <see langword="null"/> when the assembly contributes nothing.</returns>
        internal static AIContextAssembly? Build(Compilation compilation, string root)
        {
            var candidates = EnumerateTypes(compilation.Assembly.GlobalNamespace).ToList();
            var included = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var types = new List<AIContextType>();

            // 第一趟：直接参与的 —— 组件、被标注的类型、带标注成员的类型。
            foreach (var symbol in candidates)
            {
                var type = BuildType(symbol, root);
                if (type is null) continue;

                included.Add(symbol);
                types.Add(type);
            }

            // 第二趟：可达的 —— 成员的声明类型里那些枚举与结构体，否则 Agent 会在描述里读到
            // 一个它查不到的字段类型名。只收本程序集内的，跨程序集的由那一侧自己的分片负责。
            foreach (var symbol in candidates)
            {
                if (included.Contains(symbol)) continue;
                if (symbol.TypeKind != TypeKind.Enum && symbol.TypeKind != TypeKind.Struct) continue;
                if (!IsReachableFromMembers(symbol, compiledTypes: candidates)) continue;

                var type = BuildType(symbol, root, force: true);
                if (type is null) continue;

                included.Add(symbol);
                types.Add(type);
            }

            if (types.Count == 0) return null;

            types.Sort(static (a, b) => string.CompareOrdinal(a.Path, b.Path));
            return new AIContextAssembly(compilation.AssemblyName ?? "Assembly", types, DetectNotices(types));
        }

        /// <summary>
        /// Finds the places the tree cannot represent faithfully, so they are reported instead of vanishing.
        /// </summary>
        /// <remarks>
        /// Today that is one thing: two methods sharing a name and an argument count. The tree keys a call by
        /// name and argument count, so only one of them can be reached — and which one is arbitrary, where the
        /// reflection path's answer is equally arbitrary but different.
        /// </remarks>
        private static IReadOnlyList<Diagnostic> DetectNotices(IReadOnlyList<AIContextType> types)
        {
            var notices = new List<Diagnostic>();

            foreach (var type in types)
            {
                var ambiguous = type.Members
                    .Where(static m => m.IsMethod)
                    .GroupBy(static m => m.Name + "|" + m.Parameters.Count, System.StringComparer.Ordinal)
                    .Where(static g => g.Count() > 1);

                foreach (var group in ambiguous)
                {
                    var first = group.First();
                    notices.Add(Diagnostic.Create(
                        VeloxDev.Generators.Diagnostics.AmbiguousMethodOverload,
                        first.Symbol?.Locations.FirstOrDefault() ?? type.Symbol.Locations.FirstOrDefault(),
                        type.FullName + "." + first.Name,
                        first.Parameters.Count));
                }
            }

            return notices;
        }

        /// <summary>
        /// Whether any member of an included type names <paramref name="symbol"/> as its declared type.
        /// </summary>
        private static bool IsReachableFromMembers(INamedTypeSymbol symbol, IReadOnlyList<INamedTypeSymbol> compiledTypes)
        {
            foreach (var owner in compiledTypes)
            {
                foreach (var member in owner.GetMembers())
                {
                    ITypeSymbol? declared = member switch
                    {
                        IPropertySymbol property => property.Type,
                        IFieldSymbol field when !field.IsConst => field.Type,
                        IMethodSymbol method when method.MethodKind == MethodKind.Ordinary => method.ReturnType,
                        _ => null,
                    };

                    if (declared is null) continue;
                    if (SymbolEqualityComparer.Default.Equals(declared, symbol)) return true;
                    if (declared is INamedTypeSymbol named && named.IsGenericType
                        && named.TypeArguments.Any(a => SymbolEqualityComparer.Default.Equals(a, symbol))) return true;
                }
            }

            return false;
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol scope)
        {
            foreach (var member in scope.GetMembers())
            {
                switch (member)
                {
                    case INamespaceSymbol nested:
                        foreach (var inner in EnumerateTypes(nested)) yield return inner;
                        break;
                    case INamedTypeSymbol type:
                        foreach (var inner in EnumerateTypeAndNested(type)) yield return inner;
                        break;
                }
            }
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateTypeAndNested(INamedTypeSymbol type)
        {
            yield return type;

            foreach (var nested in type.GetTypeMembers())
            {
                foreach (var inner in EnumerateTypeAndNested(nested)) yield return inner;
            }
        }

        /// <summary>
        /// Builds one type entry, or returns null when the type takes no part in the tree.
        /// </summary>
        /// <param name="symbol">The type.</param>
        /// <param name="root">The tree root the entry sits under.</param>
        /// <param name="force">
        /// Include the type even without annotations. Set when the type was reached through another entry's
        /// member type, which is how a plain enum ends up in the tree.
        /// </param>
        private static AIContextType? BuildType(INamedTypeSymbol symbol, string root, bool force = false)
        {
            // 编译器生成的类型（闭包、迭代器、匿名类型）没有稳定的名字，也不该出现在给模型的目录里。
            if (symbol.IsImplicitlyDeclared || symbol.Name.StartsWith("<")) return null;
            if (symbol.TypeKind is not (TypeKind.Class or TypeKind.Struct or TypeKind.Interface or TypeKind.Enum)) return null;

            // 开放泛型不能出现在生成的 cast 里（`(X<T>)value` 的 T 没有绑定）。嵌套在被泛型包住的
            // 类型同理 —— 它自己没有类型参数，但外面的 `T` 一样是未绑定的。两者都没有对 Agent 有意义的实例面。
            for (var current = symbol; current is not null; current = current.ContainingType)
            {
                if (current.TypeParameters.Length > 0) return null;
            }

            // 生成代码在同一个程序集里，所以 internal 可以，private/protected 不行 —— 一次也访问不到。
            if (!IsAccessible(symbol)) return null;

            // 静态类没有实例面：`(Static)target` 编不过（CS0716），访问器对它无事可做。
            if (symbol.IsStatic) return null;

            var componentKind = ComponentKindOf(symbol);
            var typeTexts = ReadTexts(symbol);
            var members = symbol.TypeKind == TypeKind.Enum ? ReadEnumMembers(symbol) : ReadMembers(symbol);

            // 只有「被标注过」的类型才主动进目录。仅因为它有公开成员就收录会把整个程序集倒进去 ——
            // 真正需要的是：它是个组件，或它（或它的某个成员）带着 Agent 相关的标注。
            var annotated = typeTexts.Count > 0
                            || members.Any(static m => m.Descriptions.Count > 0
                                                       || m.HasVeloxProperty
                                                       || m.HasVeloxCommand
                                                       || m.HasSlotSelectors
                                                       || m.IsCommand);

            if (!force && componentKind is null && !annotated) return null;

            var kind = symbol.TypeKind switch
            {
                TypeKind.Enum => AIContextTypeKind.Enum,
                TypeKind.Interface => AIContextTypeKind.Interface,
                _ => componentKind is not null ? AIContextTypeKind.Component : AIContextTypeKind.Data,
            };

            // 组件按工作流运行时已识别的四个接口拆分，「有哪些节点类型」于是变成一次目录列举，而不是带过滤的扫描。
            var segments = kind switch
            {
                AIContextTypeKind.Enum => new[] { "Enums" },
                AIContextTypeKind.Interface => new[] { "Interfaces" },
                AIContextTypeKind.Component => ["Components", componentKind!],
                _ => ["Data"],
            };

            // 必须是 `Type.FullName` 的形状，不是 Roslyn 的显示名：嵌套类型前者用 `+`、后者用 `.`，
            // 而这个字符串既是索引键也是渲染出来那一行。泛型已被排除，所以不用管反引号元数。
            var fullName = ReflectionFullName(symbol);

            // 顺序照 Roslyn 给的，不排序：渲染器要复现反射的 `GetInterfaces()`，而那是未定义的顺序 ——
            // 一排序就和它不一样了。测试会逐字对比两边。
            //
            // 名字用 `Type.Name` 的形状：泛型要带反引号元数（IReadOnlyDictionary`2），而 Roslyn 的 `Name`
            // 不带。渲染器直接印这个名字，不再从全名里切 —— 泛型实参里也有 `.`，切出来是垃圾。
            var baseInterfaceNames = symbol.TypeKind == TypeKind.Enum
                ? []
                : symbol.AllInterfaces
                        .Select(static i => i.TypeParameters.Length > 0 ? i.Name + "`" + i.TypeParameters.Length : i.Name)
                        .Distinct(System.StringComparer.Ordinal)
                        .ToArray();

            // 与 Enum.GetUnderlyingType(t).ToString() 同形：System.Byte 而不是 byte。
            // ToDisplayString 默认会把 System.Int32 印成 int，TableType 不特殊化关键字。
            var enumUnderlyingType = symbol.TypeKind == TypeKind.Enum && symbol.EnumUnderlyingType is not null
                ? TableType(symbol.EnumUnderlyingType)
                : null;

            // 必须是索引键那个形状（`ReflectionFullName`，嵌套用 `+`）：BaseType 引用要拿它去查目录，
            // 用 Roslyn 的 `global::A.B.C` 形状对嵌套基类型永远查不到，继承来的成员就整条丢掉。
            var baseTypeName = symbol.BaseType is { SpecialType: not SpecialType.System_Object } baseType
                               && baseType.TypeKind == TypeKind.Class
                ? ReflectionFullName(baseType)
                : null;

            return new AIContextType(
                symbol,
                fullName,
                $"{root}/" + string.Join("/", segments) + "/" + fullName,
                kind,
                segments,
                typeTexts,
                members,
                baseInterfaceNames,
                enumUnderlyingType)
            { BaseTypeName = baseTypeName };
        }

        /// <summary>
        /// The type's name in the shape <see cref="System.Type.FullName"/> reports.
        /// </summary>
        /// <remarks>
        /// Nested types are joined with <c>+</c>, not <c>.</c> — and the tree's index key, the accessor's
        /// <c>TypeName</c> and the rendered <c>Type:</c> line all have to agree with what reflection prints.
        /// </remarks>
        private static string ReflectionFullName(INamedTypeSymbol symbol)
        {
            var name = symbol.Name;

            if (symbol.ContainingType is not null)
                return ReflectionFullName(symbol.ContainingType) + "+" + name;

            var ns = symbol.ContainingNamespace;
            return ns is null || ns.IsGlobalNamespace ? name : ns.ToDisplayString() + "." + name;
        }

        /// <summary>
        /// Which component directory the type belongs in, or null when it is not a component.
        /// </summary>
        private static string? ComponentKindOf(INamedTypeSymbol symbol)
        {
            if (symbol.TypeKind != TypeKind.Class) return null;

            if (ImplementsInterface(symbol, "VeloxDev.WorkflowSystem.IWorkflowNodeViewModel")) return "Nodes";
            if (ImplementsInterface(symbol, "VeloxDev.WorkflowSystem.IWorkflowSlotViewModel")) return "Slots";
            if (ImplementsInterface(symbol, "VeloxDev.WorkflowSystem.IWorkflowLinkViewModel")) return "Links";
            if (ImplementsInterface(symbol, "VeloxDev.WorkflowSystem.IWorkflowTreeViewModel")) return "Trees";
            return null;
        }

        /// <summary>
        /// Whether generated code in this assembly can name the type — it and every type containing it.
        /// </summary>
        private static bool IsAccessible(INamedTypeSymbol symbol)
        {
            // 从类型自己开始：嵌套 private 类型的外层可能是 public，生成代码一样够不着它。
            for (var current = symbol; current is not null; current = current.ContainingType)
            {
                if (current.DeclaredAccessibility is Accessibility.Private
                    or Accessibility.Protected
                    or Accessibility.ProtectedAndInternal
                    or Accessibility.ProtectedOrInternal)
                {
                    return false;
                }
            }

            return !IsFileLocal(symbol);
        }

        /// <summary>
        /// Whether the type is declared <c>file</c>-scoped — reachable from its own file only, so generated code
        /// in another file can never name it.
        /// </summary>
        /// <remarks>
        /// Matched by the modifier's text: <c>SyntaxKind.FileKeyword</c> arrived in a later Roslyn than the one
        /// this generator compiles against, and the generator runs inside a newer compiler than it was built with.
        /// </remarks>
        private static bool IsFileLocal(INamedTypeSymbol symbol)
        {
            foreach (var reference in symbol.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax declaration
                    && declaration.Modifiers.Any(static modifier => modifier.Text == "file"))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ImplementsInterface(INamedTypeSymbol symbol, string interfaceMetadataName)
            => symbol.AllInterfaces.Any(i =>
                i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                 .Replace("global::", string.Empty) == interfaceMetadataName);

        private static IReadOnlyList<AIContextText> ReadTexts(ISymbol symbol)
            => ReadTexts(symbol.GetAttributes());

        private static IReadOnlyList<AIContextText> ReadTexts(IEnumerable<AttributeData> attributes)
        {
            var texts = new List<AIContextText>();

            foreach (var attribute in attributes)
            {
                if (AttributeName(attribute) != AgentContextAttributeName) continue;

                var arguments = attribute.ConstructorArguments;
                var language = arguments.Length > 0 && arguments[0].Value is not null
                    ? System.Convert.ToInt32(arguments[0].Value)
                    : 0;
                var text = arguments.Length > 1 ? arguments[1].Value as string ?? string.Empty : string.Empty;

                texts.Add(new AIContextText(language, text));
            }

            return texts;
        }

        /// <summary>
        /// The four members every type inherits from <see cref="object"/>.
        /// </summary>
        /// <remarks>
        /// Skipped for the same reason <c>AgentMethodInvoker.DiscoverMethods</c> skips them: no author means them
        /// to be part of the agent surface. They are also what an overload diagnostic would otherwise fire on —
        /// every value type with a typed <c>Equals</c> — which is how a warning earns a blanket suppression.
        /// </remarks>
        private static bool IsObjectMember(IMethodSymbol method)
            => method.Name is "ToString" or "GetHashCode" or "Equals" or "GetType";

        /// <summary>
        /// Enum members, which are const fields — the property/field walk deliberately skips consts, so an enum
        /// needs its own pass or it would come out with no members at all.
        /// </summary>
        private static IReadOnlyList<AIContextMember> ReadEnumMembers(INamedTypeSymbol symbol)
        {
            var members = new List<AIContextMember>();

            foreach (var field in symbol.GetMembers().OfType<IFieldSymbol>())
            {
                if (!field.IsConst || field.ConstantValue is null) continue;

                var member = new AIContextMember(
                    field.Name,
                    field.Type,
                    DisplayType(field.Type),
                    isMethod: false,
                    isCommand: false,
                    canRead: true,
                    canWrite: false,
                    isStatic: true,
                    hasVeloxProperty: false,
                    hasVeloxCommand: false,
                    isPromotedField: false,
                    hasSlotSelectors: false,
                    descriptions: ReadTexts(field),
                    slotSelectorNames: [],
                    parameters: [])
                {
                    IsField = true,
                    Ordinal = System.Convert.ToInt64(field.ConstantValue),
                    Symbol = field,
                };

                members.Add(member);
            }

            return members;
        }

        private static IReadOnlyList<AIContextMember> ReadMembers(INamedTypeSymbol symbol)
        {
            var members = new List<AIContextMember>();

            // MVVM 生成器产出的属性就在同一个编译里，所以 `symbol.GetMembers()` 里既有那个私有字段、
            // 又有它提升出来的公开属性。提升字段已经代表了这个成员 —— 不把生成的那份再收一遍，
            // 否则目录里每个 [VeloxProperty] 都会出现两次，顺序也会被生成的那一份带偏。
            var promotedNames = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var candidate in symbol.GetMembers().OfType<IFieldSymbol>())
            {
                if (!AIContextNaming.HasAttribute(candidate, VeloxPropertyAttributeName)) continue;

                var name = AIContextNaming.PromotedPropertyName(candidate.Name);
                if (name.Length > 0) promotedNames.Add(name);
            }

            foreach (var member in symbol.GetMembers())
            {
                if (member.IsImplicitlyDeclared) continue;
                if (member is IPropertySymbol skipped && promotedNames.Contains(skipped.Name)) continue;

                switch (member)
                {
                    case IPropertySymbol property when property.DeclaredAccessibility == Accessibility.Public && !property.IsIndexer:
                        members.Add(BuildProperty(property));
                        break;

                    // [VeloxProperty] 字段由 MVVM 生成器提升成属性，Agent 面看到的是那个属性，不是字段。
                    // 自己写的字段（public 且没标注）才按字段收录。
                    case IFieldSymbol field when AIContextNaming.HasAttribute(field, VeloxPropertyAttributeName):
                        var promoted = AIContextNaming.PromotedPropertyName(field.Name);
                        if (promoted.Length > 0) members.Add(BuildPromotedProperty(field, promoted));
                        break;

                    // 只带 [AgentContext] 的私有/受保护字段：会出现在描述表格里，但**没有生成属性**，
                    // 所以访问器够不着它 —— 描述-only，读写都标 false，否则生成的 `t.X` 编不过。
                    case IFieldSymbol field when field.DeclaredAccessibility != Accessibility.Public
                                                 && !field.IsConst
                                                 && field.AssociatedSymbol is null
                                                 && AIContextNaming.HasAttribute(field, AgentContextAttributeName):
                        var described = AIContextNaming.PromotedPropertyName(field.Name);
                        if (described.Length > 0) members.Add(BuildDescribedOnlyField(field, described));
                        break;

                    case IFieldSymbol field when field.DeclaredAccessibility == Accessibility.Public
                                                 && !field.IsConst
                                                 && field.AssociatedSymbol is null
                                                 && field.Name != "value__":
                        members.Add(BuildField(field));
                        break;

                    // 泛型方法跳过：自己的类型参数在生成的调用里没有绑定（`T byref0` 编不过），
                    // 而且 Agent 也没有办法给出类型实参。
                    case IMethodSymbol method when method.MethodKind == MethodKind.Ordinary
                                                    && !method.IsStatic
                                                    && !method.IsImplicitlyDeclared
                                                    && method.TypeParameters.Length == 0
                                                    && !IsObjectMember(method):
                        // [VeloxCommand] 提升出的命令属性是公开的，哪怕方法自己是 private ——
                        // 实现类里的命令体普遍写成 private，按方法可见性过滤会把整个命令面漏掉。
                        if (AIContextNaming.HasAttribute(method, VeloxCommandAttributeName))
                            members.Add(BuildPromotedCommand(method));

                        // 方法本身只有公开时才对 Agent 可调；私有方法只经由上面那个命令暴露。
                        if (method.DeclaredAccessibility == Accessibility.Public)
                            members.Add(BuildMethod(method));
                        break;
                }
            }

            return members;
        }

        private static AIContextMember BuildProperty(IPropertySymbol property)
        {
            var isCommand = IsCommandType(property.Type);

            // 命令的说明与参数类型以接口上的声明为准 —— 反射那条先扫接口，具体类同名的那个根本不会被读到。
            var contract = isCommand ? InterfaceCommandProperty(property.ContainingType, property.Name) : null;
            var annotations = (ISymbol?)contract ?? property;
            var commandParameter = ReadCommandParameterType(annotations);

            return new AIContextMember(
                property.Name,
                property.Type,
                TableType(property.Type),
                isMethod: false,
                isCommand: isCommand,
                canRead: property.GetMethod is not null && property.GetMethod.DeclaredAccessibility == Accessibility.Public,
                // init-only 访问器只能在对象初始化器里赋值，生成 `t.X = …` 会 CS8852，所以它不可写。
                canWrite: property.SetMethod is not null
                          && property.SetMethod.DeclaredAccessibility == Accessibility.Public
                          && !property.SetMethod.IsInitOnly,
                isStatic: property.IsStatic,
                hasVeloxProperty: HasAttribute(property, VeloxPropertyAttributeName),
                hasVeloxCommand: false,
                isPromotedField: false,
                hasSlotSelectors: HasAttribute(property, SlotSelectorsAttributeName),
                descriptions: ReadTexts(annotations),
                slotSelectorNames: ReadSlotSelectorNames(property),
                parameters: [])
            {
                Symbol = property,
                IsSlotEnumerator = IsSlotEnumeratorType(property.Type),
                IsSlotCollection = IsSlotCollectionType(property.Type),
                IsSingleSlot = IsSingleSlotType(property.Type),
                // 命令的参数类型标在属性上（接口那一路），方法那一路才标在方法上 —— 两边都要读。
                CommandParameterTypeName = commandParameter.Name,
                CommandParameterTypeSymbol = commandParameter.Symbol,
            };
        }

        /// <summary>
        /// The <c>ICommand</c> property an interface declares under <paramref name="name"/>, or null when the type
        /// implements no such contract.
        /// </summary>
        /// <remarks>
        /// A command's <c>[AgentContext]</c> and <c>[AgentCommandParameter]</c> belong on the interface — that is
        /// where the workflow runtime declares them, and the accessor's implementation is generated under a name
        /// this generator has to reproduce. So the interface is asked for the annotations, exactly as the
        /// reflective discoverer did when it scanned interfaces before the concrete type.
        /// </remarks>
        private static IPropertySymbol? InterfaceCommandProperty(INamedTypeSymbol type, string name)
        {
            // 接口自己的条目按名字读自己的属性，不能去 AllInterfaces 里找同名的 —— 那会把标注挪到别的契约上。
            if (type.TypeKind == TypeKind.Interface) return null;

            foreach (var contract in type.AllInterfaces)
            {
                foreach (var member in contract.GetMembers(name))
                {
                    if (member is not IPropertySymbol property || property.IsIndexer) continue;
                    if (IsCommandType(property.Type)) return property;
                }
            }

            return null;
        }

        /// <summary>Whether a member declared as <paramref name="type"/> is an <c>ICommand</c> property.</summary>
        private static bool IsCommandType(ITypeSymbol type)
        {
            if (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty) == "System.Windows.Input.ICommand")
                return true;

            return type is INamedTypeSymbol named && ImplementsInterface(named, "System.Windows.Input.ICommand");
        }

        /// <summary>
        /// Whether the type is a slot enumerator: <c>SlotEnumerator&lt;T&gt;</c>, the contract it implements, or
        /// anything else implementing that contract.
        /// </summary>
        private static bool IsSlotEnumeratorType(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol named) return false;
            if (IsSlotEnumeratorContract(named)) return true;

            return named.AllInterfaces.Any(IsSlotEnumeratorContract);
        }

        /// <summary>Whether the type is <c>SlotEnumerator&lt;T&gt;</c> or its interface, unresolved by argument.</summary>
        private static bool IsSlotEnumeratorContract(INamedTypeSymbol type)
        {
            var definition = type.OriginalDefinition;
            if (definition.ContainingNamespace?.ToDisplayString() != "VeloxDev.WorkflowSystem") return false;

            return definition.Name is "SlotEnumerator" or "IConditionalSlotProvider";
        }

        /// <summary>Whether the type is a collection the agent should treat as a group of slots.</summary>
        private static bool IsSlotCollectionType(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol named) return false;

            // 一个类型实参、类型本身可枚举、且实参是插槽 —— `ObservableCollection<TSlot>`、`List<TSlot>` …
            if (named.TypeArguments.Length == 1
                && named.AllInterfaces.Any(static i => i.SpecialType == SpecialType.System_Collections_IEnumerable)
                && IsSingleSlotType(named.TypeArguments[0]))
            {
                return true;
            }

            // 否则看它实现的那个 `ICollection<TSlot>`。
            foreach (var contract in named.AllInterfaces)
            {
                if (contract.OriginalDefinition.SpecialType != SpecialType.None) continue;
                if (contract.OriginalDefinition.Name != "ICollection" || contract.TypeArguments.Length != 1) continue;
                if (IsSingleSlotType(contract.TypeArguments[0])) return true;
            }

            return false;
        }

        /// <summary>Whether the type is a single workflow slot — the interface itself counts, as it does for <c>IsAssignableFrom</c>.</summary>
        private static bool IsSingleSlotType(ITypeSymbol type)
        {
            const string slotInterface = "VeloxDev.WorkflowSystem.IWorkflowSlotViewModel";

            if (type is not INamedTypeSymbol named) return false;

            return named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty) == slotInterface
                   || ImplementsInterface(named, slotInterface);
        }

        /// <summary>The type named by <c>[AgentCommandParameter]</c>, or null when the command takes none.</summary>
        /// <remarks>
        /// Both forms are returned: the accessor needs the symbol to write a <c>typeof</c> literal, the rendered
        /// tables need the name. An argument-free <c>[AgentCommandParameter]</c> declares "no parameter" and
        /// answers with neither.
        /// </remarks>
        private static (string? Name, ITypeSymbol? Symbol) ReadCommandParameterType(ISymbol symbol)
        {
            var attribute = symbol.GetAttributes().FirstOrDefault(a => AttributeName(a) == AgentCommandParameterAttributeName);
            if (attribute is null || attribute.ConstructorArguments.Length == 0) return (null, null);

            return attribute.ConstructorArguments[0].Value is ITypeSymbol type
                ? (DisplayType(type), type)
                : (null, null);
        }

        private static AIContextMember BuildField(IFieldSymbol field)
            => new(
                field.Name,
                field.Type,
                TableType(field.Type),
                isMethod: false,
                isCommand: false,
                canRead: true,
                canWrite: !field.IsReadOnly,
                isStatic: field.IsStatic,
                hasVeloxProperty: HasAttribute(field, VeloxPropertyAttributeName),
                hasVeloxCommand: false,
                isPromotedField: false,
                hasSlotSelectors: HasAttribute(field, SlotSelectorsAttributeName),
                descriptions: ReadTexts(field),
                slotSelectorNames: ReadSlotSelectorNames(field),
                parameters: [])
            { IsField = true, Symbol = field };

        /// <summary>
        /// A <c>[VeloxProperty]</c> field as the Agent meets it: the property the MVVM generator promotes it to.
        /// </summary>
        /// <remarks>
        /// Recorded under the promoted name because that is what the accessor has to read and write — the field
        /// itself is private, and a private member of another type is not reachable from generated code even
        /// inside the same assembly.
        /// </remarks>
        private static AIContextMember BuildPromotedProperty(IFieldSymbol field, string promotedName)
            => new(
                promotedName,
                field.Type,
                TableType(field.Type),
                isMethod: false,
                isCommand: false,
                canRead: true,
                canWrite: !field.IsReadOnly,
                isStatic: field.IsStatic,
                hasVeloxProperty: true,
                hasVeloxCommand: false,
                isPromotedField: true,
                hasSlotSelectors: AIContextNaming.HasAttribute(field, SlotSelectorsAttributeName),
                descriptions: ReadTexts(field),
                slotSelectorNames: ReadSlotSelectorNames(field),
                parameters: [])
            {
                Symbol = field,
                IsSlotEnumerator = IsSlotEnumeratorType(field.Type),
                IsSingleSlot = IsSingleSlotType(field.Type),
                IsSlotCollection = IsSlotCollectionType(field.Type),
            };

        /// <summary>
        /// A non-public field that carries only <c>[AgentContext]</c>.
        /// </summary>
        /// <remarks>
        /// Describable but not reachable: no generator promotes it to a property, so there is nothing for an
        /// accessor to read or write. Recorded so the rendered tables keep the row they print today.
        /// </remarks>
        private static AIContextMember BuildDescribedOnlyField(IFieldSymbol field, string describedName)
            => new(
                describedName,
                field.Type,
                TableType(field.Type),
                isMethod: false,
                isCommand: false,
                canRead: false,
                canWrite: false,
                isStatic: field.IsStatic,
                hasVeloxProperty: false,
                hasVeloxCommand: false,
                isPromotedField: false,
                hasSlotSelectors: false,
                descriptions: ReadTexts(field),
                slotSelectorNames: [],
                parameters: [])
            { IsField = true, Symbol = field };

        /// <summary>
        /// A <c>[VeloxCommand]</c> method as the Agent meets it: the command property the writer emits.
        /// </summary>
        /// <remarks>
        /// The declared type is the framework command interface rather than the writer's own generated type —
        /// this generator cannot see that type, but every command property implements this one.
        /// </remarks>
        private static AIContextMember BuildPromotedCommand(IMethodSymbol method)
        {
            // 命令属性由 CommandWriter 产出，它实现的那个接口属性才带着标注 —— 接口上写了就用接口的。
            var name = AIContextNaming.CommandPropertyName(method);
            var contract = InterfaceCommandProperty(method.ContainingType, name);
            var annotations = (ISymbol?)contract ?? method;
            var commandParameter = ReadCommandParameterType(annotations);

            return new AIContextMember(
                name,
                method.ReturnType,
                "VeloxDev.MVVM.IVeloxCommand",
                isMethod: false,
                isCommand: true,
                canRead: true,
                canWrite: false,
                isStatic: false,
                hasVeloxProperty: false,
                hasVeloxCommand: true,
                isPromotedField: false,
                hasSlotSelectors: false,
                descriptions: ReadTexts(annotations),
                slotSelectorNames: [],
                parameters: [])
            {
                Symbol = method,
                CommandParameterTypeName = commandParameter.Name,
                CommandParameterTypeSymbol = commandParameter.Symbol,
            };
        }

        private static AIContextMember BuildMethod(IMethodSymbol method)
        {
            var commandParameter = ReadCommandParameterType(method);

            return new AIContextMember(
                method.Name,
                method.ReturnType,
                TableType(method.ReturnType),
                isMethod: true,
                isCommand: false,
                canRead: false,
                canWrite: false,
                isStatic: false,
                hasVeloxProperty: false,
                hasVeloxCommand: HasAttribute(method, VeloxCommandAttributeName),
                isPromotedField: false,
                hasSlotSelectors: false,
                descriptions: ReadTexts(method),
                slotSelectorNames: [],
                parameters: [.. method.Parameters.Select(static p => new AIContextParameter(
                    p.Name,
                    p.Type,
                    TableType(p.Type),
                    p.IsOptional,
                    p.RefKind))])
            {
                ReturnsVoid = method.ReturnsVoid,
                Symbol = method,
                CommandParameterTypeName = commandParameter.Name,
                CommandParameterTypeSymbol = commandParameter.Symbol,
            };
        }

        private static IReadOnlyList<string> ReadSlotSelectorNames(ISymbol symbol)
        {
            var attribute = symbol.GetAttributes().FirstOrDefault(a => AttributeName(a) == SlotSelectorsAttributeName);
            if (attribute is null) return [];

            var names = new List<string>();

            foreach (var argument in attribute.ConstructorArguments)
            {
                if (argument.Kind != TypedConstantKind.Array) continue;

                foreach (var element in argument.Values)
                {
                    if (element.Value is string name)
                    {
                        names.Add(name);
                    }
                    else if (element.Value is ITypeSymbol type)
                    {
                        names.Add(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty));
                    }
                }
            }

            return names;
        }

        private static bool HasAttribute(ISymbol symbol, string metadataName)
            => symbol.GetAttributes().Any(a => AttributeName(a) == metadataName);

        private static string AttributeName(AttributeData attribute)
            => attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty)
               ?? string.Empty;

        /// <summary>The type as it should appear in a description — the form the collector prints today.</summary>
        internal static string DisplayType(ITypeSymbol type) => Analizer.DisplayFullTypeName(type);

        /// <summary>
        /// The type as a rendered table's first column spells it.
        /// </summary>
        /// <remarks>
        /// No nullable modifier: those tables print <c>Type.FullName</c>, which never carries <c>?</c>. The
        /// accessor's conversions never read this string — they work from the symbol — so dropping the
        /// annotation here costs nothing and is what makes the two renderers agree.
        /// </remarks>
        private static string TableType(ITypeSymbol type)
            => type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters));
    }
}
