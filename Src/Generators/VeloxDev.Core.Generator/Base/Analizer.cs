using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace VeloxDev.Core.Generator.Base
{
    public static class Analizer
    {
        public static class Filters
        {
            public static IncrementalValueProvider<(Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> Classes)> FilterContext(IncrementalGeneratorInitializationContext context)
            {
                IncrementalValuesProvider<ClassDeclarationSyntax> classDeclarations =
                    context.SyntaxProvider.CreateSyntaxProvider(
                        predicate: static (node, cancellationToken) => IsPartialClass(node),
                        transform: static (context, cancellationToken) => GetClassDeclaration(context))
                    .Where(static m => m != null)!;
                return GetFilteredValue(context, classDeclarations);
            }
        }

        public sealed class MVVMFieldAnalizer
        {
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

            public IFieldSymbol Symbol { get; private set; }
            public string TypeName { get; private set; } = string.Empty;
            public string FieldName { get; private set; } = string.Empty;
            public string PropertyName { get; private set; } = string.Empty;
            public bool IsNullable { get; private set; }
            public bool IsNotifyCollectionChanged { get; private set; }
            public string? CollectionItemTypeName { get; private set; }

            private static string GetFullyQualifiedTypeName(ITypeSymbol typeSymbol)
            {
                var displayFormat = new SymbolDisplayFormat(
                    typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                    genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                    miscellaneousOptions: SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

                return typeSymbol.ToDisplayString(displayFormat);
            }

            private static bool IsNullableType(ITypeSymbol typeSymbol)
            {
                if (typeSymbol is INamedTypeSymbol namedType &&
                    namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                {
                    return true;
                }

                return typeSymbol.NullableAnnotation == NullableAnnotation.Annotated;
            }

            private static string GetPropertyNameFromFieldName(string fieldName)
            {
                if (fieldName.StartsWith("_"))
                {
                    return char.ToUpper(fieldName[1]) + fieldName.Substring(2);
                }
                else
                {
                    return char.ToUpper(fieldName[0]) + fieldName.Substring(1);
                }
            }
        }

        public sealed class MVVMPropertyAnalizer
        {
            public const string NAME_VP = "VeloxProperty";

            internal MVVMPropertyAnalizer(IPropertySymbol propertySymbol)
            {
                Symbol = propertySymbol;
                TypeName = GetFullyQualifiedTypeName(propertySymbol.Type);
                PropertyName = propertySymbol.Name;
                FieldName = GetFieldNameFromPropertyName(propertySymbol.Name);
                IsNullable = IsNullableType(propertySymbol.Type);

                // 获取属性本身的访问修饰符
                PropertyAccessModifier = GetPropertyAccessModifier(propertySymbol);

                // 获取访问器的访问修饰符（相对于属性级别的）
                GetterAccessModifier = GetAccessorAccessModifier(propertySymbol, isGetter: true);
                SetterAccessModifier = GetAccessorAccessModifier(propertySymbol, isGetter: false);

                HasGetter = propertySymbol.GetMethod != null;
                HasSetter = propertySymbol.SetMethod != null;
                IsPartial = IsPartialProperty(propertySymbol);
                IsNotifyCollectionChanged = IsNotifyCollectionChangedType(propertySymbol.Type);
                CollectionItemTypeName = GetCollectionItemTypeName(propertySymbol.Type);
            }

            public IPropertySymbol Symbol { get; private set; }
            public string TypeName { get; private set; } = string.Empty;
            public string PropertyName { get; private set; } = string.Empty;
            public string FieldName { get; private set; } = string.Empty;
            public bool IsNullable { get; private set; }
            public bool HasGetter { get; private set; }
            public bool HasSetter { get; private set; }
            public bool IsPartial { get; private set; }
            public bool IsNotifyCollectionChanged { get; private set; }
            public string? CollectionItemTypeName { get; private set; }
            public string PropertyAccessModifier { get; private set; } = "public";
            public string GetterAccessModifier { get; private set; } = string.Empty;
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

                // 只有当访问器的可访问性与属性不同时才需要指定修饰符
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

            private static string GetFullyQualifiedTypeName(ITypeSymbol typeSymbol)
            {
                var displayFormat = new SymbolDisplayFormat(
                    typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                    genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                    miscellaneousOptions: SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

                return typeSymbol.ToDisplayString(displayFormat);
            }

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

            // 从字段构造
            public MVVMPropertyFactory(MVVMFieldAnalizer fieldAnalizer, string modifies, bool isView)
            {
                Modifies = modifies;
                FullTypeName = fieldAnalizer.TypeName;
                SourceName = fieldAnalizer.FieldName;
                PropertyName = fieldAnalizer.PropertyName;
                IsNullable = fieldAnalizer.IsNullable;
                IsView = isView;
                IsFromField = true;
                HasGetter = true;
                HasSetter = true;
                IsPartial = false;
                IsNotifyCollectionChanged = fieldAnalizer.IsNotifyCollectionChanged;
                CollectionItemTypeName = fieldAnalizer.CollectionItemTypeName;
                PropertyAccessModifier = "public";
                GetterAccessModifier = string.Empty;
                SetterAccessModifier = string.Empty;
            }

            // 从属性构造
            public MVVMPropertyFactory(MVVMPropertyAnalizer propertyAnalizer, string modifies, bool isView)
            {
                Modifies = modifies;
                FullTypeName = propertyAnalizer.TypeName;
                SourceName = propertyAnalizer.FieldName;
                PropertyName = propertyAnalizer.PropertyName;
                IsNullable = propertyAnalizer.IsNullable;
                IsView = isView;
                IsFromField = false;
                HasGetter = propertyAnalizer.HasGetter;
                HasSetter = propertyAnalizer.HasSetter;
                IsPartial = propertyAnalizer.IsPartial;
                IsNotifyCollectionChanged = propertyAnalizer.IsNotifyCollectionChanged;
                CollectionItemTypeName = propertyAnalizer.CollectionItemTypeName;
                PropertyAccessModifier = propertyAnalizer.PropertyAccessModifier;
                GetterAccessModifier = propertyAnalizer.GetterAccessModifier;
                SetterAccessModifier = propertyAnalizer.SetterAccessModifier;
            }

            public string Modifies { get; private set; }
            public string FullTypeName { get; private set; }
            public string SourceName { get; private set; }
            public string PropertyName { get; private set; }
            public bool IsNullable { get; private set; }
            public bool IsFromField { get; private set; }
            public bool HasGetter { get; private set; }
            public bool HasSetter { get; private set; }
            public bool IsPartial { get; private set; }
            public bool IsNotifyCollectionChanged { get; private set; }
            public string? CollectionItemTypeName { get; private set; }
            public string PropertyAccessModifier { get; private set; }
            public string GetterAccessModifier { get; private set; }
            public string SetterAccessModifier { get; private set; }
            public bool UseWorkflowSlotLifecycle { get; set; }
            public bool UseWorkflowSlotAutoCreation { get; set; }

            public List<string> SetteringBody { get; set; } = [];
            public List<string> SetteredBody { get; set; } = [];

            private bool IsView { get; set; }

            private string NonNullableFullTypeName => FullTypeName.EndsWith("?") ? FullTypeName.Substring(0, FullTypeName.Length - 1) : FullTypeName;

            public string GenerateFieldDeclaration()
            {
                if (IsFromField) return string.Empty;

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

            public string GenerateViewModel()
            {
                var equalityComparison = $"global::System.Object.Equals({SourceName}, value)";

                // 生成属性访问器，完全保留用户写的修饰符
                var getter = HasGetter ? GenerateGetter() : string.Empty;

                string setter;
                if (HasSetter)
                {
                    var setterAccessModifier = !string.IsNullOrEmpty(SetterAccessModifier) ? SetterAccessModifier + " " : "";
                    List<string> setterLines =
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

                // 如果是部分属性，添加partial修饰符
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

                if (!UseWorkflowSlotLifecycle || IsNullable || !UseWorkflowSlotAutoCreation)
                {
                    return $"{RETRACT}    {getterAccessModifier}get => {SourceName};";
                }

                var getterBody = BuildMethodBody(GetWorkflowSlotGetterLines());
                return $$"""
                    {{RETRACT}}    {{getterAccessModifier}}get
                    {{RETRACT}}    {
                    {{getterBody}}
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

                yield return $"if (old is global::System.Collections.Specialized.INotifyCollectionChanged oldCollection)";
                yield return "{";
                yield return $"    oldCollection.CollectionChanged -= On{PropertyName}CollectionChanged;";
                yield return "}";
                yield return "if (old is not null)";
                yield return "{";
                yield return $"    OnItemRemovedFrom{PropertyName}(Enumerate{PropertyName}Items(old));";
                yield return "}";
            }

            private IEnumerable<string> GetCollectionAfterAssignmentLines()
            {
                if (!HasSetter || !IsNotifyCollectionChanged)
                {
                    yield break;
                }

                yield return $"if (value is global::System.Collections.Specialized.INotifyCollectionChanged newCollection)";
                yield return "{";
                yield return $"    newCollection.CollectionChanged += On{PropertyName}CollectionChanged;";
                yield return "}";
                yield return "if (value is not null)";
                yield return "{";
                yield return $"    OnItemAddedTo{PropertyName}(Enumerate{PropertyName}Items(value));";
                yield return "}";
            }

            private IEnumerable<string> GetWorkflowSlotBeforeAssignmentLines()
            {
                if (!HasSetter || !UseWorkflowSlotLifecycle)
                {
                    yield break;
                }

                yield return "if (old is not null)";
                yield return "{";
                yield return "    old.DeleteCommand.Execute(null);";
                yield return "}";
            }

            private IEnumerable<string> GetWorkflowSlotAfterAssignmentLines()
            {
                if (!HasSetter || !UseWorkflowSlotLifecycle)
                {
                    yield break;
                }

                yield return "if (value is not null)";
                yield return "{";
                yield return "    CreateSlotCommand.Execute(value);";
                yield return "}";
            }

            private IEnumerable<string> GetWorkflowSlotGetterLines()
            {
                yield return $"if ({SourceName} is null)";
                yield return "{";
                yield return $"    {SourceName} = Create{PropertyName}WorkflowSlot();";
                yield return $"    CreateSlotCommand.Execute({SourceName});";
                yield return "}";
                yield return $"return {SourceName};";
            }

            private string GenerateWorkflowSlotMembers()
            {
                if (!UseWorkflowSlotLifecycle || IsNullable || !UseWorkflowSlotAutoCreation)
                {
                    return string.Empty;
                }

                return $$"""
                    {{RETRACT}}private {{FullTypeName}} Create{{PropertyName}}WorkflowSlot()
                    {{RETRACT}}{
                    {{RETRACT}}    try
                    {{RETRACT}}    {
                    {{RETRACT}}        if (global::System.Activator.CreateInstance(typeof({{NonNullableFullTypeName}}), true) is {{FullTypeName}} created)
                    {{RETRACT}}        {
                    {{RETRACT}}            return created;
                    {{RETRACT}}        }
                    {{RETRACT}}    }
                    {{RETRACT}}    catch
                    {{RETRACT}}    {
                    {{RETRACT}}    }
                    {{RETRACT}}    return ({{FullTypeName}})(object)new global::VeloxDev.Core.WorkflowSystem.Templates.SlotViewModelBase();
                    {{RETRACT}}}
                    """;
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

            public string Generate()
            {
                return IsView ? GenerateProxy() : GenerateViewModel();
            }

            public string GenerateProxy()
            {
                // 生成属性访问器，完全保留用户写的修饰符
                var getter = HasGetter ?
                    $"{RETRACT}    {(!string.IsNullOrEmpty(GetterAccessModifier) ? GetterAccessModifier + " " : "")}get => {SourceName};" :
                    string.Empty;

                var setter = HasSetter ?
                    $"{RETRACT}    {(!string.IsNullOrEmpty(SetterAccessModifier) ? SetterAccessModifier + " " : "")}set => {SourceName} = value;" :
                    string.Empty;

                // 如果是部分属性，添加partial修饰符
                var partialModifier = IsPartial ? "partial " : string.Empty;

                return $$"""
                    {{RETRACT}}{{PropertyAccessModifier}} {{partialModifier}}{{FullTypeName}} {{PropertyName}}
                    {{RETRACT}}{
                    {{getter}}
                    {{setter}}
                    {{RETRACT}}}
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

        private static IncrementalValueProvider<(Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> Classes)> GetFilteredValue(IncrementalGeneratorInitializationContext context, IncrementalValuesProvider<ClassDeclarationSyntax> classDeclarations)
        {
            IncrementalValueProvider<(Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> Classes)> compilationAndClasses =
                context.CompilationProvider.Combine(classDeclarations.Collect());
            return compilationAndClasses;
        }

        private static ClassDeclarationSyntax GetClassDeclaration(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            return classDeclaration;
        }
    }
}