using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace VeloxDev.WPF.Generator
{
    internal static class AnalizeHelper
    {
        const string FULLNAME_HOTKEY = "global::VeloxDev.WPF.SourceGeneratorMark.HotKeyComponentAttribute";
        const string NAME_ASPECTORIENTED = "AspectOriented";

        internal static bool IsPartialClass(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax classDecl && classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
        }
        internal static bool IsAopClass(ClassDeclarationSyntax classDecl)
        {
            return classDecl.Members
                    .OfType<MemberDeclarationSyntax>()
                    .Any(member => member.AttributeLists
                    .SelectMany(al => al.Attributes)
                    .Any(attr => attr.Name.ToString() == NAME_ASPECTORIENTED));
        }
        internal static bool IsHotKeyClass(INamedTypeSymbol namedTypeSymbol)
        {
            return namedTypeSymbol.GetAttributes().Any(attr => attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == FULLNAME_HOTKEY);
        }
        internal static bool IsViewModelClass(INamedTypeSymbol classSymbol, out IEnumerable<IFieldSymbol> fieldSymbols)
        {
            fieldSymbols = classSymbol.GetMembers()
                    .OfType<IFieldSymbol>()
                    .Where(field => field.GetAttributes().Any(attr => attr.AttributeClass?.Name == "ObservableAttribute"));
            return fieldSymbols.Any();
        }

        internal static INamedTypeSymbol? FindTargetTypeSymbol(
        Compilation compilation,
        string typeName,
        string namespaceValidation)
        {
            if (string.IsNullOrEmpty(namespaceValidation))
            {
                return compilation.GetSymbolsWithName(typeName, SymbolFilter.Type)
                    .OfType<INamedTypeSymbol>()
                    .FirstOrDefault();
            }
            else
            {
                var fullName = $"{namespaceValidation}.{typeName}";
                return compilation.GetTypeByMetadataName(fullName) as INamedTypeSymbol;
            }
        }

        internal static ClassDeclarationSyntax GetClassDeclaration(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            return classDeclaration;
        }

        /// <summary>
        /// 过滤非partial类以确保可使用源生成器
        /// </summary>
        internal static IncrementalValuesProvider<ClassDeclarationSyntax> DefiningFilter(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValuesProvider<ClassDeclarationSyntax> classDeclarations =
                context.SyntaxProvider.CreateSyntaxProvider(
                    predicate: static (node, cancellationToken) => AnalizeHelper.IsPartialClass(node),
                    transform: static (context, cancellationToken) => AnalizeHelper.GetClassDeclaration(context))
                .Where(static m => m != null)!;
            return classDeclarations;
        }
        /// <summary>
        /// 获取最终用以分析项目的数据
        /// </summary>
        internal static IncrementalValueProvider<(Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> Classes)> GetValue(IncrementalGeneratorInitializationContext context, IncrementalValuesProvider<ClassDeclarationSyntax> classDeclarations)
        {
            IncrementalValueProvider<(Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> Classes)> compilationAndClasses =
                context.CompilationProvider.Combine(classDeclarations.Collect());
            return compilationAndClasses;
        }

        /// <summary>
        /// 获取类所指向的AOP接口
        /// </summary>
        internal static string GetInterfaceName(ClassDeclarationSyntax classDeclarationSyntax)
        {
            return $"{classDeclarationSyntax.Identifier.Text}_{GetNamespace(classDeclarationSyntax).Replace('.', '_')}_Aop";
        }

        internal static string GetPropertyNameByFieldName(VariableDeclaratorSyntax variable)
        {
            if (variable.Identifier.Text.StartsWith("_"))
            {
                return char.ToUpper(variable.Identifier.Text[1]) + variable.Identifier.Text.Substring(2);
            }
            else
            {
                return char.ToUpper(variable.Identifier.Text[0]) + variable.Identifier.Text.Substring(1);
            }
        }

        /// <summary>
        /// 解析可访问性
        /// </summary>
        internal static string GetAccessModifier(INamedTypeSymbol symbol)
        {
            switch (symbol.DeclaredAccessibility)
            {
                case Accessibility.Public:
                    return "public";
                case Accessibility.Internal:
                    return "internal";
                case Accessibility.Private:
                    return "private";
                case Accessibility.Protected:
                    return "protected";
                case Accessibility.ProtectedAndInternal:
                    return "protected internal";
                case Accessibility.ProtectedOrInternal:
                    return "private protected";
                default:
                    return "internal";
            }
        }

        internal static string GetViewFileName(INamedTypeSymbol symbol, ClassDeclarationSyntax classDeclarationSyntax)
        {
            return $"{classDeclarationSyntax.Identifier.Text}_{symbol.ContainingNamespace.ToString().Replace('.', '_')}_View.g.cs";
        }
        internal static string GetViewModelFileName(INamedTypeSymbol symbol, ClassDeclarationSyntax classDeclarationSyntax)
        {
            return $"{classDeclarationSyntax.Identifier.Text}_{symbol.ContainingNamespace.ToString().Replace('.', '_')}_ViewModel.g.cs";
        }

        internal static string GetDefaultInitialText(string fullTypeName)
        {
            return fullTypeName switch
            {
                "global::System.Windows.Media.Brush" => "global::System.Windows.Media.Brushes.Transparent",
                "double" => "0.0",
                "global::System.Windows.Thickness" => "new global::System.Windows.Thickness(0)",
                "global::System.Windows.Media.Transform" => "global::System.Windows.Media.Transform.Identity",
                "global::System.Windows.CornerRadius" => "new global::System.Windows.CornerRadius(0)",
                "global::System.Windows.Point" => "new global::System.Windows.Point(0,0)",
                _ => $"default{fullTypeName}"
            };
        }

        /// <summary>
        /// 从主题特性原始文本中提取主题名称
        /// </summary>
        internal static string ExtractThemeName(string fullAttributeText)
        {
            // 步骤1：分割参数部分
            int firstParen = fullAttributeText.IndexOf('(');
            string withoutArgs = (firstParen >= 0) ?
                fullAttributeText.Substring(0, firstParen) :
                fullAttributeText;

            // 步骤2：定位最后一个命名空间分隔符
            int lastDot = withoutArgs.LastIndexOf('.');
            int lastColon = withoutArgs.LastIndexOf("::");
            string candidate = (lastDot >= 0) ?
                withoutArgs.Substring(lastDot + 1) :
                (lastColon >= 0) ?
                withoutArgs.Substring(lastColon + 2) :
                withoutArgs;

            // 步骤3：严格校验名称规范
            candidate = candidate.Trim();
            return candidate;
        }

        private static string GetNamespace(ClassDeclarationSyntax classDeclaration)
        {
            SyntaxNode? current = classDeclaration;
            while (current != null && current is not NamespaceDeclarationSyntax)
            {
                current = current.Parent;
            }

            return current is NamespaceDeclarationSyntax namespaceDeclaration
                ? namespaceDeclaration.Name.ToString()
                : "Global";
        }

        internal static string ReplaceBrushes(this string source)
        {
            var regex = new System.Text.RegularExpressions.Regex(@"(?:\b\w+\.)*Brushes\.(\w+)\b");
            return regex.Replace(source, match =>
            {
                var brushName = match.Groups[1].Value;
                return match.Value.Contains("System.Windows.Media.Brushes.") ? match.Value : $"global::System.Windows.Media.Brushes.{brushName}";
            });
        }
        internal static string ReplaceGradientSpreadMethods(this string source)
        {
            var regex = new System.Text.RegularExpressions.Regex(@"(?:\b\w+\.)*GradientSpreadMethod\.(\w+)\b");
            return regex.Replace(source, match =>
            {
                var methodName = match.Groups[1].Value;
                return match.Value.Contains("System.Windows.Media.GradientSpreadMethod.")
                    ? match.Value
                    : $"global::System.Windows.Media.GradientSpreadMethod.{methodName}";
            });
        }
    }
}
