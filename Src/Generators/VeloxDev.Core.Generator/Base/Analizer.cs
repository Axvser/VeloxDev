using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;

namespace VeloxDev.Generators.Base
{
    public static class Analizer
    {
        // 类型名的唯一写法。字段类型比较若用另一种 SymbolDisplayFormat，'string' 与 'System.String'
        // 会被判成不同类型 —— 所有字段复用都会因此被误报为冲突。
        internal static string DisplayFullTypeName(ITypeSymbol typeSymbol) =>
            typeSymbol.ToDisplayString(new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));

        /// <summary>
        /// Whether the setter's "unchanged" test for a type has to look at the bit pattern rather than at
        /// equality — true for the two floating-point types, unwrapping <see cref="Nullable{T}"/>.
        /// </summary>
        /// <remarks>
        /// 浮点的相等对字节一无所知：`(-0.0).Equals(0.0)` 与 `NaN.Equals(NaN)` 都为真。生成器原来用
        /// `Object.Equals` 判「没变」，于是字段已经握着 `+0.0` 时赋 `-0.0` 会被当成没变、直接 return —— 符号
        /// 永远进不去，回写出来就差一个字节（`-0.0` 变 `0.0`）。两个类型都用
        /// <c>BitConverter.DoubleToInt64Bits</c> 比：它接 `float` 时按值拓宽，符号保得住，不同 NaN 也仍然不同。
        /// </remarks>
        internal static bool IsFloatingPointType(ITypeSymbol typeSymbol)
        {
            var type = typeSymbol;
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } named)
            {
                type = named.TypeArguments[0];
            }

            return type.SpecialType is SpecialType.System_Double or SpecialType.System_Single;
        }

        public static class Filters
        {
            /// <summary>
            /// One class a generator has to write code for, together with the partial declaration to
            /// write it against.
            /// </summary>
            /// <remarks>
            /// <para>
            /// Deliberately free of <see cref="ISymbol"/>: a transform's output is cached per syntax
            /// node and is not recomputed while that tree is unchanged, so a symbol captured here
            /// would keep pointing at an older <see cref="Compilation"/>. Every writer reads semantics
            /// that live outside the target's own file — base-type chains, referenced assemblies — so a
            /// stale symbol produces silently wrong code after an edit to some other file. The symbol is
            /// re-resolved against the current compilation in <see cref="Resolve"/> instead.
            /// </para>
            /// <para>
            /// A hand-written struct rather than a <c>record struct</c>: the generator targets
            /// netstandard2.0, which has no <c>IsExternalInit</c> for the compiler to synthesise.
            /// </para>
            /// </remarks>
            public readonly struct GeneratorTarget : IEquatable<GeneratorTarget>
            {
                /// <summary>Creates a target descriptor for a class declaration produced by one of the trigger attributes.</summary>
                public GeneratorTarget(ClassDeclarationSyntax syntax, string typeKey, bool isClassLevelAttribute)
                {
                    Syntax = syntax;
                    TypeKey = typeKey;
                    IsClassLevelAttribute = isClassLevelAttribute;
                }

                /// <summary>The partial declaration a writer is handed.</summary>
                public ClassDeclarationSyntax Syntax { get; }

                /// <summary>
                /// Fully qualified name of the target type, used to recognise the several partial
                /// declarations of one type and to compare targets without touching symbols.
                /// </summary>
                public string TypeKey { get; }

                /// <summary>
                /// True when the attribute that produced this target sits on the class declaration
                /// itself rather than on one of its members. Used only to pick a representative
                /// declaration when a type is split over several partial files.
                /// </summary>
                public bool IsClassLevelAttribute { get; }

                /// <inheritdoc />
                public bool Equals(GeneratorTarget other)
                    => ReferenceEquals(Syntax, other.Syntax)
                       && IsClassLevelAttribute == other.IsClassLevelAttribute
                       && string.Equals(TypeKey, other.TypeKey, StringComparison.Ordinal);

                /// <inheritdoc />
                public override bool Equals(object? obj) => obj is GeneratorTarget other && Equals(other);

                /// <inheritdoc />
                public override int GetHashCode()
                    => TypeKey.GetHashCode() ^ (IsClassLevelAttribute ? 1 : 0);

                /// <summary>Returns whether two targets are equal.</summary>
                public static bool operator ==(GeneratorTarget left, GeneratorTarget right) => left.Equals(right);

                /// <summary>Returns whether two targets differ.</summary>
                public static bool operator !=(GeneratorTarget left, GeneratorTarget right) => !left.Equals(right);
            }

            /// <summary>
            /// Metadata names of every attribute that makes a class a codegen target. Class-level
            /// attributes are listed first so that, for a type split over several partial files, the
            /// declaration carrying the class-level attribute becomes the representative one — the one
            /// every writer is handed.
            /// </summary>
            private static readonly string[] TriggerAttributes =
            [
                "VeloxDev.WorkflowSystem.WorkflowBuilder+TreeAttribute`1",
                "VeloxDev.WorkflowSystem.WorkflowBuilder+NodeAttribute`1",
                "VeloxDev.WorkflowSystem.WorkflowBuilder+SlotAttribute`1",
                "VeloxDev.WorkflowSystem.WorkflowBuilder+LinkAttribute`1",
                "VeloxDev.WorkflowSystem.DefaultAnchorAttribute",
                "VeloxDev.WorkflowSystem.DefaultSizeAttribute",
                "VeloxDev.TimeLine.TickableAttribute",
                "VeloxDev.MVVM.VeloxPropertyAttribute",
                "VeloxDev.MVVM.VeloxCommandAttribute",
                "VeloxDev.AspectOriented.AspectOrientedAttribute",
            ];

            /// <summary>
            /// Streams every class carrying one of <see cref="TriggerAttributes"/> on itself or on one
            /// of its members, exactly once per type.
            /// </summary>
            /// <remarks>
            /// Attributes are resolved as symbols rather than matched by name, so fully qualified and
            /// aliased forms are recognised too, and a class carrying none of them never reaches a
            /// writer. The previous pipeline selected every partial class in the compilation and ran
            /// every writer over all of them on every keystroke; here only attributed classes reach a
            /// writer, and the per-node transform is cached by Roslyn.
            /// </remarks>
            public static IncrementalValueProvider<ImmutableArray<GeneratorTarget>> Targets(
                IncrementalGeneratorInitializationContext context)
            {
                var perAttribute = new IncrementalValueProvider<ImmutableArray<GeneratorTarget>>[TriggerAttributes.Length];
                for (var i = 0; i < TriggerAttributes.Length; i++)
                {
                    perAttribute[i] = context.SyntaxProvider
                        .ForAttributeWithMetadataName(TriggerAttributes[i], IsCandidateClass, ToGeneratorTarget)
                        .Where(static target => target.HasValue)
                        .Select(static (target, _) => target!.Value)
                        .Collect();
                }

                // 把逐特性流折叠成一个扁平数组。
                var merged = perAttribute[0];
                for (var i = 1; i < perAttribute.Length; i++)
                {
                    var next = perAttribute[i];
                    merged = merged.Combine(next)
                                   .Select(static (pair, _) => pair.Left.AddRange(pair.Right));
                }

                return merged.Select(static (targets, _) => Deduplicate(targets));
            }

            /// <summary>
            /// Re-resolves each target's symbol against <paramref name="compilation"/> and yields the
            /// pairs a writer needs.
            /// </summary>
            /// <remarks>
            /// This is the step that keeps the pipeline honest: <see cref="Targets"/> has no notion of
            /// the current <see cref="Compilation"/>, so the symbol must be looked up here against the
            /// compilation being compiled right now. Targets whose tree has already left the compilation
            /// are skipped.
            /// </remarks>
            public static IEnumerable<(ClassDeclarationSyntax Syntax, INamedTypeSymbol Symbol)> Resolve(
                ImmutableArray<GeneratorTarget> targets,
                Compilation compilation)
            {
                foreach (var target in targets)
                {
                    if (!compilation.ContainsSyntaxTree(target.Syntax.SyntaxTree))
                        continue;

                    var model = compilation.GetSemanticModel(target.Syntax.SyntaxTree);
                    if (model.GetDeclaredSymbol(target.Syntax) is INamedTypeSymbol symbol)
                        yield return (target.Syntax, symbol);
                }
            }

            private static bool IsCandidateClass(SyntaxNode node, CancellationToken cancellationToken)
            {
                var declaration = node as ClassDeclarationSyntax ?? node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
                return declaration != null && IsPartialClass(declaration);
            }

            private static GeneratorTarget? ToGeneratorTarget(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
            {
                var declaration = context.TargetNode as ClassDeclarationSyntax
                    ?? context.TargetNode.FirstAncestorOrSelf<ClassDeclarationSyntax>();
                if (declaration == null)
                    return null;

                return context.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken) is INamedTypeSymbol symbol
                    ? new GeneratorTarget(
                        declaration,
                        symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        context.TargetNode is ClassDeclarationSyntax)
                    : null;
            }

            /// <summary>
            /// Collapses the per-attribute streams into one entry per type.
            /// </summary>
            /// <remarks>
            /// Deduplication is by <see cref="GeneratorTarget.TypeKey"/> rather than by symbol: keying
            /// on symbols would let the same type in twice whenever two cached entries hold symbols
            /// from different compilations, and the second <c>AddSource</c> would then be rejected for
            /// a duplicate hint name.
            /// </remarks>
            private static ImmutableArray<GeneratorTarget> Deduplicate(ImmutableArray<GeneratorTarget> targets)
            {
                var indexByType = new Dictionary<string, int>(StringComparer.Ordinal);
                var result = new List<GeneratorTarget>(targets.Length);

                foreach (var target in targets)
                {
                    if (indexByType.TryGetValue(target.TypeKey, out var index))
                    {
                        // 跨多个 partial 文件的类型每个声明各到达一次。保留带类级特性的那份（语法作用域写入者需要它）；否则先见到的胜出。
                        if (!result[index].IsClassLevelAttribute && target.IsClassLevelAttribute)
                            result[index] = target;
                        continue;
                    }

                    indexByType.Add(target.TypeKey, result.Count);
                    result.Add(target);
                }

                return ImmutableArray.CreateRange(result);
            }
        }

        public sealed class MVVMFieldAnalizer
        {
            /// <summary>Metadata name of the attribute that marks a field for MVVM generation.</summary>
            public const string NAME_VP = "VeloxProperty";

            internal MVVMFieldAnalizer(IFieldSymbol fieldSymbol)
            {
                Symbol = fieldSymbol;
                TypeName = GetFullyQualifiedTypeName(fieldSymbol.Type);
                FieldName = fieldSymbol.Name;
                PropertyName = GetPropertyNameFromFieldName(fieldSymbol.Name);
                IsNullable = IsNullableType(fieldSymbol.Type);
                IsNotifyCollectionChanged = IsNotifyCollectionChangedType(fieldSymbol.Type);
                CollectionItemTypeName = GetCollectionItemTypeName(fieldSymbol.Type);
            }

            /// <summary>Gets the field symbol being analyzed.</summary>
            public IFieldSymbol Symbol { get; private set; }
            /// <summary>Gets the fully qualified type name of the field.</summary>
            public string TypeName { get; private set; } = string.Empty;
            /// <summary>Gets the field's name.</summary>
            public string FieldName { get; private set; } = string.Empty;
            /// <summary>Gets the promoted property name derived from the field.</summary>
            public string PropertyName { get; private set; } = string.Empty;
            /// <summary>Gets whether the field's type is nullable.</summary>
            public bool IsNullable { get; private set; }
            /// <summary>Gets whether the field's type is an observable collection.</summary>
            public bool IsNotifyCollectionChanged { get; private set; }
            /// <summary>Gets the collection element type name, or <see langword="null"/> when the field is not a collection.</summary>
            public string? CollectionItemTypeName { get; private set; }

            private static string GetFullyQualifiedTypeName(ITypeSymbol typeSymbol) =>
                Analizer.DisplayFullTypeName(typeSymbol);

            private static bool IsNullableType(ITypeSymbol typeSymbol)
            {
                if (typeSymbol is INamedTypeSymbol namedType &&
                    namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                {
                    return true;
                }

                return typeSymbol.NullableAnnotation == NullableAnnotation.Annotated;
            }

            // 规则本体在 AIContextNaming：上下文树的生成器看不见这里的产物，只能复现同一个命名规则。
            private static string GetPropertyNameFromFieldName(string fieldName)
                => AIContextNaming.PromotedPropertyName(fieldName);
        }

        public sealed class MVVMPropertyAnalizer
        {
            /// <summary>Metadata name of the attribute that marks a property for MVVM generation.</summary>
            public const string NAME_VP = "VeloxProperty";

            internal MVVMPropertyAnalizer(IPropertySymbol propertySymbol)
            {
                Symbol = propertySymbol;
                TypeName = GetFullyQualifiedTypeName(propertySymbol.Type);
                PropertyName = propertySymbol.Name;
                FieldName = GetFieldNameFromPropertyName(propertySymbol.Name);
                IsNullable = IsNullableType(propertySymbol.Type);

                // 取属性自身的访问修饰符
                PropertyAccessModifier = GetPropertyAccessModifier(propertySymbol);

                // 取访问器的访问修饰符（相对属性层级）
                GetterAccessModifier = GetAccessorAccessModifier(propertySymbol, isGetter: true);
                SetterAccessModifier = GetAccessorAccessModifier(propertySymbol, isGetter: false);

                HasGetter = propertySymbol.GetMethod != null;
                HasSetter = propertySymbol.SetMethod != null;
                IsPartial = IsPartialProperty(propertySymbol);
                IsNotifyCollectionChanged = IsNotifyCollectionChangedType(propertySymbol.Type);
                CollectionItemTypeName = GetCollectionItemTypeName(propertySymbol.Type);
            }

            /// <summary>Gets the property symbol being analyzed.</summary>
            public IPropertySymbol Symbol { get; private set; }
            /// <summary>Gets the fully qualified type name of the property.</summary>
            public string TypeName { get; private set; } = string.Empty;
            /// <summary>Gets the property's name.</summary>
            public string PropertyName { get; private set; } = string.Empty;
            /// <summary>Gets the backing field name derived from the property.</summary>
            public string FieldName { get; private set; } = string.Empty;
            /// <summary>Gets whether the property's type is nullable.</summary>
            public bool IsNullable { get; private set; }
            /// <summary>Gets whether the property declares a getter.</summary>
            public bool HasGetter { get; private set; }
            /// <summary>Gets whether the property declares a setter.</summary>
            public bool HasSetter { get; private set; }
            /// <summary>Gets whether the property is a partial property.</summary>
            public bool IsPartial { get; private set; }
            /// <summary>Gets whether the property's type is an observable collection.</summary>
            public bool IsNotifyCollectionChanged { get; private set; }
            /// <summary>Gets the collection element type name, or <see langword="null"/> when the property is not a collection.</summary>
            public string? CollectionItemTypeName { get; private set; }
            /// <summary>Gets the property's declared access modifier.</summary>
            public string PropertyAccessModifier { get; private set; } = "public";
            /// <summary>Gets the getter's explicit access modifier, or an empty string when it inherits the property's.</summary>
            public string GetterAccessModifier { get; private set; } = string.Empty;
            /// <summary>Gets the setter's explicit access modifier, or an empty string when it inherits the property's.</summary>
            public string SetterAccessModifier { get; private set; } = string.Empty;

            private static string GetPropertyAccessModifier(IPropertySymbol propertySymbol)
            {
                return propertySymbol.DeclaredAccessibility switch
                {
                    Accessibility.Private => "private",
                    Accessibility.Protected => "protected",
                    Accessibility.Internal => "internal",
                    Accessibility.ProtectedOrInternal => "protected internal",
                    Accessibility.ProtectedAndInternal => "private protected",
                    Accessibility.Public => "public",
                    _ => "public"
                };
            }

            private static string GetAccessorAccessModifier(IPropertySymbol propertySymbol, bool isGetter)
            {
                var accessorMethod = isGetter ? propertySymbol.GetMethod : propertySymbol.SetMethod;
                if (accessorMethod == null) return string.Empty;

                // 仅当访问器可访问性与属性不同时才写修饰符
                var propertyAccessibility = propertySymbol.DeclaredAccessibility;
                var accessorAccessibility = accessorMethod.DeclaredAccessibility;

                if (accessorAccessibility == propertyAccessibility)
                    return string.Empty;

                return accessorAccessibility switch
                {
                    Accessibility.Private => "private",
                    Accessibility.Protected => "protected",
                    Accessibility.Internal => "internal",
                    Accessibility.ProtectedOrInternal => "protected internal",
                    Accessibility.ProtectedAndInternal => "private protected",
                    Accessibility.Public => "public",
                    _ => string.Empty
                };
            }

            private static bool IsPartialProperty(IPropertySymbol propertySymbol)
            {
                foreach (var syntaxReference in propertySymbol.DeclaringSyntaxReferences)
                {
                    var syntax = syntaxReference.GetSyntax();
                    if (syntax is PropertyDeclarationSyntax propertySyntax)
                    {
                        return propertySyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
                    }
                }
                return false;
            }

            private static string GetFullyQualifiedTypeName(ITypeSymbol typeSymbol) =>
                Analizer.DisplayFullTypeName(typeSymbol);

            private static bool IsNullableType(ITypeSymbol typeSymbol)
            {
                if (typeSymbol is INamedTypeSymbol namedType &&
                    namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                {
                    return true;
                }

                return typeSymbol.NullableAnnotation == NullableAnnotation.Annotated;
            }

            private static string GetFieldNameFromPropertyName(string propertyName)
            {
                if (char.IsUpper(propertyName[0]))
                {
                    return "_" + char.ToLower(propertyName[0]) + propertyName.Substring(1);
                }
                else
                {
                    return "_" + propertyName;
                }
            }
        }

        public class MVVMPropertyFactory
        {
            const string RETRACT = "   ";

            // 从字段构造：属性名由字段名推导，读写器必定齐全
            /// <summary>Creates a factory from an analyzed field; the generated property is public with full accessors.</summary>
            public MVVMPropertyFactory(MVVMFieldAnalizer fieldAnalizer)
            {
                FullTypeName = fieldAnalizer.TypeName;
                SourceName = $"this.{fieldAnalizer.FieldName}";
                PropertyName = fieldAnalizer.PropertyName;
                IsNullable = fieldAnalizer.IsNullable;
                IsFloatingPoint = Analizer.IsFloatingPointType(fieldAnalizer.Symbol.Type);
                IsFromField = true;
                ShouldEmitField = false;
                HasGetter = true;
                HasSetter = true;
                IsPartial = false;
                IsNotifyCollectionChanged = fieldAnalizer.IsNotifyCollectionChanged;
                CollectionItemTypeName = fieldAnalizer.CollectionItemTypeName;
                PropertyAccessModifier = "public";
                GetterAccessModifier = string.Empty;
                SetterAccessModifier = string.Empty;
            }

            // 从 partial 属性构造：读写器与可访问性照用户写的那份保留
            /// <summary>Creates a factory from an analyzed partial property, preserving the accessors and modifiers the user wrote.</summary>
            public MVVMPropertyFactory(MVVMPropertyAnalizer propertyAnalizer)
            {
                FullTypeName = propertyAnalizer.TypeName;
                SourceName = propertyAnalizer.FieldName;
                PropertyName = propertyAnalizer.PropertyName;
                IsNullable = propertyAnalizer.IsNullable;
                IsFloatingPoint = Analizer.IsFloatingPointType(propertyAnalizer.Symbol.Type);
                IsFromField = false;
                ShouldEmitField = true;
                HasGetter = propertyAnalizer.HasGetter;
                HasSetter = propertyAnalizer.HasSetter;
                IsPartial = propertyAnalizer.IsPartial;
                IsNotifyCollectionChanged = propertyAnalizer.IsNotifyCollectionChanged;
                CollectionItemTypeName = propertyAnalizer.CollectionItemTypeName;
                PropertyAccessModifier = propertyAnalizer.PropertyAccessModifier;
                GetterAccessModifier = propertyAnalizer.GetterAccessModifier;
                SetterAccessModifier = propertyAnalizer.SetterAccessModifier;
            }

            /// <summary>Gets the fully qualified type name of the generated property.</summary>
            public string FullTypeName { get; private set; }
            /// <summary>Gets the source expression the generated code reads and writes.</summary>
            public string SourceName { get; private set; }
            /// <summary>Gets the generated property's name.</summary>
            public string PropertyName { get; private set; }
            /// <summary>Gets whether the generated property's type is nullable.</summary>
            public bool IsNullable { get; private set; }
            /// <summary>Gets whether the setter's "unchanged" test must compare bit patterns; see <see cref="Analizer.IsFloatingPointType"/>.</summary>
            public bool IsFloatingPoint { get; private set; }
            /// <summary>Gets whether this factory was built from a field rather than a partial property.</summary>
            public bool IsFromField { get; private set; }
            /// <summary>
            /// Whether the generated code must declare the backing field itself.
            /// </summary>
            /// <remarks>
            /// False whenever the storage already exists in the user's source: the field form supplies its own
            /// field, and the property form reuses any accessible field of the conventional name instead of
            /// declaring a second one of the same name.
            /// </remarks>
            public bool ShouldEmitField { get; internal set; }
            /// <summary>Gets whether a getter is generated.</summary>
            public bool HasGetter { get; private set; }
            /// <summary>Gets whether a setter is generated.</summary>
            public bool HasSetter { get; private set; }
            /// <summary>Gets whether the generated property is partial.</summary>
            public bool IsPartial { get; private set; }
            /// <summary>Gets whether the generated property participates in change tracking.</summary>
            public bool IsNotifyCollectionChanged { get; private set; }
            /// <summary>Gets the collection element type name, or <see langword="null"/> when the property is not a collection.</summary>
            public string? CollectionItemTypeName { get; private set; }
            /// <summary>Gets the access modifier of the generated property.</summary>
            public string PropertyAccessModifier { get; private set; }
            /// <summary>Gets the access modifier of the generated getter, if any.</summary>
            public string GetterAccessModifier { get; private set; }
            /// <summary>Gets the access modifier of the generated setter, if any.</summary>
            public string SetterAccessModifier { get; private set; }
            /// <summary>Gets or sets whether workflow slot lifecycle hooks are generated.</summary>
            public bool UseWorkflowSlotLifecycle { get; set; }
            /// <summary>Gets or sets whether workflow slots are auto-created.</summary>
            public bool UseWorkflowSlotAutoCreation { get; set; }
            /// <summary>Gets or sets whether collection lifecycle hooks are generated for workflow slots.</summary>
            public bool UseWorkflowSlotCollectionLifecycle { get; set; }
            /// <summary>Gets or sets whether slot-enumerator lifecycle hooks are generated.</summary>
            public bool UseSlotEnumeratorLifecycle { get; set; }

            /// <summary>
            /// Controls how the setter body is generated:
            /// <see cref="SetterMode.Default"/> — direct field set with Object.Equals check.
            /// <see cref="SetterMode.FrameworkSetProperty"/> — delegates to <c>SetProperty(ref T, T, string)</c>.
            /// <see cref="SetterMode.FrameworkRaiseAndSet"/> — ReactiveUI's <c>RaiseAndSetIfChanged(ref T, T, string)</c>.
            /// </summary>
            public SetterMode FrameworkSetterMode { get; set; } = SetterMode.Default;

            /// <summary>Gets or sets the body lines emitted while the property value is changing.</summary>
            public List<string> SetteringBody { get; set; } = [];
            /// <summary>Gets or sets the body lines emitted after the property value has changed.</summary>
            public List<string> SetteredBody { get; set; } = [];

            /// <summary>
        /// The full setter body lines, resolved according to <see cref="FrameworkSetterMode"/>.
        /// </summary>
        // "值没变" 的判据。浮点必须比位模式（理由见 Analizer.IsFloatingPointType）—— 用 Object.Equals 时
        // `-0.0` 会被当成「和 +0.0 一样」而丢掉符号。可空的那种两头都要有值才算相等。
        private string EqualityComparison()
        {
            if (!IsFloatingPoint) return $"global::System.Object.Equals({SourceName}, value)";

            const string bits = "global::System.BitConverter.DoubleToInt64Bits";
            return IsNullable
                ? $"{SourceName}.HasValue == value.HasValue && (!{SourceName}.HasValue || {bits}({SourceName}.Value) == {bits}(value.Value))"
                : $"{bits}({SourceName}) == {bits}(value)";
        }

        public List<string> GetSetterBodyLines()
        {
            var equalityComparison = EqualityComparison();

            if (FrameworkSetterMode == SetterMode.Default)
            {
                return
                [
                    $"if({equalityComparison}) return;",
                    $"var old = {SourceName};",
                    .. GetWorkflowSlotBeforeAssignmentLines(),
                    .. SetteringBody,
                    $"On{PropertyName}Changing(old, value);",
                    .. GetCollectionBeforeAssignmentLines(),
                    $"{SourceName} = value;",
                    .. GetWorkflowSlotAfterAssignmentLines(),
                    .. GetCollectionAfterAssignmentLines(),
                    $"On{PropertyName}Changed(old, value);",
                    .. SetteredBody,
                ];
            }
            else if (FrameworkSetterMode == SetterMode.FrameworkSetProperty)
            {
                // 委托给宿主 MVVM 框架的 SetProperty<T>(ref T, T, string)。生成的 partial 类能访问它，因为 SetProperty 在 CommunityToolkit.Mvvm 的 ObservableObject/ObservableValidator 和 Prism 的 BindableBase 里都是 protected。
                // 注意：有意排除 SetteredBody（OnPropertyChanged），因为 SetProperty 内部已发 PropertyChanged；仍调用 partial OnXxxChanged 以维持 VeloxDev 自己的 API 契约。
                return
                [
                    $"var old = {SourceName};",
                    .. GetWorkflowSlotBeforeAssignmentLines(),
                    .. SetteringBody,
                    $"On{PropertyName}Changing(old, value);",
                    .. GetCollectionBeforeAssignmentLines(),
                    $"if (SetProperty(ref {SourceName}, value, nameof({PropertyName})))",
                    "{",
                    .. GetWorkflowSlotAfterAssignmentLines().Select(l => $"    {l}"),
                    .. GetCollectionAfterAssignmentLines().Select(l => $"    {l}"),
                    $"    On{PropertyName}Changed(old, {SourceName});",
                    "}",
                ];
            }
            else if (FrameworkSetterMode == SetterMode.FrameworkRaiseAndSet)
            {
                // ReactiveUI 的 this.RaiseAndSetIfChanged<T>(ref T, T, string)
                return
                [
                    $"var old = {SourceName};",
                    .. GetWorkflowSlotBeforeAssignmentLines(),
                    .. SetteringBody,
                    $"this.RaiseAndSetIfChanged(ref {SourceName}, value, nameof({PropertyName}));",
                    .. GetWorkflowSlotAfterAssignmentLines(),
                    .. GetCollectionAfterAssignmentLines(),
                    $"On{PropertyName}Changed(old, {SourceName});",
                    .. SetteredBody,
                ];
            }
            else if (FrameworkSetterMode == SetterMode.FrameworkNotifyOfPropertyChange)
            {
                // Caliburn.Micro：由 NotifyOfPropertyChange 负责变更通知
                return
                [
                    $"if({equalityComparison}) return;",
                    $"var old = {SourceName};",
                    .. GetWorkflowSlotBeforeAssignmentLines(),
                    .. SetteringBody,
                    $"On{PropertyName}Changing(old, value);",
                    .. GetCollectionBeforeAssignmentLines(),
                    $"{SourceName} = value;",
                    $"NotifyOfPropertyChange(nameof({PropertyName}));",
                    .. GetWorkflowSlotAfterAssignmentLines(),
                    .. GetCollectionAfterAssignmentLines(),
                    $"On{PropertyName}Changed(old, value);",
                    .. SetteredBody,
                ];
            }

            return [];
        }

        private string NonNullableFullTypeName => FullTypeName.EndsWith("?") ? FullTypeName.Substring(0, FullTypeName.Length - 1) : FullTypeName;

            /// <summary>Returns the backing-field declaration, or an empty string when an existing field is reused.</summary>
            public string GenerateFieldDeclaration()
            {
                // 复用已有字段（见 MVVMWriter.ResolveBackingStorage）时不再声明，否则同名成员会撞成 CS0102。
                if (!ShouldEmitField) return string.Empty;

                var defaultValue = GetDefaultValue();
                return $"{RETRACT}private {FullTypeName} {SourceName} = {defaultValue};";
            }

            private string GetDefaultValue()
            {
                if (IsNullable)
                {
                    return "null";
                }

                var baseTypeName = FullTypeName.TrimEnd('?');
                return baseTypeName switch
                {
                    "bool" => "false",
                    "int" or "long" or "float" or "double" or "decimal" => "0",
                    "string" => "string.Empty",
                    _ => $"default({baseTypeName})"
                };
            }

            /// <summary>Returns the generated property accessors.</summary>
            public string Generate()
            {
                // 生成属性访问器，完整保留用户写的修饰符
                var getter = HasGetter ? GenerateGetter() : string.Empty;

                string setter;
                if (HasSetter)
                {
                    var setterAccessModifier = !string.IsNullOrEmpty(SetterAccessModifier) ? SetterAccessModifier + " " : "";
                    var setterLines = GetSetterBodyLines();
                    var setterBody = BuildMethodBody(setterLines);
                    setter = $$"""
                        {{RETRACT}}    {{setterAccessModifier}}set
                        {{RETRACT}}    {
                        {{setterBody}}
                        {{RETRACT}}    }
                        """;
                }
                else
                {
                    setter = string.Empty;
                }

                var changingMethod = HasSetter ?
                    $"{RETRACT}partial void On{PropertyName}Changing({FullTypeName} oldValue, {FullTypeName} newValue);" : string.Empty;

                var changedMethod = HasSetter ?
                    $"{RETRACT}partial void On{PropertyName}Changed({FullTypeName} oldValue, {FullTypeName} newValue);" : string.Empty;

                // 分部属性就加 partial 修饰符
                var partialModifier = IsPartial ? "partial " : string.Empty;
                var collectionMembers = GenerateCollectionMembers();
                var workflowSlotMembers = GenerateWorkflowSlotMembers();

                return $$"""
                    {{RETRACT}}{{PropertyAccessModifier}} {{partialModifier}}{{FullTypeName}} {{PropertyName}}
                    {{RETRACT}}{
                    {{getter}}
                    {{setter}}
                    {{RETRACT}}}
                    {{changingMethod}}
                    {{changedMethod}}
                    {{collectionMembers}}
                    {{workflowSlotMembers}}
                    """;
            }

            private string GenerateGetter()
            {
                var getterAccessModifier = !string.IsNullOrEmpty(GetterAccessModifier) ? GetterAccessModifier + " " : string.Empty;

                if (!IsNotifyCollectionChanged)
                {
                    return $"{RETRACT}    {getterAccessModifier}get => {SourceName};";
                }

                // ObservableCollection：在 getter 里惰性订阅 CollectionChanged。字段初始化器（[] = ...）直接赋值给字段、不经过 setter，所以这次 tracker 调用让订阅保持有效（只有首次真正订阅，之后是 O(1) 空操作）。
                var handlerName = $"On{PropertyName}CollectionChanged";
                return $$"""
                    {{RETRACT}}    {{getterAccessModifier}}get
                    {{RETRACT}}    {
                    {{RETRACT}}        global::VeloxDev.MVVM.ObservableCollectionTracker.EnsureSubscribed({{SourceName}}, {{handlerName}});
                    {{RETRACT}}        return {{SourceName}};
                    {{RETRACT}}    }
                    """;
            }

            private string BuildMethodBody(IEnumerable<string> lines)
            {
                StringBuilder builder = new();
                var actualLines = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();

                for (int i = 0; i < actualLines.Count; i++)
                {
                    if (i == actualLines.Count - 1)
                    {
                        builder.Append($"{RETRACT}       {actualLines[i]}");
                    }
                    else
                    {
                        builder.AppendLine($"{RETRACT}       {actualLines[i]}");
                    }
                }

                return builder.ToString();
            }

            private IEnumerable<string> GetCollectionBeforeAssignmentLines()
            {
                if (!HasSetter || !IsNotifyCollectionChanged)
                {
                    yield break;
                }

                yield return $"global::VeloxDev.MVVM.ObservableCollectionTracker.Unsubscribe(old, On{PropertyName}CollectionChanged);";
                yield return "if (old is not null)";
                yield return "{";
                if (UseWorkflowSlotCollectionLifecycle)
                {
                    yield return $"    foreach (var _slotItem in Enumerate{PropertyName}Items(old))";
                    yield return "    {";
                    yield return "        OnWorkflowSlotRemoved(_slotItem);";
                    yield return "    }";
                }
                yield return $"    OnItemRemovedFrom{PropertyName}(Enumerate{PropertyName}Items(old));";
                yield return "}";
            }

            private IEnumerable<string> GetCollectionAfterAssignmentLines()
            {
                if (!HasSetter || !IsNotifyCollectionChanged)
                {
                    yield break;
                }

                yield return $"global::VeloxDev.MVVM.ObservableCollectionTracker.EnsureSubscribed(value, On{PropertyName}CollectionChanged);";
                yield return "if (value is not null)";
                yield return "{";
                if (UseWorkflowSlotCollectionLifecycle)
                {
                    yield return $"    foreach (var _slotItem in Enumerate{PropertyName}Items(value))";
                    yield return "    {";
                    yield return "        OnWorkflowSlotAdded(_slotItem);";
                    yield return "    }";
                }
                yield return $"    OnItemAddedTo{PropertyName}(Enumerate{PropertyName}Items(value));";
                yield return "}";
            }

            private IEnumerable<string> GetWorkflowSlotBeforeAssignmentLines()
            {
                if (!HasSetter) yield break;

                if (UseSlotEnumeratorLifecycle)
                {
                    yield return "if (old is not null)";
                    yield return "{";
                    yield return "    old.Uninstall();";
                    yield return "}";
                    yield break;
                }

                if (!UseWorkflowSlotLifecycle) yield break;

                yield return "if (old is not null)";
                yield return "{";
                yield return "    OnWorkflowSlotRemoved(old);";
                yield return "}";
            }

            private IEnumerable<string> GetWorkflowSlotAfterAssignmentLines()
            {
                if (!HasSetter) yield break;

                if (UseSlotEnumeratorLifecycle)
                {
                    yield return "if (value is not null)";
                    yield return "{";
                    yield return $"    value.Install(this, \"{PropertyName}\");";
                    yield return "}";
                    yield break;
                }

                if (!UseWorkflowSlotLifecycle) yield break;

                yield return "if (value is not null)";
                yield return "{";
                yield return "    OnWorkflowSlotAdded(value);";
                yield return "}";
            }

            private string GenerateWorkflowSlotMembers()
            {
                return string.Empty;
            }

            private string GenerateCollectionMembers()
            {
                if (!HasSetter || !IsNotifyCollectionChanged)
                {
                    return string.Empty;
                }

                var itemParameterType = string.IsNullOrWhiteSpace(CollectionItemTypeName)
                    ? "global::System.Collections.IEnumerable"
                    : $"global::System.Collections.Generic.IEnumerable<{CollectionItemTypeName}>";

                if (string.IsNullOrWhiteSpace(CollectionItemTypeName))
                {
                    return $$"""
                        {{RETRACT}}private static {{itemParameterType}} Enumerate{{PropertyName}}Items({{NonNullableFullTypeName}} collection)
                        {{RETRACT}}{
                        {{RETRACT}}    return collection;
                        {{RETRACT}}}
                        {{RETRACT}}private static {{itemParameterType}} Enumerate{{PropertyName}}Items(global::System.Collections.IList collection)
                        {{RETRACT}}{
                        {{RETRACT}}    return collection;
                        {{RETRACT}}}
                        {{RETRACT}}private void On{{PropertyName}}CollectionChanged(object? sender, global::System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
                        {{RETRACT}}{
                        {{RETRACT}}    var oldItems = e.OldItems is null ? null : global::System.Linq.Enumerable.Cast<object?>(e.OldItems);
                        {{RETRACT}}    var newItems = e.NewItems is null ? null : global::System.Linq.Enumerable.Cast<object?>(e.NewItems);
                        {{RETRACT}}    OnCollectionChanged(nameof({{PropertyName}}), e, oldItems, newItems);
                        {{RETRACT}}    switch (e.Action)
                        {{RETRACT}}    {
                        {{RETRACT}}        case global::System.Collections.Specialized.NotifyCollectionChangedAction.Add when e.NewItems is not null:
                        {{RETRACT}}            OnItemAddedTo{{PropertyName}}(Enumerate{{PropertyName}}Items(e.NewItems));
                        {{RETRACT}}            break;
                        {{RETRACT}}        case global::System.Collections.Specialized.NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                        {{RETRACT}}            OnItemRemovedFrom{{PropertyName}}(Enumerate{{PropertyName}}Items(e.OldItems));
                        {{RETRACT}}            break;
                        {{RETRACT}}        case global::System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
                        {{RETRACT}}            if (e.OldItems is not null)
                        {{RETRACT}}            {
                        {{RETRACT}}                OnItemRemovedFrom{{PropertyName}}(Enumerate{{PropertyName}}Items(e.OldItems));
                        {{RETRACT}}            }
                        {{RETRACT}}            if (e.NewItems is not null)
                        {{RETRACT}}            {
                        {{RETRACT}}                OnItemAddedTo{{PropertyName}}(Enumerate{{PropertyName}}Items(e.NewItems));
                        {{RETRACT}}            }
                        {{RETRACT}}            break;
                        {{RETRACT}}        case global::System.Collections.Specialized.NotifyCollectionChangedAction.Move when e.NewItems is not null:
                        {{RETRACT}}            OnItemMovedIn{{PropertyName}}(Enumerate{{PropertyName}}Items(e.NewItems));
                        {{RETRACT}}            break;
                        {{RETRACT}}        case global::System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                        {{RETRACT}}            OnItemsResetIn{{PropertyName}}();
                        {{RETRACT}}            break;
                        {{RETRACT}}    }
                        {{RETRACT}}}
                        {{RETRACT}}partial void OnItemAddedTo{{PropertyName}}({{itemParameterType}} items);
                        {{RETRACT}}partial void OnItemRemovedFrom{{PropertyName}}({{itemParameterType}} items);
                        {{RETRACT}}partial void OnItemMovedIn{{PropertyName}}({{itemParameterType}} items);
                        {{RETRACT}}partial void OnItemsResetIn{{PropertyName}}();
                        """;
                }

                var slotLifecycleAdd = UseWorkflowSlotCollectionLifecycle
                    ? $"{RETRACT}        foreach (var _slotItem in Enumerate{PropertyName}Items(e.NewItems))\n{RETRACT}        {{\n{RETRACT}            OnWorkflowSlotAdded(_slotItem);\n{RETRACT}        }}"
                    : string.Empty;

                var slotLifecycleRemove = UseWorkflowSlotCollectionLifecycle
                    ? $"{RETRACT}        foreach (var _slotItem in Enumerate{PropertyName}Items(e.OldItems))\n{RETRACT}        {{\n{RETRACT}            OnWorkflowSlotRemoved(_slotItem);\n{RETRACT}        }}"
                    : string.Empty;

                var slotLifecycleReplaceRemove = UseWorkflowSlotCollectionLifecycle
                    ? $"{RETRACT}            foreach (var _slotItem in Enumerate{PropertyName}Items(e.OldItems))\n{RETRACT}            {{\n{RETRACT}                OnWorkflowSlotRemoved(_slotItem);\n{RETRACT}            }}"
                    : string.Empty;

                var slotLifecycleReplaceAdd = UseWorkflowSlotCollectionLifecycle
                    ? $"{RETRACT}            foreach (var _slotItem in Enumerate{PropertyName}Items(e.NewItems))\n{RETRACT}            {{\n{RETRACT}                OnWorkflowSlotAdded(_slotItem);\n{RETRACT}            }}"
                    : string.Empty;

                return $$"""
                    {{RETRACT}}private static {{itemParameterType}} Enumerate{{PropertyName}}Items({{NonNullableFullTypeName}} collection)
                    {{RETRACT}}{
                    {{RETRACT}}    return global::System.Linq.Enumerable.ToArray(collection);
                    {{RETRACT}}}
                    {{RETRACT}}private static {{itemParameterType}} Enumerate{{PropertyName}}Items(global::System.Collections.IList collection)
                    {{RETRACT}}{
                    {{RETRACT}}    return global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Cast<{{CollectionItemTypeName}}>(collection));
                    {{RETRACT}}}
                    {{RETRACT}}private void On{{PropertyName}}CollectionChanged(object? sender, global::System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
                    {{RETRACT}}{
                    {{RETRACT}}    var oldItems = e.OldItems is null ? null : Enumerate{{PropertyName}}Items(e.OldItems);
                    {{RETRACT}}    var newItems = e.NewItems is null ? null : Enumerate{{PropertyName}}Items(e.NewItems);
                    {{RETRACT}}    OnCollectionChanged(nameof({{PropertyName}}), e, oldItems, newItems);
                    {{RETRACT}}    switch (e.Action)
                    {{RETRACT}}    {
                    {{RETRACT}}        case global::System.Collections.Specialized.NotifyCollectionChangedAction.Add when e.NewItems is not null:
                    {{slotLifecycleAdd}}
                    {{RETRACT}}            OnItemAddedTo{{PropertyName}}(Enumerate{{PropertyName}}Items(e.NewItems));
                    {{RETRACT}}            break;
                    {{RETRACT}}        case global::System.Collections.Specialized.NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                    {{slotLifecycleRemove}}
                    {{RETRACT}}            OnItemRemovedFrom{{PropertyName}}(Enumerate{{PropertyName}}Items(e.OldItems));
                    {{RETRACT}}            break;
                    {{RETRACT}}        case global::System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
                    {{RETRACT}}            if (e.OldItems is not null)
                    {{RETRACT}}            {
                    {{slotLifecycleReplaceRemove}}
                    {{RETRACT}}                OnItemRemovedFrom{{PropertyName}}(Enumerate{{PropertyName}}Items(e.OldItems));
                    {{RETRACT}}            }
                    {{RETRACT}}            if (e.NewItems is not null)
                    {{RETRACT}}            {
                    {{slotLifecycleReplaceAdd}}
                    {{RETRACT}}                OnItemAddedTo{{PropertyName}}(Enumerate{{PropertyName}}Items(e.NewItems));
                    {{RETRACT}}            }
                    {{RETRACT}}            break;
                    {{RETRACT}}        case global::System.Collections.Specialized.NotifyCollectionChangedAction.Move when e.NewItems is not null:
                    {{RETRACT}}            OnItemMovedIn{{PropertyName}}(Enumerate{{PropertyName}}Items(e.NewItems));
                    {{RETRACT}}            break;
                    {{RETRACT}}        case global::System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                    {{RETRACT}}            OnItemsResetIn{{PropertyName}}();
                    {{RETRACT}}            break;
                    {{RETRACT}}    }
                    {{RETRACT}}}
                    {{RETRACT}}partial void OnItemAddedTo{{PropertyName}}({{itemParameterType}} items);
                    {{RETRACT}}partial void OnItemRemovedFrom{{PropertyName}}({{itemParameterType}} items);
                    {{RETRACT}}partial void OnItemMovedIn{{PropertyName}}({{itemParameterType}} items);
                    {{RETRACT}}partial void OnItemsResetIn{{PropertyName}}();
                    """;
            }

        }

        private static bool IsPartialClass(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax classDecl && classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
        }

        private static bool IsNotifyCollectionChangedType(ITypeSymbol typeSymbol)
        {
            if (typeSymbol.ToDisplayString() == "System.Collections.Specialized.INotifyCollectionChanged")
            {
                return true;
            }

            return typeSymbol.AllInterfaces.Any(i => i.ToDisplayString() == "System.Collections.Specialized.INotifyCollectionChanged");
        }

        private static string? GetCollectionItemTypeName(ITypeSymbol typeSymbol)
        {
            if (GetGenericEnumerableInterface(typeSymbol) is not INamedTypeSymbol enumerableInterface ||
                enumerableInterface.TypeArguments.Length == 0)
            {
                return null;
            }

            return GetFullyQualifiedTypeName(enumerableInterface.TypeArguments[0]);
        }

        private static INamedTypeSymbol? GetGenericEnumerableInterface(ITypeSymbol typeSymbol)
        {
            if (typeSymbol is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            {
                return namedType;
            }

            return typeSymbol.AllInterfaces
                .OfType<INamedTypeSymbol>()
                .FirstOrDefault(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
        }

        private static string GetFullyQualifiedTypeName(ITypeSymbol typeSymbol)
        {
            var displayFormat = new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

            return typeSymbol.ToDisplayString(displayFormat);
        }

    }

    /// <summary>
    /// Controls how generated property setters delegate to the host MVVM framework's native mechanism.
    /// </summary>
    public enum SetterMode
    {
        /// <summary>Direct field assignment with <c>Object.Equals</c> equality check.</summary>
        Default,
        /// <summary>Delegates to <c>SetProperty&lt;T&gt;(ref T, T, string)</c> (CommunityToolkit.Mvvm, Prism).</summary>
        FrameworkSetProperty,
        /// <summary>Delegates to <c>this.RaiseAndSetIfChanged&lt;T&gt;(ref T, T, string)</c> (ReactiveUI).</summary>
        FrameworkRaiseAndSet,
        /// <summary>Uses <c>NotifyOfPropertyChange(string)</c> for the changed notification (Caliburn.Micro).</summary>
        FrameworkNotifyOfPropertyChange,
    }
}
