using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;

namespace VeloxDev.Core.Generator
{
    [Generator(LanguageNames.CSharp)]
    public class Theme : IIncrementalGenerator
    {
        private const string ThemeManagerFullName = "global::VeloxDev.Core.DynamicTheme.ThemeManager";
        private const string IThemeFullName = "global::VeloxDev.Core.Interfaces.DynamicTheme.ITheme";
        private const string IThemeObjectFullName = "global::VeloxDev.Core.Interfaces.DynamicTheme.IThemeObject";
        private const string IThemeValueConverterFullName = "global::VeloxDev.Core.Interfaces.DynamicTheme.IThemeValueConverter";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            static (ClassDeclarationSyntax Syntax, INamedTypeSymbol Symbol) Transform(
                GeneratorAttributeSyntaxContext context, CancellationToken token)
            {
                var classSyntax = (ClassDeclarationSyntax)context.TargetNode;
                var symbol = context.SemanticModel.GetDeclaredSymbol(classSyntax, token) as INamedTypeSymbol;
                return (classSyntax!, symbol!);
            }

            var classWith3Params = context.SyntaxProvider.ForAttributeWithMetadataName(
                "VeloxDev.Core.DynamicTheme.ThemeConfigAttribute`3",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: Transform).Collect();

            var classWith4Params = context.SyntaxProvider.ForAttributeWithMetadataName(
                "VeloxDev.Core.DynamicTheme.ThemeConfigAttribute`4",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: Transform).Collect();

            var classWith5Params = context.SyntaxProvider.ForAttributeWithMetadataName(
                "VeloxDev.Core.DynamicTheme.ThemeConfigAttribute`5",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: Transform).Collect();

            var classWith6Params = context.SyntaxProvider.ForAttributeWithMetadataName(
                "VeloxDev.Core.DynamicTheme.ThemeConfigAttribute`6",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: Transform).Collect();

            var classWith7Params = context.SyntaxProvider.ForAttributeWithMetadataName(
                "VeloxDev.Core.DynamicTheme.ThemeConfigAttribute`7",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: Transform).Collect();

            var allClasses = classWith3Params
                .Combine(classWith4Params)
                .Combine(classWith5Params)
                .Combine(classWith6Params)
                .Combine(classWith7Params)
                .Select((combined, _) =>
                {
                    var ((((classes3, classes4), classes5), classes6), classes7) = combined;

                    var allItems = new List<(ClassDeclarationSyntax Syntax, INamedTypeSymbol Symbol)>();
                    allItems.AddRange(classes3);
                    allItems.AddRange(classes4);
                    allItems.AddRange(classes5);
                    allItems.AddRange(classes6);
                    allItems.AddRange(classes7);

                    var uniqueItems = new Dictionary<INamedTypeSymbol, (ClassDeclarationSyntax, INamedTypeSymbol)>(SymbolEqualityComparer.Default);
                    foreach (var item in allItems)
                    {
                        if (!uniqueItems.ContainsKey(item.Symbol))
                        {
                            uniqueItems.Add(item.Symbol, item);
                        }
                    }

                    return uniqueItems.Values.ToImmutableArray();
                });

            context.RegisterSourceOutput(allClasses, (spc, classes) =>
            {
                foreach (var item in classes)
                {
                    var (syntax, symbol) = item;
                    var attributes = symbol.GetAttributes()
                        .Where(ad => ad.AttributeClass?.Name.StartsWith("ThemeConfigAttribute") ?? false)
                        .ToList();

                    if (!attributes.Any()) continue;

                    var sourceCode = GenerateThemeConfigClass(symbol, attributes);
                    if (!string.IsNullOrEmpty(sourceCode))
                    {
                        spc.AddSource($"{symbol.Name}_{symbol.ContainingNamespace.ToString().Replace(".", "_")}_ThemeConfig.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
                    }
                }
            });
        }

        private string GenerateThemeConfigClass(INamedTypeSymbol classSymbol, IEnumerable<AttributeData> attributes)
        {
            var namespaceName = classSymbol.ContainingNamespace.ToDisplayString();
            var className = classSymbol.Name;
            var isPartial = classSymbol.DeclaringSyntaxReferences
                .Any(r => ((ClassDeclarationSyntax)r.GetSyntax()).Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));

            if (!isPartial)
            {
                return string.Empty;
            }

            var converterInstances = new Dictionary<string, string>();
            var propertyConverters = new Dictionary<string, string>();
            var propertyInfos = new Dictionary<string, string>();
            var themeCache = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

            var converterTypeToKey = new Dictionary<string, string>(StringComparer.Ordinal);
            int converterIndex = 0;

            foreach (var attribute in attributes)
            {
                // 1. 安全获取属性名（第一个构造函数参数）
                var propertyName = attribute.ConstructorArguments[0].Value?.ToString();
                if (string.IsNullOrEmpty(propertyName))
                    continue;

                // 2. 获取转换器类型（第一个泛型参数）
                var converterType = attribute.AttributeClass?.TypeArguments[0];
                if (converterType == null)
                    continue;
                var converterTypeName = converterType.ToDisplayString();

                // 3. 获取属性符号（支持继承链查找）
                var propertySymbol = classSymbol.GetMembers(propertyName!)
                    .OfType<IPropertySymbol>()
                    .FirstOrDefault();

                // 递归查找基类属性
                var baseType = classSymbol.BaseType;
                while (propertySymbol == null && baseType != null)
                {
                    propertySymbol = baseType.GetMembers(propertyName!)
                        .OfType<IPropertySymbol>()
                        .FirstOrDefault();
                    baseType = baseType.BaseType;
                }

                if (propertySymbol == null)
                {
                    // 可以在此处添加诊断警告
                    continue;
                }

                // 4. 获取属性的完整类型信息（支持泛型、数组等）
                var propertyTypeName = propertySymbol.Type.ToDisplayString(
                    new SymbolDisplayFormat(
                        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes));

                // 5. 处理转换器实例缓存
                if (!converterTypeToKey.TryGetValue(converterTypeName, out var converterKey))
                {
                    converterKey = $"{converterTypeName.Replace(".", "_")}__Converter__{converterIndex++}";
                    converterTypeToKey[converterTypeName] = converterKey;

                    converterInstances[converterKey] =
                        $"private static readonly {IThemeValueConverterFullName} {converterKey} = " +
                        $"({IThemeValueConverterFullName})global::System.Activator.CreateInstance(typeof(global::{converterTypeName}))!;";
                }

                // 6. 收集属性元数据
                propertyConverters[propertyName!] = converterKey;
                propertyInfos[propertyName!] =
                    $"{{ nameof({propertyName}), typeof(global::{classSymbol.ToDisplayString()}).GetProperty(nameof({propertyName}))! }}";

                // 7. 处理主题上下文参数（从第二个构造函数参数开始）
                var themeContexts = new List<string>();
                for (int i = 1; i < attribute.ConstructorArguments.Length; i++)
                {
                    var contextArg = attribute.ConstructorArguments[i];
                    if (contextArg.IsNull)
                        continue;

                    var contextElements = new List<string>();
                    foreach (var value in contextArg.Values)
                    {
                        contextElements.Add(value.Kind switch
                        {
                            TypedConstantKind.Type =>
                                $"typeof(global::{((INamedTypeSymbol?)value.Value)?.ToDisplayString() ?? "object"})",
                            TypedConstantKind.Enum =>
                                $"(global::{((INamedTypeSymbol?)value.Type)?.ToDisplayString() ?? "int"})({value.Value})",
                            _ =>
                                value.Value is string str ?
                                    $"\"{str.Replace("\"", "\\\"")}\"" :
                                    value.Value?.ToString() ?? "null"
                        });
                    }

                    themeContexts.Add($"[{string.Join(", ", contextElements)}]");
                }

                // 8. 获取主题类型（从第二个泛型参数开始）
                var themeTypes = new List<string>();
                for (int i = 1; i < attribute.AttributeClass?.TypeArguments.Length; i++)
                {
                    var themeType = attribute.AttributeClass.TypeArguments[i];
                    if (themeType != null)
                    {
                        themeTypes.Add(themeType.ToDisplayString());
                    }
                }

                // 9. 初始化三层字典结构
                if (!themeCache.TryGetValue(propertyName!, out var propertyCache))
                {
                    propertyCache = [];
                    themeCache[propertyName!] = propertyCache;
                }

                if (!propertyCache.TryGetValue(propertyName!, out var typeCache))
                {
                    typeCache = [];
                    propertyCache[propertyName!] = typeCache;
                }

                // 10. 为每个主题类型添加转换值
                for (int i = 0; i < Math.Min(themeTypes.Count, themeContexts.Count); i++)
                {
                    typeCache[themeTypes[i]] =
                        $"{converterKey}.Convert(typeof({propertyTypeName}), nameof({propertyName}), {themeContexts[i]})";
                }
            }

            if (!propertyConverters.Any())
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#pragma warning disable 1591");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
            sb.AppendLine($"    public partial class {className} : {IThemeObjectFullName}");
            sb.AppendLine("    {");

            foreach (var converter in converterInstances.Values)
            {
                sb.AppendLine($"        {converter}");
            }
            sb.AppendLine();

            sb.AppendLine($"        private static readonly global::System.Collections.Generic.Dictionary<global::System.String, {IThemeValueConverterFullName}> __velox__Converters__ = new()");
            sb.AppendLine("        {");
            foreach (var kvp in propertyConverters)
            {
                sb.AppendLine($"            {{ nameof({kvp.Key}), {kvp.Value} }},");
            }
            sb.AppendLine("        };");
            sb.AppendLine();

            sb.AppendLine("        public static readonly global::System.Collections.Generic.Dictionary<global::System.String, global::System.Reflection.PropertyInfo> __velox_Theme__Props__ = new()");
            sb.AppendLine("        {");
            foreach (var kvp in propertyInfos)
            {
                sb.AppendLine($"            {kvp.Value},");
            }
            sb.AppendLine("        };");
            sb.AppendLine();

            sb.AppendLine("        public static readonly global::System.Collections.Generic.Dictionary<global::System.String, global::System.Collections.Generic.Dictionary<global::System.Reflection.PropertyInfo, global::System.Collections.Generic.Dictionary<global::System.Type, object?>>> __velox__Def__ThemeCache__ = new()");
            sb.AppendLine("        {");
            foreach (var propertyEntry in themeCache)
            {
                sb.AppendLine($"            {{");
                sb.AppendLine($"                nameof({propertyEntry.Key}),");
                sb.AppendLine($"                new global::System.Collections.Generic.Dictionary<global::System.Reflection.PropertyInfo, global::System.Collections.Generic.Dictionary<global::System.Type, object?>>()");
                sb.AppendLine($"                {{");
                sb.AppendLine($"                    {{");
                sb.AppendLine($"                        __velox_Theme__Props__[nameof({propertyEntry.Key})],");
                sb.AppendLine($"                        new global::System.Collections.Generic.Dictionary<global::System.Type, object?>()");
                sb.AppendLine($"                        {{");

                foreach (var themeEntry in propertyEntry.Value[propertyEntry.Key])
                {
                    sb.AppendLine($"                            {{ typeof(global::{themeEntry.Key}), {themeEntry.Value} }},");
                }

                sb.AppendLine($"                        }}");
                sb.AppendLine($"                    }}");
                sb.AppendLine($"                }}");
                sb.AppendLine($"            }},");
            }
            sb.AppendLine("        };");
            sb.AppendLine();

            sb.AppendLine("        public global::System.Collections.Generic.Dictionary<global::System.String, global::System.Collections.Generic.Dictionary<global::System.Reflection.PropertyInfo, global::System.Collections.Generic.Dictionary<global::System.Type, object?>>> __velox__Act__ThemeCache__ = new global::System.Collections.Generic.Dictionary<global::System.String, global::System.Collections.Generic.Dictionary<global::System.Reflection.PropertyInfo, global::System.Collections.Generic.Dictionary<global::System.Type, object?>>>();");
            sb.AppendLine();

            sb.AppendLine("        public void ExecuteThemeChanging(global::System.Type? oldValue, global::System.Type? newValue)");
            sb.AppendLine("        {");
            sb.AppendLine("            OnThemeChanging(oldValue, newValue);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public void ExecuteThemeChanged(global::System.Type? oldValue, global::System.Type? newValue)");
            sb.AppendLine("        {");
            sb.AppendLine("            OnThemeChanged(oldValue, newValue);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        partial void OnThemeChanging(global::System.Type? oldValue, global::System.Type? newValue);");
            sb.AppendLine("        partial void OnThemeChanged(global::System.Type? oldValue, global::System.Type? newValue);");
            sb.AppendLine();

            sb.AppendLine($"        public void EditThemeValue<T>(global::System.String propertyName, object? newValue) where T : {IThemeFullName}");
            sb.AppendLine("        {");
            sb.AppendLine("            if (__velox__Act__ThemeCache__.TryGetValue(propertyName, out var propertyCache) &&");
            sb.AppendLine("                propertyCache.TryGetValue(__velox_Theme__Props__[propertyName], out var typeCache))");
            sb.AppendLine("            {");
            sb.AppendLine("                typeCache[typeof(T)] = newValue;");
            sb.AppendLine("            }");
            sb.AppendLine("            else");
            sb.AppendLine("            {");
            sb.AppendLine("                if (!__velox__Act__ThemeCache__.TryGetValue(propertyName, out global::System.Collections.Generic.Dictionary<global::System.Reflection.PropertyInfo, global::System.Collections.Generic.Dictionary<global::System.Type, object?>>? value))");
            sb.AppendLine("                {");
            sb.AppendLine("                    value = new global::System.Collections.Generic.Dictionary<global::System.Reflection.PropertyInfo, global::System.Collections.Generic.Dictionary<global::System.Type, object?>>();");
            sb.AppendLine("                    __velox__Act__ThemeCache__[propertyName] = value;");
            sb.AppendLine("                }");
            sb.AppendLine();
            sb.AppendLine("                value[__velox_Theme__Props__[propertyName]] = new global::System.Collections.Generic.Dictionary<global::System.Type, object?> { { typeof(T), newValue } };");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine($"        public void RestoreThemeValue<T>(global::System.String propertyName) where T : {IThemeFullName} => __velox__Act__ThemeCache__.Remove(propertyName);");
            sb.AppendLine();
            sb.AppendLine("        public global::System.Collections.Generic.Dictionary<global::System.String, global::System.Collections.Generic.Dictionary<global::System.Reflection.PropertyInfo, global::System.Collections.Generic.Dictionary<global::System.Type, object?>>> GetStaticCache() => __velox__Def__ThemeCache__;");
            sb.AppendLine("        public global::System.Collections.Generic.Dictionary<global::System.String, global::System.Collections.Generic.Dictionary<global::System.Reflection.PropertyInfo, global::System.Collections.Generic.Dictionary<global::System.Type, object?>>> GetActiveCache() => __velox__Act__ThemeCache__;");
            sb.AppendLine();
            sb.AppendLine("        public void InitializeTheme()");
            sb.AppendLine("        {");
            sb.AppendLine($"            {ThemeManagerFullName}.Register(this);");
            foreach (var propertyName in propertyInfos.Keys)
            {
                sb.AppendLine($"            __velox_Theme__Props__[nameof({propertyName})].SetValue(this, __velox__Def__ThemeCache__[nameof({propertyName})][__velox_Theme__Props__[nameof({propertyName})]][{ThemeManagerFullName}.Current]);");
            }
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }
    }
}