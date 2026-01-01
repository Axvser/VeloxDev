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
            }

            public IFieldSymbol Symbol { get; private set; }
            public string TypeName { get; private set; } = string.Empty;
            public string FieldName { get; private set; } = string.Empty;
            public string PropertyName { get; private set; } = string.Empty;
            public bool IsNullable { get; private set; }

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
            }

            public IPropertySymbol Symbol { get; private set; }
            public string TypeName { get; private set; } = string.Empty;
            public string PropertyName { get; private set; } = string.Empty;
            public string FieldName { get; private set; } = string.Empty;
            public bool IsNullable { get; private set; }
            public bool HasGetter { get; private set; }
            public bool HasSetter { get; private set; }
            public bool IsPartial { get; private set; }
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
            public string PropertyAccessModifier { get; private set; }
            public string GetterAccessModifier { get; private set; }
            public string SetterAccessModifier { get; private set; }

            public List<string> SetteringBody { get; set; } = [];
            public List<string> SetteredBody { get; set; } = [];

            private bool IsView { get; set; }

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
                var setteringBody = new StringBuilder();
                for (int i = 0; i < SetteringBody.Count; i++)
                {
                    if (i == SetteringBody.Count - 1)
                    {
                        setteringBody.Append($"{RETRACT}       {SetteringBody[i]}");
                    }
                    else
                    {
                        setteringBody.AppendLine($"{RETRACT}       {SetteringBody[i]}");
                    }
                }

                var setteredBody = new StringBuilder();
                for (int i = 0; i < SetteredBody.Count; i++)
                {
                    if (i == SetteredBody.Count - 1)
                    {
                        setteredBody.Append($"{RETRACT}       {SetteredBody[i]}");
                    }
                    else
                    {
                        setteredBody.AppendLine($"{RETRACT}       {SetteredBody[i]}");
                    }
                }

                var baseTypeName = IsNullable ? FullTypeName.TrimEnd('?') : FullTypeName;
                var equalityComparison = IsNullable ?
                    $"global::System.Collections.Generic.EqualityComparer<{baseTypeName}>.Default.Equals({SourceName}, value)" :
                    $"global::System.Object.Equals({SourceName}, value)";

                // 生成属性访问器，完全保留用户写的修饰符
                var getter = HasGetter ?
                    $"{RETRACT}    {(!string.IsNullOrEmpty(GetterAccessModifier) ? GetterAccessModifier + " " : "")}get => {SourceName};" :
                    string.Empty;

                string setter;
                if (HasSetter)
                {
                    var setterAccessModifier = !string.IsNullOrEmpty(SetterAccessModifier) ? SetterAccessModifier + " " : "";
                    setter = $$"""
                        {{RETRACT}}    {{setterAccessModifier}}set
                        {{RETRACT}}    {
                        {{RETRACT}}       if({{equalityComparison}}) return;
                        {{RETRACT}}       var old = {{SourceName}};
                        {{setteringBody}}
                        {{RETRACT}}       On{{PropertyName}}Changing(old, value);
                        {{RETRACT}}       {{SourceName}} = value;
                        {{RETRACT}}       On{{PropertyName}}Changed(old, value);
                        {{setteredBody}}
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

                return $$"""
                    {{RETRACT}}{{PropertyAccessModifier}} {{partialModifier}}{{FullTypeName}} {{PropertyName}}
                    {{RETRACT}}{
                    {{getter}}
                    {{setter}}
                    {{RETRACT}}}
                    {{changingMethod}}
                    {{changedMethod}}
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