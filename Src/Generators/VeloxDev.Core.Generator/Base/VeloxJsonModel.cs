using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;

namespace VeloxDev.Generators.Base
{
    /// <summary>
    /// What one assembly contributes to the archive serializer: a writer (and later a reader) for every type a
    /// document can contain.
    /// </summary>
    /// <remarks>
    /// Built by one walk over <c>compilation.Assembly</c> rather than through
    /// <see cref="Analizer.Filters.Targets"/>: a type takes part here because of what it <i>is</i> — a workflow
    /// component, or a type reachable from one — not because it carries a trigger attribute, and the reachable
    /// set has to be closed over member types in a second pass.
    /// </remarks>
    internal sealed class VeloxJsonAssembly
    {
        internal VeloxJsonAssembly(string assemblyName, IReadOnlyList<VeloxJsonType> types, IReadOnlyList<Diagnostic> notices)
        {
            AssemblyName = assemblyName;
            Types = types;
            Notices = notices;
        }

        internal string AssemblyName { get; }

        internal IReadOnlyList<VeloxJsonType> Types { get; }

        /// <summary>What the walk could not represent faithfully — reported rather than dropped in silence.</summary>
        internal IReadOnlyList<Diagnostic> Notices { get; }
    }

    /// <summary>How a member's value reaches the document.</summary>
    internal enum VeloxJsonMemberKind
    {
        /// <summary>A scalar: written by a dedicated primitive, never named.</summary>
        Scalar,

        /// <summary>An object: written through its own entry.</summary>
        Object,

        /// <summary>A sequence: written as an array, each element by its own entry.</summary>
        Collection,

        /// <summary>A map: written as an object, each value by its own entry.</summary>
        Dictionary,
    }

    /// <summary>One member of a serialized type.</summary>
    internal sealed class VeloxJsonMember
    {
        internal VeloxJsonMember(string name, ITypeSymbol declaredType, ITypeSymbol? elementType, VeloxJsonMemberKind kind)
        {
            Name = name;
            DeclaredType = declaredType;
            ElementType = elementType;
            Kind = kind;
        }

        /// <summary>The member's name in the document — the property name, never the backing field's.</summary>
        internal string Name { get; }

        /// <summary>The declared type, which is what decides whether a value needs its type name written.</summary>
        internal ITypeSymbol DeclaredType { get; }

        /// <summary>For a collection or a dictionary, the type each element or value is written as.</summary>
        internal ITypeSymbol? ElementType { get; }

        internal VeloxJsonMemberKind Kind { get; }

        /// <summary>
        /// For a dictionary, whether its keys are an interface. Such a map is written with its keys' reference
        /// ids as property names, which is a different shape from an ordinary map.
        /// </summary>
        internal bool InterfaceKeyed { get; set; }

        /// <summary>Whether the member is declared on the type itself rather than inherited.</summary>
        internal bool IsDeclaredHere { get; set; }
    }

    /// <summary>One serialized type.</summary>
    internal sealed class VeloxJsonType
    {
        internal VeloxJsonType(INamedTypeSymbol symbol, string fullName, string writtenName, IReadOnlyList<VeloxJsonMember> members)
        {
            Symbol = symbol;
            FullName = fullName;
            WrittenName = writtenName;
            Members = members;
        }

        internal INamedTypeSymbol Symbol { get; }

        /// <summary>The type's full name, as <c>Type.FullName</c> reports it.</summary>
        internal string FullName { get; }

        /// <summary>
        /// The name written into <c>$type</c> — the assembly-qualified name without version or culture, which is
        /// what existing archives carry.
        /// </summary>
        internal string WrittenName { get; }

        internal IReadOnlyList<VeloxJsonMember> Members { get; }

        /// <summary>The name of the generated writer class for this type.</summary>
        internal string WriterClassName { get; set; } = string.Empty;

        /// <summary>The name of the generated reader class for this type.</summary>
        internal string ReaderClassName { get; set; } = string.Empty;
    }

    /// <summary>Builds the serializer model for one assembly.</summary>
    internal static class VeloxJsonModelBuilder
    {
        private const string VeloxPropertyAttributeName = "VeloxDev.MVVM.VeloxPropertyAttribute";

        private static readonly string[] ComponentInterfaces =
        [
            "VeloxDev.WorkflowSystem.IWorkflowTreeViewModel",
            "VeloxDev.WorkflowSystem.IWorkflowNodeViewModel",
            "VeloxDev.WorkflowSystem.IWorkflowSlotViewModel",
            "VeloxDev.WorkflowSystem.IWorkflowLinkViewModel",
        ];

        /// <summary>
        /// Which component contract each <c>[WorkflowBuilder.*]</c> attribute shapes a type into.
        /// </summary>
        /// <remarks>
        /// The Workflow generator emits a component's members itself, so this one cannot read them off the class
        /// — it has to reproduce them. The interface is where that shape is written down, and the Workflow
        /// generator mirrors it too, so both read the same source rather than one copying the other.
        /// </remarks>
        private static readonly Dictionary<string, string> BuilderContracts = new(System.StringComparer.Ordinal)
        {
            ["TreeAttribute"] = "VeloxDev.WorkflowSystem.IWorkflowTreeViewModel",
            ["NodeAttribute"] = "VeloxDev.WorkflowSystem.IWorkflowNodeViewModel",
            ["SlotAttribute"] = "VeloxDev.WorkflowSystem.IWorkflowSlotViewModel",
            ["LinkAttribute"] = "VeloxDev.WorkflowSystem.IWorkflowLinkViewModel",
        };

        /// <summary>
        /// True when the compilation takes part in the archive format at all.
        /// </summary>
        /// <remarks>
        /// The marker is the property attribute rather than anything in this assembly: a project that does not
        /// reference <c>VeloxDev.MVVM</c> has no ViewModels to write.
        /// </remarks>
        internal static bool Applies(Compilation compilation)
            => compilation.GetTypeByMetadataName(VeloxPropertyAttributeName) is not null;

        internal static VeloxJsonAssembly? Build(Compilation compilation)
        {
            var contracts = ResolveContracts(compilation);
            var candidates = EnumerateTypes(compilation.Assembly.GlobalNamespace).ToList();
            var roots = candidates.Where(s => IsRoot(s, compilation.Assembly)).ToList();
            if (roots.Count == 0) return null;

            var included = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var queue = new Queue<INamedTypeSymbol>(roots);

            // 闭包：从根类型出发，沿可写成员的声明类型一路收下去。目录里没有的类型写不出去，
            // 所以这一步决定了「什么能进文档」。
            while (queue.Count > 0)
            {
                var symbol = queue.Dequeue();
                if (!included.Add(symbol)) continue;

                foreach (var member in ReadMembers(symbol, contracts))
                {
                    foreach (var reachable in Reachable(member))
                    {
                        // 声明类型是接口或抽象类时，实际写进文档的是某个具体类型，而它是由类型名派发过去的 ——
                        // 所以「谁实现了这个契约」也要收进来，否则运行期会找不到写它的条目。
                        if (reachable.TypeKind == TypeKind.Interface || reachable.IsAbstract)
                        {
                            foreach (var implementer in candidates)
                            {
                                if (included.Contains(implementer)) continue;
                                if (!IsWritableType(implementer, compilation.Assembly)) continue;
                                if (!MatchesContract(implementer, reachable)) continue;

                                queue.Enqueue(implementer);
                            }

                            continue;
                        }

                        if (included.Contains(reachable)) continue;
                        if (!IsWritableType(reachable, compilation.Assembly)) continue;

                        queue.Enqueue(reachable);
                    }
                }
            }

            var types = included
                .Select(s => BuildType(s, contracts))
                .Where(static t => t is not null)
                .Select(static t => t!)
                .OrderBy(static t => t.FullName, System.StringComparer.Ordinal)
                .ToList();

            if (types.Count == 0) return null;

            return new VeloxJsonAssembly(compilation.AssemblyName ?? "Assembly", types, []);
        }

        /// <summary>The component contracts a builder-shaped type draws its members from.</summary>
        private static IReadOnlyDictionary<string, INamedTypeSymbol> ResolveContracts(Compilation compilation)
        {
            var resolved = new Dictionary<string, INamedTypeSymbol>(System.StringComparer.Ordinal);

            foreach (var entry in BuilderContracts)
            {
                if (compilation.GetTypeByMetadataName(entry.Value) is { } contract)
                    resolved[entry.Key] = contract;
            }

            return resolved;
        }

        /// <summary>
        /// Whether a type is one the format is rooted at: a workflow component, or a type whose author asked for
        /// a serialized member.
        /// </summary>
        private static bool IsRoot(INamedTypeSymbol symbol, IAssemblySymbol assembly)
        {
            if (!IsWritableType(symbol, assembly)) return false;

            if (ComponentInterfaces.Any(contract => ImplementsInterface(symbol, contract))) return true;

            // 组件的接口是 Workflow 生成器加上去的，而生成器之间看不见彼此的产物 —— 所以这里认的是
            // 作者写下的那个特性，而不是最终会出现的接口。
            //
            // 按包含类型判而不是按名字前缀：`WorkflowBuilder.Slot<T>` 是泛型嵌套特性，`ToDisplayString`
            // 把嵌套类型渲染成 `.` 而不是元数据里的 `+`，前缀匹配永远匹配不上。
            foreach (var attribute in symbol.GetAttributes())
            {
                if (IsWorkflowBuilderAttribute(attribute.AttributeClass)) return true;
            }

            return symbol.GetMembers().OfType<IFieldSymbol>()
                .Any(f => AIContextNaming.HasAttribute(f, VeloxPropertyAttributeName));
        }

        /// <summary>The member types a member's value can be written as.</summary>
        private static IEnumerable<INamedTypeSymbol> Reachable(VeloxJsonMember member)
        {
            if (member.ElementType is INamedTypeSymbol element) yield return element;
            if (member.DeclaredType is INamedTypeSymbol declared) yield return declared;
        }

        /// <summary>
        /// Whether a type needs an entry of its own.
        /// </summary>
        /// <remarks>
        /// Framework containers are written by the runtime's own writers, which the generated registration points
        /// at, so they are not this assembly's business. An open generic cannot be written at all: the entry for a
        /// closed one is emitted where the closed type is met, not for the definition.
        /// </remarks>
        private static bool IsWritableType(INamedTypeSymbol symbol, IAssemblySymbol assembly)
        {
            if (symbol.TypeKind is not (TypeKind.Class or TypeKind.Struct)) return false;
            if (symbol.IsStatic || symbol.IsImplicitlyDeclared) return false;

            // 抽象类型没有实例可写：它的值总是某个具体类型，而那个类型有自己的条目。
            if (symbol.IsAbstract) return false;

            // 开放泛型生成不出来：`(SlotEnumerator<T>)value` 里的 T 没有绑定。
            //
            // 判据是**类型实参里还有类型参数**，不是 `TypeParameters.Length` —— 后者对封闭实例照样返回
            // 定义上的那些参数，用它会把 `SlotEnumerator<SlotDefaultViewModel>` 一起挡掉。
            for (var current = symbol; current is not null; current = current.ContainingType)
            {
                if (current.IsUnboundGenericType) return false;
                if (current.TypeArguments.Any(static argument => argument.TypeKind == TypeKind.TypeParameter)) return false;
            }

            if (!IsAccessible(symbol)) return false;

            if (SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, assembly)) return true;

            // 别的程序集里的**封闭**泛型：`SlotEnumerator<SlotDefaultViewModel>` 的定义在 Core，但只有见过
            // 这个实例的消费方才发得出它的条目 —— 声明它的那一侧永远不会知道有这么一个组合。
            // 门槛与旧的契约解析器一致：有公开无参构造、且不是框架容器（容器由运行时写成数组/对象）。
            return symbol.IsGenericType
                   && !IsNativeCollection(symbol.OriginalDefinition)
                   && HasPublicParameterlessConstructor(symbol);
        }

        /// <summary>Whether a type is one of the framework containers the serializer writes by shape.</summary>
        private static bool IsNativeCollection(INamedTypeSymbol definition)
        {
            var ns = definition.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            if (ns.StartsWith("System.Collections", System.StringComparison.Ordinal)) return true;
            if (ns.StartsWith("System.Linq", System.StringComparison.Ordinal)) return true;

            return definition.Name is "List" or "ObservableCollection" or "Dictionary" or "HashSet" or "Queue" or "Stack";
        }

        private static bool HasPublicParameterlessConstructor(INamedTypeSymbol symbol)
            => symbol.TypeKind == TypeKind.Struct
               || symbol.InstanceConstructors.Any(static c =>
                      c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);

        /// <summary>Whether generated code in this assembly can name the type — it and every type containing it.</summary>
        private static bool IsAccessible(INamedTypeSymbol symbol)
        {
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

        private static VeloxJsonType? BuildType(INamedTypeSymbol symbol, IReadOnlyDictionary<string, INamedTypeSymbol> contracts)
        {
            var members = ReadMembers(symbol, contracts).ToList();
            if (members.Count == 0) return null;

            return new VeloxJsonType(
                symbol,
                ReflectionFullName(symbol),
                WrittenName(symbol),
                members);
        }

        /// <summary>
        /// The members the format writes: public read/write properties, in the order the contract reports them —
        /// hand-written ones in declaration order first, then the properties the MVVM generator promotes from
        /// <c>[VeloxProperty]</c> fields, in field order.
        /// </summary>
        /// <remarks>
        /// The order is the whole reason this is not simply <c>GetMembers()</c>. Measured against the previous
        /// serializer: it writes the author's own properties first because they live in the author's file, and
        /// the promoted ones after because the compiler appends the generated partial.
        /// </remarks>
        private static IEnumerable<VeloxJsonMember> ReadMembers(
            INamedTypeSymbol symbol,
            IReadOnlyDictionary<string, INamedTypeSymbol> contracts)
        {
            var promoted = new HashSet<string>(System.StringComparer.Ordinal);
            var fields = new List<(IFieldSymbol Field, string Name)>();

            foreach (var member in symbol.GetMembers())
            {
                if (member is IFieldSymbol field && AIContextNaming.HasAttribute(field, VeloxPropertyAttributeName))
                {
                    var name = AIContextNaming.PromotedPropertyName(field.Name);
                    if (name.Length == 0) continue;

                    promoted.Add(name);
                    fields.Add((field, name));
                }
            }

            var emitted = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var member in symbol.GetMembers())
            {
                if (member is not IPropertySymbol property) continue;
                if (property.IsIndexer || property.IsStatic) continue;
                if (promoted.Contains(property.Name)) continue;
                if (property.SetMethod is not { DeclaredAccessibility: Accessibility.Public }) continue;
                if (!emitted.Add(property.Name)) continue;

                yield return BuildMember(property.Name, property.Type);
            }

            foreach (var (field, name) in fields)
            {
                if (!emitted.Add(name)) continue;
                yield return BuildMember(name, field.Type);
            }

            // 组件的成员由 Workflow 生成器写出，本生成器看不见 —— 按作者写下的 [WorkflowBuilder.*]
            // 认到契约接口，再把接口上的可写属性按声明顺序补上。生成器之间看不见彼此，所以两边都照
            // 接口这条同一份来源走，而不是互相抄。
            foreach (var member in ContractMembers(symbol, contracts))
            {
                if (!emitted.Add(member.Name)) continue;
                yield return member;
            }

            // 继承来的成员排在后面：反射报告的是「自己的属性在前、基类的在后」，派生类型隐藏同名成员时
            // 只留最派生那一个。每一层都用同一条规则 —— 先它自己写的属性，再它提升出来的字段。
            for (var baseType = symbol.BaseType; baseType is not null; baseType = baseType.BaseType)
            {
                if (baseType.SpecialType == SpecialType.System_Object) break;

                foreach (var member in baseType.GetMembers())
                {
                    if (member is not IPropertySymbol property) continue;
                    if (property.IsIndexer || property.IsStatic) continue;
                    if (property.SetMethod is not { DeclaredAccessibility: Accessibility.Public }) continue;
                    if (!emitted.Add(property.Name)) continue;

                    yield return BuildMember(property.Name, property.Type);
                }

                foreach (var member in baseType.GetMembers())
                {
                    if (member is not IFieldSymbol field) continue;
                    if (!AIContextNaming.HasAttribute(field, VeloxPropertyAttributeName)) continue;

                    var name = AIContextNaming.PromotedPropertyName(field.Name);
                    if (name.Length == 0 || !emitted.Add(name)) continue;

                    yield return BuildMember(name, field.Type);
                }
            }
        }

        /// <summary>The writable properties a component contract contributes to a builder-shaped type.</summary>
        private static IEnumerable<VeloxJsonMember> ContractMembers(
            INamedTypeSymbol symbol,
            IReadOnlyDictionary<string, INamedTypeSymbol> contracts)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                if (attribute.AttributeClass is not { } attributeClass) continue;
                if (!IsWorkflowBuilder(attributeClass.ContainingType)) continue;
                if (!contracts.TryGetValue(attributeClass.Name, out var contract)) continue;

                for (var current = contract; current is not null; current = current.BaseType)
                {
                    foreach (var member in current.GetMembers())
                    {
                        if (member is not IPropertySymbol property) continue;
                        if (property.IsIndexer || property.IsStatic) continue;
                        if (property.SetMethod is not { DeclaredAccessibility: Accessibility.Public }) continue;

                        yield return BuildMember(property.Name, property.Type);
                    }
                }
            }
        }

        private static VeloxJsonMember BuildMember(string name, ITypeSymbol declaredType)
        {
            var (kind, element, interfaceKeyed) = Classify(declaredType);
            return new VeloxJsonMember(name, declaredType, element, kind)
            {
                IsDeclaredHere = true,
                InterfaceKeyed = interfaceKeyed,
            };
        }

        /// <summary>
        /// Decides how a declared type reaches the document — the four cases the format knows.
        /// </summary>
        private static (VeloxJsonMemberKind Kind, ITypeSymbol? Element, bool InterfaceKeyed) Classify(ITypeSymbol type)
        {
            if (IsScalar(type)) return (VeloxJsonMemberKind.Scalar, null, false);

            if (type is INamedTypeSymbol named && named.TypeArguments.Length == 1)
            {
                var definition = named.OriginalDefinition;
                if (definition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
                    || IsSequenceType(definition))
                {
                    return (VeloxJsonMemberKind.Collection, named.TypeArguments[0], false);
                }
            }

            if (type is INamedTypeSymbol map && map.TypeArguments.Length == 2 && IsMapType(map.OriginalDefinition))
                return (VeloxJsonMemberKind.Dictionary, map.TypeArguments[1], map.TypeArguments[0].TypeKind == TypeKind.Interface);

            return (VeloxJsonMemberKind.Object, null, false);
        }

        /// <summary>Whether a type is one of the scalars the format writes with a dedicated primitive.</summary>
        private static bool IsScalar(ITypeSymbol type)
            => type.SpecialType switch
            {
                SpecialType.System_String or SpecialType.System_Int32 or SpecialType.System_Int64
                    or SpecialType.System_Double or SpecialType.System_Single or SpecialType.System_Decimal
                    or SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_Int16
                    or SpecialType.System_Char or SpecialType.System_Object => true,
                _ => type.TypeKind == TypeKind.Enum
                     || type.ToDisplayString() == "System.Guid"
                     || type.ToDisplayString() == "System.DateTime"
                     || type.ToDisplayString() == "System.TimeSpan",
            };

        private static bool IsSequenceType(INamedTypeSymbol definition)
            => definition.Name is "List" or "ObservableCollection" or "IList" or "ICollection"
                    or "IReadOnlyList" or "IReadOnlyCollection" or "HashSet" or "Queue" or "Stack"
               && definition.ContainingNamespace?.ToDisplayString().StartsWith("System.Collections") == true;

        private static bool IsMapType(INamedTypeSymbol definition)
            => definition.Name is "Dictionary" or "IDictionary" or "IReadOnlyDictionary"
               && definition.ContainingNamespace?.ToDisplayString().StartsWith("System.Collections") == true;

        /// <summary>
        /// Whether a type is written as the contract.
        /// </summary>
        /// <remarks>
        /// A component's interfaces are emitted by the Workflow generator, which this one cannot see, so the
        /// author's <c>[WorkflowBuilder.*]</c> attribute stands in for them.
        /// </remarks>
        private static bool MatchesContract(INamedTypeSymbol symbol, INamedTypeSymbol contract)
        {
            if (Implements(symbol, contract)) return true;

            var attribute = contract.Name switch
            {
                "IWorkflowTreeViewModel" => "TreeAttribute",
                "IWorkflowNodeViewModel" => "NodeAttribute",
                "IWorkflowSlotViewModel" => "SlotAttribute",
                "IWorkflowLinkViewModel" => "LinkAttribute",
                _ => null,
            };

            if (attribute is null) return false;

            return symbol.GetAttributes().Any(a =>
                a.AttributeClass is { } attributeClass
                && attributeClass.Name == attribute
                && IsWorkflowBuilder(attributeClass.ContainingType));
        }

        /// <summary>Whether a type is one of the workflow builder's component attributes.</summary>
        private static bool IsWorkflowBuilderAttribute(INamedTypeSymbol? attributeClass)
            => attributeClass is not null && IsWorkflowBuilder(attributeClass.ContainingType);

        private static bool IsWorkflowBuilder(INamedTypeSymbol? containingType)
            => containingType?.Name == "WorkflowBuilder"
               && containingType.ContainingNamespace?.ToDisplayString() == "VeloxDev.WorkflowSystem";

        /// <summary>Whether a type is the contract itself, implements it, or derives from it.</summary>
        private static bool Implements(INamedTypeSymbol symbol, INamedTypeSymbol contract)
        {
            if (SymbolEqualityComparer.Default.Equals(symbol, contract)) return true;

            if (symbol.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, contract))) return true;

            for (var baseType = symbol.BaseType; baseType is not null; baseType = baseType.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(baseType, contract)) return true;
            }

            return false;
        }

        private static bool ImplementsInterface(INamedTypeSymbol symbol, string metadataName)
            => symbol.AllInterfaces.Any(i =>
                i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                 .Replace("global::", string.Empty) == metadataName);

        /// <summary>The type's name in the shape <c>Type.FullName</c> reports.</summary>
        private static string ReflectionFullName(INamedTypeSymbol symbol)
        {
            var name = symbol.Name;

            if (symbol.ContainingType is not null)
                return ReflectionFullName(symbol.ContainingType) + "+" + name;

            var ns = symbol.ContainingNamespace;
            return ns is null || ns.IsGlobalNamespace ? name : ns.ToDisplayString() + "." + name;
        }

        /// <summary>
        /// The name written into <c>$type</c>: <c>Namespace.Type, AssemblyName</c>, the form existing archives
        /// carry.
        /// </summary>
        private static string WrittenName(INamedTypeSymbol symbol)
            => ReflectionFullName(symbol) + ", " + symbol.ContainingAssembly?.Name;

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
    }
}
