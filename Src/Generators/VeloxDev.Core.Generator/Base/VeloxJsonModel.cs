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
        internal VeloxJsonAssembly(
            string assemblyName,
            IReadOnlyList<VeloxJsonType> types,
            IReadOnlyList<VeloxJsonContainer> containers)
        {
            AssemblyName = assemblyName;
            Types = types;
            Containers = containers;
        }

        internal string AssemblyName { get; }

        internal IReadOnlyList<VeloxJsonType> Types { get; }

        // 嵌在另一个容器里的容器，每个都要登记一句构造方式。
        // 成员自己那一层容器由它的生成 reader 就地填；再深一层就没有「就地」了，值得先被造出来，
        // 而按 Type 造一个泛型容器只能靠反射 —— 所以由见过这个关闭组合的那一方提供。
        internal IReadOnlyList<VeloxJsonContainer> Containers { get; }
    }

    // 文档里嵌在别的容器里的一个容器。
    internal sealed class VeloxJsonContainer
    {
        internal VeloxJsonContainer(INamedTypeSymbol symbol, VeloxJsonMemberKind kind, ITypeSymbol? keyType, ITypeSymbol elementType, bool interfaceKeys)
        {
            Symbol = symbol;
            Kind = kind;
            KeyType = keyType;
            ElementType = elementType;
            InterfaceKeys = interfaceKeys;
        }

        // 声明类型本身：Dictionary<K, V>、List<T> 这种。
        internal INamedTypeSymbol Symbol { get; }

        // 字典还是序列，决定读的时候走 ReadMap 还是 ReadArray。
        internal VeloxJsonMemberKind Kind { get; }

        // 字典的键类型；序列为 null。
        internal ITypeSymbol? KeyType { get; }

        // 每个值 / 元素按哪个类型读。
        internal ITypeSymbol ElementType { get; }

        internal bool InterfaceKeys { get; }
    }

    /// <summary>
    /// <c>VeloxDev.Serialization.ArchiveOptions</c>, mirrored.
    /// </summary>
    /// <remarks>
    /// A source generator cannot reference the assembly it generates for, so the values are repeated here. This is
    /// the one place in this file where a number has to be kept in step by hand: <c>[Archive]</c>'s first argument
    /// arrives as its underlying integer, and these are what those integers mean.
    /// </remarks>
    [System.Flags]
    internal enum ArchiveFlags
    {
        None = 0,
        KeepProperty = 1,
        KeepField = 2,
        IgnoreField = 4,
        ReName = 8,
    }

    /// <summary>When a member is written, as decided by <c>[JsonIgnore(Condition = …)]</c>.</summary>
    /// <remarks>
    /// <c>WhenWritingNull</c> is already resolved against the member's type here — a value type that cannot be
    /// null has no null to test, so it collapses to <see cref="Always"/> and the writer emits no guard at all.
    /// </remarks>
    internal enum VeloxJsonWriteCondition
    {
        /// <summary>Written whenever the type is written.</summary>
        Always,

        /// <summary>Written only when the member is not null.</summary>
        WhenNotNull,

        /// <summary>Written only when the member differs from its default value.</summary>
        WhenNotDefault,
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
            DocumentName = name;
            DeclaredType = declaredType;
            ElementType = elementType;
            Kind = kind;
        }

        /// <summary>
        /// The member's CLR name — what generated code reaches it through (<c>t.…</c>). For a
        /// <c>[VeloxProperty]</c> field this is the promoted property's name, never the backing field's.
        /// </summary>
        internal string Name { get; }

        /// <summary>
        /// The name this member carries in the document. Differs from <see cref="Name"/> only where
        /// <see cref="ArchiveFlags.ReName"/> moved it.
        /// </summary>
        internal string DocumentName { get; set; } = string.Empty;

        /// <summary>
        /// Whether the member is written but never read back — a computed property, which has no setter for the
        /// reader to assign through. The generated reader leaves no branch for such a member, so the document's
        /// value is stepped over as an unknown one.
        /// </summary>
        internal bool WriteOnly { get; set; }

        /// <summary>
        /// When the member is written. Anything but <see cref="VeloxJsonWriteCondition.Always"/> makes the
        /// generated writer guard the member, so the document can be missing it — which the reader already
        /// tolerates, since an absent member is the same thing as one it has never heard of.
        /// </summary>
        internal VeloxJsonWriteCondition WriteCondition { get; set; }

        /// <summary>
        /// Whether the document has to carry this member — C#'s <c>required</c>, or <c>[JsonRequired]</c>.
        /// The generated reader refuses a document that is missing it.
        /// </summary>
        internal bool IsRequired { get; set; }

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

    /// <summary>One serialization callback the generated code has to call.</summary>
    internal sealed class VeloxJsonHook
    {
        internal VeloxJsonHook(string name, bool takesContext)
        {
            Name = name;
            TakesContext = takesContext;
        }

        /// <summary>The method's name on the instance the generated code calls it on.</summary>
        internal string Name { get; }

        /// <summary>Whether it takes a <c>StreamingContext</c>, the shape the BCL attributes document.</summary>
        internal bool TakesContext { get; }
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

        /// <summary>
        /// Every member the type requires, its own and its bases'. Kept apart from <see cref="Members"/> because
        /// the generated factory has to set all of them — C# will not let a required member be left unassigned in
        /// an object initializer, whether or not the member takes part in the document.
        /// </summary>
        internal IReadOnlyList<string> RequiredMembers { get; set; } = [];

        /// <summary>The callbacks to run around writing, base type first. Empty when the type has none.</summary>
        internal IReadOnlyList<VeloxJsonHook> Serializing { get; set; } = [];

        /// <summary>The callbacks to run after writing, base type first.</summary>
        internal IReadOnlyList<VeloxJsonHook> Serialized { get; set; } = [];

        /// <summary>The callbacks to run before reading, base type first.</summary>
        internal IReadOnlyList<VeloxJsonHook> Deserializing { get; set; } = [];

        /// <summary>The callbacks to run after every member is read, base type first.</summary>
        internal IReadOnlyList<VeloxJsonHook> Deserialized { get; set; } = [];
    }

    /// <summary>Builds the serializer model for one assembly.</summary>
    internal static class VeloxJsonModelBuilder
    {
        private const string VeloxPropertyAttributeName = "VeloxDev.MVVM.VeloxPropertyAttribute";
        private const string ArchivableAttributeName = "VeloxDev.Serialization.ArchivableAttribute";
        private const string ArchiveAttributeName = "VeloxDev.Serialization.ArchiveAttribute";

        // 与钩子同一条口径：认识 .NET 自带的那一个，使用方就不必为了同一件事把代码改一遍。
        private const string JsonIgnoreAttributeName = "System.Text.Json.Serialization.JsonIgnoreAttribute";

        // 「这个成员必须在文档里」。C# 的 required 关键字走 RequiredMembers（编译器 API），STJ 用这个特性。
        private const string JsonRequiredAttributeName = "System.Text.Json.Serialization.JsonRequiredAttribute";

        // 钩子就是 BCL 那四个。生成器认它们，而不是另立一套自己名字的特性 —— 这些特性本来就写在这些
        // 方法上，只是此前生成器不看它们，于是成了死的装饰。
        private const string OnSerializingAttributeName = "System.Runtime.Serialization.OnSerializingAttribute";
        private const string OnSerializedAttributeName = "System.Runtime.Serialization.OnSerializedAttribute";
        private const string OnDeserializingAttributeName = "System.Runtime.Serialization.OnDeserializingAttribute";
        private const string OnDeserializedAttributeName = "System.Runtime.Serialization.OnDeserializedAttribute";

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

        /// <summary>
        /// Builds one assembly's serializer model, collecting the diagnostics it trips over into
        /// <paramref name="notices"/>.
        /// </summary>
        /// <remarks>
        /// The caller owns <paramref name="notices"/> rather than reading them off the result, because the result
        /// is <see langword="null"/> when the assembly contributes nothing — and a declaration the generator
        /// refused is exactly the case where nothing gets contributed. Keeping the notices inside the result would
        /// drop them at the one moment they matter most.
        /// </remarks>
        /// <param name="compilation">The assembly being compiled.</param>
        /// <param name="notices">Collects what the walk could not represent faithfully.</param>
        /// <returns>The model, or <see langword="null"/> when there is nothing to generate.</returns>
        internal static VeloxJsonAssembly? Build(Compilation compilation, List<Diagnostic> notices)
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

                // 声明里点名的额外根。放在循环里而不是只处理初始 roots —— 被点名的类型自己也可能再点名，
                // 而它是不是根不影响它能不能点名。重复的由 included 去重，不需要另一张表。
                foreach (var (named, site) in ReadAdditionalRoots(symbol))
                {
                    if (included.Contains(named)) continue;

                    if (!IsWritableType(named, compilation.Assembly))
                    {
                        // 发不出条目就报错而不是丢掉：丢掉的话要等到运行期 MissingWriter 才显形，
                        // 那时已经离能改的那一行很远了。
                        notices.Add(Diagnostic.Create(
                            Diagnostics.UnsupportedArchivableRoot,
                            site,
                            named.ToDisplayString(),
                            symbol.ToDisplayString(),
                            WhyNotWritable(named)));
                        continue;
                    }

                    queue.Enqueue(named);
                }

                var members = ReadMembers(symbol, contracts, compilation.Assembly, notices).ToList();
                ReportUnsatisfiableRequired(symbol, members, notices);

                foreach (var member in members)
                {
                    ReportNestedArray(member, notices);

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
                .Select(s => BuildType(s, contracts, notices, compilation.Assembly))
                .Where(static t => t is not null)
                .Select(static t => t!)
                .OrderBy(static t => t.FullName, System.StringComparer.Ordinal)
                .ToList();

            if (types.Count == 0) return null;

            return new VeloxJsonAssembly(compilation.AssemblyName ?? "Assembly", types, CollectNestedContainers(types));
        }

        // 收出所有嵌在别的容器里的容器，一直收到嵌套见底。
        // 起点是每个成员的元素 / 值类型，绝不是成员自己的类型 —— 外面那一层由成员的生成 reader 就地填。
        private static IReadOnlyList<VeloxJsonContainer> CollectNestedContainers(IReadOnlyList<VeloxJsonType> types)
        {
            var containers = new List<VeloxJsonContainer>();
            var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var queue = new Queue<INamedTypeSymbol>();

            foreach (var type in types)
                foreach (var member in type.Members)
                    if (member.ElementType is INamedTypeSymbol element) queue.Enqueue(element);

            while (queue.Count > 0)
            {
                var candidate = queue.Dequeue();
                var (kind, element, interfaceKeyed) = Classify(candidate);

                // 不是容器就到此为止；读不回来的也不登记 —— 声明那一层本来就跳过了它。
                if (kind is not (VeloxJsonMemberKind.Dictionary or VeloxJsonMemberKind.Collection)) continue;
                if (!IsContainerReadable(candidate, kind)) continue;
                if (!seen.Add(candidate)) continue;

                containers.Add(new VeloxJsonContainer(
                    candidate,
                    kind,
                    kind == VeloxJsonMemberKind.Dictionary ? candidate.TypeArguments[0] : null,
                    element!,
                    interfaceKeyed));

                if (element is INamedTypeSymbol inner) queue.Enqueue(inner);
            }

            return containers
                .OrderBy(static c => c.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), System.StringComparer.Ordinal)
                .ToList();
        }

        // 嵌容器读不读得回来 —— 生成器造得出的实例，得真的装得进文档声明的那个类型。
        // 字典一律可以（Dictionary<K,V> 满足分类器认识的每一种字典）；序列只在 List<T> 能顶替它时才行，
        // 因为读进来的集合是按 IList 追加的。
        private static bool IsContainerReadable(INamedTypeSymbol symbol, VeloxJsonMemberKind kind)
        {
            if (kind == VeloxJsonMemberKind.Dictionary) return true;

            if (symbol.TypeKind == TypeKind.Interface)
            {
                // List<T> 满足这五个泛型接口，正好是 IsSequenceType 认的那几个接口名。
                var definition = symbol.OriginalDefinition;
                return definition.ContainingNamespace?.ToDisplayString().StartsWith("System.Collections") == true
                       && definition.Name is "IList" or "ICollection" or "IEnumerable"
                           or "IReadOnlyList" or "IReadOnlyCollection";
            }

            return HasPublicParameterlessConstructor(symbol)
                   && ImplementsInterface(symbol, "System.Collections.IList");
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

            // 不是 ViewModel 的普通文档类型靠一个特性自报家门 —— 检查点就是这种。
            if (AIContextNaming.HasAttribute(symbol, ArchivableAttributeName)) return true;

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
        /// The types an <c>[Archivable(typeof(…))]</c> declaration names, each paired with the site to blame when
        /// the generator cannot honour it.
        /// </summary>
        private static IEnumerable<(INamedTypeSymbol Named, Location? Site)> ReadAdditionalRoots(INamedTypeSymbol symbol)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        .Replace("global::", string.Empty) != ArchivableAttributeName) continue;
                if (attribute.ConstructorArguments.Length == 0) continue;

                // 位置报在声明处，不报在被点名类型的声明处 —— 那是作者能改的那一行。
                var site = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();

                // `params Type[]` 收成一个数组实参；`[Archivable]` 不带参数时是一个空数组。
                foreach (var argument in attribute.ConstructorArguments[0].Values)
                {
                    if (argument.Value is INamedTypeSymbol named) yield return (named, site);
                }
            }
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

            if (IsOpenGeneric(symbol)) return false;

            if (!IsAccessible(symbol)) return false;

            if (SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, assembly)) return true;

            // 别的程序集里的**封闭**泛型：`SlotEnumerator<SlotDefaultViewModel>` 的定义在 Core，但只有见过
            // 这个实例的消费方才发得出它的条目 —— 声明它的那一侧永远不会知道有这么一个组合。
            // 门槛与旧的契约解析器一致：有公开无参构造、且不是框架容器（容器由运行时写成数组/对象）。
            return symbol.IsGenericType
                   && !IsNativeCollection(symbol.OriginalDefinition)
                   && HasPublicParameterlessConstructor(symbol);
        }

        /// <summary>
        /// Whether the type is generic in a way the generator cannot write, because some type argument is still
        /// unbound.
        /// </summary>
        /// <remarks>
        /// 判据是**类型实参里还有类型参数**，不是 <c>TypeParameters.Length</c> —— 后者对封闭实例照样返回定义上的
        /// 那些参数，用它会把 <c>SlotEnumerator&lt;SlotDefaultViewModel&gt;</c> 一起挡掉。
        /// </remarks>
        private static bool IsOpenGeneric(INamedTypeSymbol symbol)
        {
            for (var current = symbol; current is not null; current = current.ContainingType)
            {
                if (current.IsUnboundGenericType) return true;
                if (current.TypeArguments.Any(static argument => argument.TypeKind == TypeKind.TypeParameter)) return true;
            }

            return false;
        }

        /// <summary>
        /// Why <see cref="IsWritableType"/> refused a type, in words an author can act on.
        /// </summary>
        /// <remarks>
        /// Same checks in the same order, kept next to it so a new rule added there gets a sentence here. The
        /// error it feeds is worth the duplication: without it the author is told a type cannot take part but not
        /// which of six reasons applies.
        /// </remarks>
        private static string WhyNotWritable(INamedTypeSymbol symbol)
        {
            if (symbol.TypeKind is not (TypeKind.Class or TypeKind.Struct)) return "it is not a class or a struct";
            if (symbol.IsStatic) return "it is static";
            if (symbol.IsImplicitlyDeclared) return "it is compiler-generated";
            if (symbol.IsAbstract) return "it is abstract, so there is no instance to write";
            if (IsOpenGeneric(symbol)) return "it is an open generic type, so its type arguments cannot be bound";
            if (!IsAccessible(symbol)) return "generated code in this assembly cannot name it";

            return "it is declared in another assembly and is not a closed generic this assembly has seen";
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

        private static VeloxJsonType? BuildType(
            INamedTypeSymbol symbol,
            IReadOnlyDictionary<string, INamedTypeSymbol> contracts,
            List<Diagnostic> notices,
            IAssemblySymbol assembly)
        {
            // notices 传 null：闭包那一趟已经为每个类型报过一次成员级的诊断，这里再报就是同一个毛病说两遍。
            var members = ReadMembers(symbol, contracts, assembly, notices: null).ToList();
            if (members.Count == 0) return null;

            return new VeloxJsonType(
                symbol,
                ReflectionFullName(symbol),
                WrittenName(symbol),
                members)
            {
                RequiredMembers = RequiredMembers.NamesOf(symbol),
                Serializing = ReadHooks(symbol, OnSerializingAttributeName, notices, assembly),
                Serialized = ReadHooks(symbol, OnSerializedAttributeName, notices, assembly),
                Deserializing = ReadHooks(symbol, OnDeserializingAttributeName, notices, assembly),
                Deserialized = ReadHooks(symbol, OnDeserializedAttributeName, notices, assembly),
            };
        }

        /// <summary>
        /// The callback methods one hook attribute names along a type's base chain, base type first.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The attributes are <c>Inherited = false</c>, so a base type's callback is only found by walking the
        /// chain — and the BCL calls base callbacks before the derived ones, which is the order returned here.
        /// </para>
        /// <para>
        /// A callback the generated code cannot reach is an error rather than a silent omission: the callbacks
        /// exist to change what a document holds (<c>Anchor</c> expands a collapsed transient in
        /// <c>[OnSerializing]</c>), so dropping one would move the bytes with no other symptom.
        /// </para>
        /// </remarks>
        private static IReadOnlyList<VeloxJsonHook> ReadHooks(
            INamedTypeSymbol symbol,
            string attributeName,
            List<Diagnostic> notices,
            IAssemblySymbol assembly)
        {
            var hooks = new List<VeloxJsonHook>();
            var chain = new Stack<INamedTypeSymbol>();

            for (var current = symbol; current is not null; current = current.BaseType)
            {
                if (current.SpecialType == SpecialType.System_Object) break;
                chain.Push(current);
            }

            foreach (var level in chain)
            {
                var found = level.GetMembers().OfType<IMethodSymbol>()
                    .Where(static method => !method.IsStatic && method.ReturnsVoid)
                    .Where(method => AIContextNaming.HasAttribute(method, attributeName))
                    .ToList();


                if (found.Count == 0) continue;

                if (found.Count > 1)
                {
                    notices.Add(Diagnostic.Create(
                        Diagnostics.AmbiguousSerializationHook,
                        found[1].Locations.FirstOrDefault(),
                        attributeName,
                        level.ToDisplayString()));
                }

                var hook = found[0];

                if (!IsReachableFromGeneratedCode(hook, assembly))
                {
                    notices.Add(Diagnostic.Create(
                        Diagnostics.UnreachableSerializationHook,
                        hook.Locations.FirstOrDefault(),
                        hook.Name,
                        level.ToDisplayString(),
                        SymbolEqualityComparer.Default.Equals(hook.ContainingAssembly, assembly)
                            ? "generated code sits in another namespace of the same assembly, so it reaches internal methods but not private or protected ones"
                            : "the generated reader and writer are emitted into the consuming assembly, so a hook on a type declared elsewhere has to be public"));
                    continue;
                }

                if (hook.Parameters.Length > 1
                    || (hook.Parameters.Length == 1
                        && hook.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                            != "global::System.Runtime.Serialization.StreamingContext"))
                {
                    notices.Add(Diagnostic.Create(
                        Diagnostics.UnreachableSerializationHook,
                        hook.Locations.FirstOrDefault(),
                        hook.Name,
                        level.ToDisplayString(),
                        "it must take no parameter, or a single System.Runtime.Serialization.StreamingContext"));
                    continue;
                }

                hooks.Add(new VeloxJsonHook(hook.Name, hook.Parameters.Length == 1));
            }

            return hooks;
        }

        /// <summary>
        /// Whether generated code in <paramref name="assembly"/> can name the method.
        /// </summary>
        /// <remarks>
        /// <c>internal</c> only reaches as far as its own assembly, and the generated reader and writer are
        /// emitted into the assembly being compiled — which for a type reached as another assembly's closed
        /// generic is <b>not</b> the one that declares it. A type serialized from elsewhere therefore needs a
        /// <c>public</c> hook. That is not a style preference: reference assemblies drop non-public members, so an
        /// <c>internal</c> hook on a foreign type cannot even be seen here, let alone called.
        /// </remarks>
        private static bool IsReachableFromGeneratedCode(ISymbol member, IAssemblySymbol assembly)
            => member.DeclaredAccessibility == Accessibility.Public
               || (SymbolEqualityComparer.Default.Equals(member.ContainingAssembly, assembly)
                   && member.DeclaredAccessibility is Accessibility.Internal or Accessibility.ProtectedOrInternal);

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
            IReadOnlyDictionary<string, INamedTypeSymbol> contracts,
            IAssemblySymbol assembly,
            List<Diagnostic>? notices)
        {
            var promoted = new HashSet<string>(System.StringComparer.Ordinal);
            var fields = new List<(IFieldSymbol Field, string Name, string DocumentName, VeloxJsonWriteCondition Condition)>();

            foreach (var member in symbol.GetMembers())
            {
                if (member is not IFieldSymbol field) continue;

                var (options, renamed) = ReadArchive(field, notices);
                var condition = ReadWriteCondition(field, field.Type, notices);

                // 三者说的是同一件事的三种说法：`IgnoreField`、`[JsonIgnore]`、`[JsonIgnore(Condition = Always)]`。
                // 任一命中就整个排除 —— 排除掉的东西不会回到文档里。
                if (condition is null || (options & ArchiveFlags.IgnoreField) != 0) continue;

                if (AIContextNaming.HasAttribute(field, VeloxPropertyAttributeName))
                {
                    // 成员是提升出来的那个属性，生成代码写的是它而不是字段 —— 所以字段的可访问性在这里无关紧要，
                    // 只有下面那条「直接读字段」的路才要求字段自己够得着。
                    if ((options & ArchiveFlags.KeepField) != 0) ReportFieldConflict(field, notices);

                    var name = AIContextNaming.PromotedPropertyName(field.Name);
                    if (name.Length == 0) continue;

                    promoted.Add(name);
                    fields.Add((field, name, renamed ?? name, condition.Value));
                    continue;
                }

                // 普通字段默认不进文档；KeepField 或 ReName 才把它拉进来。
                if ((options & (ArchiveFlags.KeepField | ArchiveFlags.ReName)) == 0) continue;

                if (!CanBeMarked(field, options, assembly, notices)) continue;

                if (HasCorrespondingProperty(symbol, field))
                {
                    ReportFieldConflict(field, notices);
                    continue;
                }

                fields.Add((field, field.Name, renamed ?? field.Name, condition.Value));
            }

            var emitted = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var member in symbol.GetMembers())
            {
                if (member is not IPropertySymbol property) continue;
                if (property.IsIndexer || property.IsStatic) continue;
                if (promoted.Contains(property.Name)) continue;

                var (options, renamed) = ReadArchive(property, notices);
                if (!CanBeMarked(property, options, assembly, notices)) continue;

                // 没有 public setter 的计算属性默认丢弃。KeepProperty 把它写进文档，读侧却必须跳过 ——
                // 没有 setter 可赋值，所以它是只写得出去的那一种成员。
                var writable = property.SetMethod is { DeclaredAccessibility: Accessibility.Public };
                if (!writable && (options & ArchiveFlags.KeepProperty) == 0) continue;

                var condition = ReadWriteCondition(property, property.Type, notices);
                if (condition is null) continue;
                if (!emitted.Add(property.Name)) continue;

                yield return BuildMember(property.Name, property.Type, renamed, writeOnly: !writable, condition: condition.Value, required: IsRequired(property));
            }

            foreach (var (field, name, documentName, condition) in fields)
            {
                if (!emitted.Add(name)) continue;
                yield return BuildMember(name, field.Type, documentName, condition: condition, required: IsRequired(field));
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

                    yield return BuildMember(property.Name, property.Type, required: IsRequired(property));
                }

                foreach (var member in baseType.GetMembers())
                {
                    if (member is not IFieldSymbol field) continue;
                    if (!AIContextNaming.HasAttribute(field, VeloxPropertyAttributeName)) continue;

                    var name = AIContextNaming.PromotedPropertyName(field.Name);
                    if (name.Length == 0 || !emitted.Add(name)) continue;

                    yield return BuildMember(name, field.Type, required: IsRequired(field));
                }
            }
        }

        /// <summary>
        /// Reports a required member the document will never be able to satisfy.
        /// </summary>
        /// <remarks>
        /// Required means the generated reader refuses a document missing it, so a required member the writer can
        /// leave out is a document nobody can load. There are two ways to get there: excluding the member
        /// outright, or letting its write be conditional.
        /// </remarks>
        private static void ReportUnsatisfiableRequired(
            INamedTypeSymbol symbol,
            IReadOnlyList<VeloxJsonMember> members,
            List<Diagnostic>? notices)
        {
            if (notices is null) return;

            foreach (var name in DocumentRequiredMemberNames(symbol))
            {
                var location = symbol.GetMembers(name).FirstOrDefault()?.Locations.FirstOrDefault();
                var member = members.FirstOrDefault(m => m.Name == name);

                if (member is null)
                {
                    notices.Add(Diagnostic.Create(Diagnostics.UnusableArchiveDeclaration, location, name,
                        "it is required, so the document has to carry it — but it is excluded and never written"));
                    continue;
                }

                if (member.WriteOnly || member.WriteCondition != VeloxJsonWriteCondition.Always)
                {
                    notices.Add(Diagnostic.Create(Diagnostics.UnusableArchiveDeclaration, location, name,
                        "it is required, so the document has to carry it — but its write is conditional, so it may be absent"));
                }
            }
        }

        /// <summary>
        /// Reports an array that sits inside a container, which this format cannot read back.
        /// </summary>
        /// <remarks>
        /// A member's own array is read into a <c>List&lt;T&gt;</c> and converted, and that works because the element
        /// type is known where the member is declared. An element has no such place: by the time the reader is
        /// inside <c>List&lt;int[]&gt;</c> all it holds is a <c>Type</c>, and making an array from one is reflection —
        /// the thing this format exists to avoid. Reported rather than left to fail at run time.
        /// </remarks>
        private static void ReportNestedArray(VeloxJsonMember member, List<Diagnostic>? notices)
        {
            if (notices is null) return;
            if (member.ElementType is not IArrayTypeSymbol array || IsByteArray(array)) return;

            notices.Add(Diagnostic.Create(
                Diagnostics.UnusableArchiveDeclaration,
                null,
                member.Name,
                "an array nested inside a container cannot be read back — declare the element as a List<T> instead"));
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

                        yield return BuildMember(property.Name, property.Type, required: IsRequired(property));
                    }
                }
            }
        }

        /// <summary>
        /// The <c>[Archive]</c> declaration on a member: its options, and the name it renames the member to.
        /// </summary>
        /// <remarks>
        /// An enum argument arrives as its underlying integer, not as the enum — attributes carry constants, and
        /// the compiler boxes them as the backing type.
        /// </remarks>
        private static (ArchiveFlags Options, string? Name) ReadArchive(ISymbol member, List<Diagnostic>? notices)
        {
            var attribute = member.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Replace("global::", string.Empty) == ArchiveAttributeName);

            if (attribute is null) return (ArchiveFlags.None, null);

            var options = attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is int raw
                ? (ArchiveFlags)raw
                : ArchiveFlags.None;

            if ((options & ArchiveFlags.ReName) == 0) return (options, null);

            // 形参声明成 object?，所以名字按实参自己的类型出现 —— 字符串就是 string。
            if (attribute.ConstructorArguments.Length > 1
                && attribute.ConstructorArguments[1].Value is string { Length: > 0 } name)
            {
                return (options, name);
            }

            notices?.Add(Diagnostic.Create(
                Diagnostics.UnusableArchiveDeclaration,
                attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
                member.Name,
                "ReName needs a non-empty string as the attribute's second argument"));

            return (options & ~ArchiveFlags.ReName, null);
        }

        /// <summary>
        /// What a member's <c>[JsonIgnore]</c> says about writing it, or <see langword="null"/> when it says the
        /// member is not written at all.
        /// </summary>
        /// <remarks>
        /// The standard attribute is honoured as it stands rather than mirrored under a Velox name, so a type
        /// written for another serializer does not have to be rewritten to take part in this one. Its
        /// <c>Condition</c> keeps its meaning: <c>Never</c> is an explicit opt-in, and the two conditional values
        /// guard the write instead of dropping the member.
        /// </remarks>
        private static VeloxJsonWriteCondition? ReadWriteCondition(ISymbol member, ITypeSymbol declaredType, List<Diagnostic>? notices)
        {
            var attribute = member.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Replace("global::", string.Empty) == JsonIgnoreAttributeName);

            if (attribute is null) return VeloxJsonWriteCondition.Always;

            var condition = ReadJsonIgnoreCondition(attribute) ?? "Always";

            switch (condition)
            {
                case "Never":
                    return VeloxJsonWriteCondition.Always;

                case "Always":
                    return null;

                // 值类型（可空的除外）永远不为 null：没有可判的条件，写侧也就不必发一条永远为真的守卫。
                case "WhenWritingNull" when !CanBeNull(declaredType):
                    return VeloxJsonWriteCondition.Always;

                case "WhenWritingNull":
                    return VeloxJsonWriteCondition.WhenNotNull;

                case "WhenWritingDefault":
                    return VeloxJsonWriteCondition.WhenNotDefault;

                default:
                    notices?.Add(Diagnostic.Create(
                        Diagnostics.UnusableArchiveDeclaration,
                        attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
                        member.Name,
                        $"the condition '{condition}' is not one this format knows how to honour"));
                    return VeloxJsonWriteCondition.Always;
            }
        }

        /// <summary>
        /// The name of the <c>Condition</c> on a <c>[JsonIgnore]</c>, or <see langword="null"/> when it names none.
        /// </summary>
        /// <remarks>
        /// Read <b>by name</b>, never by the number behind it. <c>JsonIgnoreCondition</c> has gained members across
        /// releases — <c>WhenWriting</c> and <c>WhenReading</c> arrived in .NET 11 — so a mirror of its ordering is
        /// a fact waiting to go stale, and the ordering is not what the attribute means anyway.
        /// </remarks>
        private static string? ReadJsonIgnoreCondition(AttributeData attribute)
        {
            // Condition 是特性上的属性（那个特性只有无参构造），所以它一定出现在命名实参里。
            foreach (var named in attribute.NamedArguments)
            {
                if (named.Key != "Condition" || named.Value.Value is not int value) continue;

                return named.Value.Type?.GetMembers()
                    .OfType<IFieldSymbol>()
                    .FirstOrDefault(field => field.HasConstantValue && field.ConstantValue is int constant && constant == value)
                    ?.Name;
            }

            return null;
        }

        // 值类型（可空的除外）永远不为 null。
        private static bool CanBeNull(ITypeSymbol type)
            => !type.IsValueType || !SymbolEqualityComparer.Default.Equals(UnwrapNullable(type), type);

        /// <summary>Whether the document has to carry this member — C#'s <c>required</c>, or <c>[JsonRequired]</c>.</summary>
        private static bool IsRequired(ISymbol member)
            => RequiredMembers.IsRequired(member)
               || AIContextNaming.HasAttribute(member, JsonRequiredAttributeName);

        /// <summary>
        /// The names of every member the document has to carry, on the type and up its base chain.
        /// </summary>
        /// <remarks>
        /// Wider than <see cref="RequiredMembers.NamesOf"/>: that one answers what the compiler demands at the
        /// construction site, this one answers what the reader will refuse a document for — which also includes
        /// <c>[JsonRequired]</c>, a member C# knows nothing about.
        /// </remarks>
        private static List<string> DocumentRequiredMemberNames(INamedTypeSymbol symbol)
        {
            var names = new List<string>();

            for (var current = symbol; current is not null; current = current.BaseType)
            {
                if (current.SpecialType == SpecialType.System_Object) break;

                foreach (var member in current.GetMembers())
                {
                    if (!IsRequired(member) || names.Contains(member.Name)) continue;
                    names.Add(member.Name);
                }
            }

            return names;
        }

        /// <summary>
        /// Whether a marked member can be written by generated code, reporting it when it cannot.
        /// </summary>
        /// <remarks>
        /// Only a member that asked to take part is worth a diagnostic: the default rules already pass over
        /// everything the generator cannot reach, so saying so for every private helper would drown the reports
        /// that matter.
        /// </remarks>
        private static bool CanBeMarked(ISymbol member, ArchiveFlags options, IAssemblySymbol assembly, List<Diagnostic>? notices)
        {
            if (options == ArchiveFlags.None) return true;
            if (IsReachableFromGeneratedCode(member, assembly)) return true;

            notices?.Add(Diagnostic.Create(
                Diagnostics.UnusableArchiveDeclaration,
                member.Locations.FirstOrDefault(),
                member.Name,
                SymbolEqualityComparer.Default.Equals(member.ContainingAssembly, assembly)
                    ? "generated code sits in another namespace of the same assembly, so it reaches internal members but not private or protected ones"
                    : "the generated reader and writer are emitted into the consuming assembly, so a marked member on a type declared elsewhere has to be public"));

            return false;
        }

        /// <summary>Reports a <c>KeepField</c> the default rules will not honour.</summary>
        private static void ReportFieldConflict(IFieldSymbol field, List<Diagnostic>? notices)
            => notices?.Add(Diagnostic.Create(
                Diagnostics.ConflictingArchiveField,
                field.Locations.FirstOrDefault(),
                field.Name,
                "it has a property beside it, which is the member the default rules take — drop the field with ArchiveOptions.IgnoreField, or move the marking onto the property"));

        // 字段是不是已经有一个对应属性 —— 按提升名找同名属性。`[VeloxProperty]` 的字段一定有（生成器会造出那个属性）。
        private static bool HasCorrespondingProperty(INamedTypeSymbol symbol, IFieldSymbol field)
        {
            var name = AIContextNaming.PromotedPropertyName(field.Name);
            if (name.Length == 0) return false;

            return symbol.GetMembers().OfType<IPropertySymbol>().Any(property => !property.IsIndexer && property.Name == name);
        }

        private static VeloxJsonMember BuildMember(
            string name,
            ITypeSymbol declaredType,
            string? documentName = null,
            bool writeOnly = false,
            VeloxJsonWriteCondition condition = VeloxJsonWriteCondition.Always,
            bool required = false)
        {
            var (kind, element, interfaceKeyed) = Classify(declaredType);
            return new VeloxJsonMember(name, declaredType, element, kind)
            {
                IsDeclaredHere = true,
                InterfaceKeyed = interfaceKeyed,
                DocumentName = documentName ?? name,
                WriteOnly = writeOnly,
                WriteCondition = condition,
                IsRequired = required,
            };
        }

        /// <summary>
        /// Decides how a declared type reaches the document — the four cases the format knows.
        /// </summary>
        private static (VeloxJsonMemberKind Kind, ITypeSymbol? Element, bool InterfaceKeyed) Classify(ITypeSymbol type)
        {
            // IsScalar 在前：byte[] 是标量（base64 字符串），其余的数组才是序列。
            if (IsScalar(type)) return (VeloxJsonMemberKind.Scalar, null, false);

            if (type is IArrayTypeSymbol array) return (VeloxJsonMemberKind.Collection, array.ElementType, false);

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

        /// <summary>
        /// A nullable value type as its underlying type, so <c>int?</c> is spelled like <c>int</c>.
        /// </summary>
        /// <remarks>
        /// An absent value is handled by every scalar read already — <c>null</c> is a token like any other — so
        /// the only thing the nullable wrapper changes is that the member may be null.
        /// </remarks>
        internal static ITypeSymbol UnwrapNullable(ITypeSymbol type)
            => type is INamedTypeSymbol named
               && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
               && named.TypeArguments.Length == 1
                ? named.TypeArguments[0]
                : type;

        /// <summary>Whether a type is one of the scalars the format writes with a dedicated primitive.</summary>
        /// <remarks>
        /// <c>byte[]</c> counts: it is written as a base64 string rather than as an array of numbers. It is the one
        /// array with a spelling of its own, and it is the one STJ and Json.NET agree on.
        /// </remarks>
        private static bool IsScalar(ITypeSymbol type)
        {
            var unwrapped = UnwrapNullable(type);
            if (IsByteArray(unwrapped)) return true;

            return unwrapped.SpecialType switch
            {
                SpecialType.System_String or SpecialType.System_Int32 or SpecialType.System_Int64
                    or SpecialType.System_Double or SpecialType.System_Single or SpecialType.System_Decimal
                    or SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_Int16
                    or SpecialType.System_Char or SpecialType.System_Object => true,
                _ => unwrapped.TypeKind == TypeKind.Enum
                     || unwrapped.ToDisplayString() == "System.Guid"
                     || unwrapped.ToDisplayString() == "System.DateTime"
                     || unwrapped.ToDisplayString() == "System.TimeSpan",
            };
        }

        /// <summary>Whether the type is <c>byte[]</c> — the one array written as a scalar.</summary>
        private static bool IsByteArray(ITypeSymbol type)
            => type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte };

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
