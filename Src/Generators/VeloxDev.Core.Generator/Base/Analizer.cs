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
                // 检查是否为可空值类型 (Nullable<T>)
                if (typeSymbol is INamedTypeSymbol namedType &&
                    namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                {
                    return true;
                }

                // 检查是否为可空引用类型 (在Nullable上下文中的引用类型)
                if (typeSymbol.NullableAnnotation == NullableAnnotation.Annotated)
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

        public class MVVMPropertyFactory
        {
            const string RETRACT = "   ";

            public MVVMPropertyFactory(MVVMFieldAnalizer fieldRoslyn, string modifies, bool isView)
            {
                Modifies = modifies;
                FullTypeName = fieldRoslyn.TypeName;
                SourceName = fieldRoslyn.FieldName;
                PropertyName = fieldRoslyn.PropertyName;
                IsNullable = fieldRoslyn.IsNullable;
                IsView = isView;
            }

            public MVVMPropertyFactory(string modifies, string fullTypeName, string sourceName, string propertyName, bool isView)
            {
                Modifies = modifies;
                FullTypeName = fullTypeName;
                SourceName = sourceName;
                PropertyName = propertyName;
                IsNullable = fullTypeName.EndsWith("?");
                IsView = isView;
            }

            public string Modifies { get; private set; }
            public string FullTypeName { get; private set; }
            public string SourceName { get; private set; }
            public string PropertyName { get; private set; }
            public bool IsNullable { get; private set; }

            public List<string> SetteringBody { get; set; } = [];
            public List<string> SetteredBody { get; set; } = [];
            public List<string> AttributeBody { get; set; } = [];

            private bool IsView { get; set; }

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

                // 使用EqualityComparer<T>.Default来处理可空类型的比较
                var baseTypeName = IsNullable ? FullTypeName.TrimEnd('?') : FullTypeName;
                var equalityComparison = IsNullable ?
                    $"global::System.Collections.Generic.EqualityComparer<{baseTypeName}>.Default.Equals({SourceName}, value)" :
                    $"global::System.Object.Equals({SourceName}, value)";

                return $$"""
        {{RETRACT}}{{Modifies}} {{FullTypeName}} {{PropertyName}}
        {{RETRACT}}{
        {{RETRACT}}    get => {{SourceName}};
        {{RETRACT}}    set
        {{RETRACT}}    {
        {{RETRACT}}       if({{equalityComparison}}) return;
        {{RETRACT}}       var old = {{SourceName}};
        {{setteringBody}}
        {{RETRACT}}       On{{PropertyName}}Changing(old, value);
        {{RETRACT}}       {{SourceName}} = value;
        {{RETRACT}}       On{{PropertyName}}Changed(old, value);
        {{setteredBody}}
        {{RETRACT}}    }
        {{RETRACT}}}
        {{RETRACT}}partial void On{{PropertyName}}Changing({{FullTypeName}} oldValue, {{FullTypeName}} newValue);
        {{RETRACT}}partial void On{{PropertyName}}Changed({{FullTypeName}} oldValue, {{FullTypeName}} newValue);
        """;
            }

            public string Generate()
            {
                return IsView ? GenerateProxy() : GenerateViewModel();
            }

            public string GenerateProxy()
            {
                var attributeBody = new StringBuilder();
                for (int i = 0; i < AttributeBody.Count; i++)
                {
                    if (i == AttributeBody.Count - 1)
                    {
                        attributeBody.Append($"{RETRACT}[{AttributeBody[i]}]");
                    }
                    else
                    {
                        attributeBody.AppendLine($"{RETRACT}[{AttributeBody[i]}]");
                    }
                }

                return $$"""

        {{attributeBody}}
        {{RETRACT}}{{Modifies}} {{FullTypeName}} {{PropertyName}}
        {{RETRACT}}{
        {{RETRACT}}    get => {{SourceName}};
        {{RETRACT}}    set => {{SourceName}} = value;
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
