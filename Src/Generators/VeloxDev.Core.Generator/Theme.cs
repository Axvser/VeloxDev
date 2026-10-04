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

namespace VeloxDev.Generators
{
    [Generator(LanguageNames.CSharp)]
    public class Theme : IIncrementalGenerator
    {
        private const string ThemeManagerFullName = "global::VeloxDev.DynamicTheme.ThemeManager";
        private const string IThemeFullName = "global::VeloxDev.DynamicTheme.ITheme";
        private const string IThemeObjectFullName = "global::VeloxDev.DynamicTheme.IThemeObject";
        private const string IThemeValueConverterFullName = "global::VeloxDev.DynamicTheme.IThemeValueConverter";

        /// <inheritdoc />
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
                "VeloxDev.DynamicTheme.ThemeConfigAttribute`3",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: Transform).Collect();

            var classWith4Params = context.SyntaxProvider.ForAttributeWithMetadataName(
                "VeloxDev.DynamicTheme.ThemeConfigAttribute`4",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: Transform).Collect();

            var classWith5Params = context.SyntaxProvider.ForAttributeWithMetadataName(
                "VeloxDev.DynamicTheme.ThemeConfigAttribute`5",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: Transform).Collect();

            var classWith6Params = context.SyntaxProvider.ForAttributeWithMetadataName(
                "VeloxDev.DynamicTheme.ThemeConfigAttribute`6",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: Transform).Collect();

            var classWith7Params = context.SyntaxProvider.ForAttributeWithMetadataName(
                "VeloxDev.DynamicTheme.ThemeConfigAttribute`7",
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

        private const string ThemeCacheFullName = "global::VeloxDev.DynamicTheme.ThemeCache";

        private string GenerateThemeConfigClass(INamedTypeSymbol classSymbol, IEnumerable<AttributeData> attributes)
        {
            var namespaceName = classSymbol.ContainingNamespace.ToDisplayString();
            var className = classSymbol.Name;
            var classFullTypeName = $"global::{classSymbol.ToDisplayString()}";
            var isPartial = classSymbol.DeclaringSyntaxReferences
                .Any(r => ((ClassDeclarationSyntax)r.GetSyntax()).Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));

            if (!isPartial)
            {
                return string.Empty;
            }

            // ── 检测基类型是否已实现 IThemeObject ──
            bool baseHasIThemeObject = false;
            bool baseHasOurThemeConfig = false;
            if (classSymbol.BaseType != null && classSymbol.BaseType.SpecialType != SpecialType.System_Object)
            {
                baseHasIThemeObject = classSymbol.BaseType.AllInterfaces.Any(i =>
                    i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == IThemeObjectFullName);

                // 检查是否有祖先带 ThemeConfigAttribute（表示由本生成器处理）
                var checkType = classSymbol.BaseType;
                while (checkType != null && checkType.SpecialType != SpecialType.System_Object)
                {
                    if (checkType.GetAttributes().Any(attr =>
                            attr.AttributeClass?.Name.StartsWith("ThemeConfigAttribute") == true))
                    {
                        baseHasOurThemeConfig = true;
                        break;
                    }
                    checkType = checkType.BaseType;
                }
            }

            bool isSealed = classSymbol.IsSealed;
            string methodModifier;
            if (isSealed)
                methodModifier = "";
            else if (baseHasOurThemeConfig)
                methodModifier = "override ";
            else
                methodModifier = "virtual ";

            string baseCallInit = baseHasIThemeObject ? "base.InitializeTheme();" : "";
            string baseCallChanging = baseHasIThemeObject ? "base.ExecuteThemeChanging(oldValue, newValue);" : "";
            string baseCallChanged = baseHasIThemeObject ? "base.ExecuteThemeChanged(oldValue, newValue);" : "";
            string addInterface = baseHasIThemeObject ? "" : $" : {IThemeObjectFullName}";

            // 收集属性配置供 ThemeCache 注册
            var configRegistrations = new List<string>();
            var propertyNamesForInit = new List<string>();

            foreach (var attribute in attributes)
            {
                var propertyName = attribute.ConstructorArguments[0].Value?.ToString();
                if (string.IsNullOrEmpty(propertyName))
                    continue;

                var converterType = attribute.AttributeClass?.TypeArguments[0];
                if (converterType == null)
                    continue;
                var converterTypeName = converterType.ToDisplayString();

                // 查属性符号（沿继承链上溯）
                var propertySymbol = classSymbol.GetMembers(propertyName!)
                    .OfType<IPropertySymbol>()
                    .FirstOrDefault();

                var baseType = classSymbol.BaseType;
                while (propertySymbol == null && baseType != null)
                {
                    propertySymbol = baseType.GetMembers(propertyName!)
                        .OfType<IPropertySymbol>()
                        .FirstOrDefault();
                    baseType = baseType.BaseType;
                }

                if (propertySymbol == null)
                    continue;

                var propertyTypeName = propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                // 构建转换器键（仅占位 —— 转换器内联创建）
                var converterKey = $"__velox_conv_{converterTypeName.GetHashCode()}_{propertyName}__";

                // 处理主题上下文的参数
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

                // 取主题类型
                var themeTypes = new List<string>();
                for (int i = 1; i < attribute.AttributeClass?.TypeArguments.Length; i++)
                {
                    var themeType = attribute.AttributeClass.TypeArguments[i];
                    if (themeType != null)
                    {
                        themeTypes.Add(themeType.ToDisplayString());
                    }
                }

                // 构建主题值表达式
                var themeValueExprs = new List<string>();
                for (int i = 0; i < Math.Min(themeTypes.Count, themeContexts.Count); i++)
                {
                    var convertExpr =
                        $"(({IThemeValueConverterFullName})global::System.Activator.CreateInstance(typeof(global::{converterTypeName}))!).Convert(typeof({propertyTypeName}), nameof({propertyName}), {themeContexts[i]})";
                    themeValueExprs.Add(
                        $"                            {{ typeof(global::{themeTypes[i]}), {convertExpr} }},");
                }

                var propertyExpr = $"typeof({classFullTypeName}).GetProperty(nameof({propertyName}))!";

                // 为本属性构建注册字符串（用于 InitializeTheme）
                var themeDictEntries = string.Join("\n", themeValueExprs);
                var regEntry = $$"""
                                {
                                    nameof({{propertyName}}),
                                    (
                                        {{propertyExpr}},
                                        new global::System.Collections.Generic.Dictionary<global::System.Type, object?>()
                                        {
                            {{themeDictEntries}}
                                        }
                                    )
                                },
                    """;
                configRegistrations.Add(regEntry);
                propertyNamesForInit.Add(propertyName!);
            }

            if (configRegistrations.Count == 0)
            {
                return string.Empty;
            }

            var registrationDict = string.Join("", configRegistrations);

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#pragma warning disable 1591");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
            sb.AppendLine($"    public partial class {className}{addInterface}");
            sb.AppendLine("    {");

            // ── ExecuteThemeChanging ──
            sb.AppendLine($"        public {methodModifier}void ExecuteThemeChanging(global::System.Type? oldValue, global::System.Type? newValue)");
            sb.AppendLine("        {");
            if (!string.IsNullOrEmpty(baseCallChanging))
                sb.AppendLine($"            {baseCallChanging}");
            sb.AppendLine("            OnThemeChanging(oldValue, newValue);");
            sb.AppendLine("        }");
            sb.AppendLine();

            // ── ExecuteThemeChanged ──
            sb.AppendLine($"        public {methodModifier}void ExecuteThemeChanged(global::System.Type? oldValue, global::System.Type? newValue)");
            sb.AppendLine("        {");
            if (!string.IsNullOrEmpty(baseCallChanged))
                sb.AppendLine($"            {baseCallChanged}");
            sb.AppendLine("            OnThemeChanged(oldValue, newValue);");
            sb.AppendLine("        }");
            sb.AppendLine();

            // ── 分部方法 ──
            sb.AppendLine("        partial void OnThemeChanging(global::System.Type? oldValue, global::System.Type? newValue);");
            sb.AppendLine("        partial void OnThemeChanged(global::System.Type? oldValue, global::System.Type? newValue);");
            sb.AppendLine();

            // ── SetThemeValue ──
            sb.AppendLine($"        public {methodModifier}void SetThemeValue<T>(global::System.String propertyName, object? newValue) where T : {IThemeFullName}");
            sb.AppendLine("        {");
            sb.AppendLine($"            var cache = {ThemeCacheFullName}.GetOrCreateActiveEntry(this);");
            sb.AppendLine("            if (!cache.Overrides.TryGetValue(propertyName, out var propertyCache))");
            sb.AppendLine("            {");
            sb.AppendLine("                propertyCache = new global::System.Collections.Generic.Dictionary<global::System.Reflection.PropertyInfo, global::System.Collections.Generic.Dictionary<global::System.Type, object?>>();");
            sb.AppendLine("                cache.Overrides[propertyName] = propertyCache;");
            sb.AppendLine("            }");
            sb.AppendLine("            var propertyInfo = typeof(" + classFullTypeName + ").GetProperty(propertyName)!;");
            sb.AppendLine("            if (!propertyCache.TryGetValue(propertyInfo, out var typeCache))");
            sb.AppendLine("            {");
            sb.AppendLine("                typeCache = new global::System.Collections.Generic.Dictionary<global::System.Type, object?>();");
            sb.AppendLine("                propertyCache[propertyInfo] = typeCache;");
            sb.AppendLine("            }");
            sb.AppendLine("            typeCache[typeof(T)] = newValue;");
            sb.AppendLine("            UpdatePropertyToCurrentTheme(propertyName);");
            sb.AppendLine("        }");
            sb.AppendLine();

            // ── RestoreThemeValue ──
            sb.AppendLine($"        public {methodModifier}void RestoreThemeValue<T>(global::System.String propertyName) where T : {IThemeFullName}");
            sb.AppendLine("        {");
            sb.AppendLine($"            var cache = {ThemeCacheFullName}.TryGetActiveEntry(this);");
            sb.AppendLine("            cache?.Overrides.Remove(propertyName);");
            sb.AppendLine("            UpdatePropertyToCurrentTheme(propertyName);");
            sb.AppendLine("        }");
            sb.AppendLine();

            // ── GetStaticThemeCache ──
            sb.AppendLine($"        public {methodModifier}global::System.Collections.Generic.Dictionary<global::System.String, global::System.Collections.Generic.Dictionary<global::System.Reflection.PropertyInfo, global::System.Collections.Generic.Dictionary<global::System.Type, object?>>> GetStaticThemeCache()");
            sb.AppendLine("        {");
            sb.AppendLine($"            return {ThemeCacheFullName}.GetStaticForType(this.GetType());");
            sb.AppendLine("        }");
            sb.AppendLine();

            // ── GetActiveThemeCache ──
            sb.AppendLine($"        public {methodModifier}global::System.Collections.Generic.Dictionary<global::System.String, global::System.Collections.Generic.Dictionary<global::System.Reflection.PropertyInfo, global::System.Collections.Generic.Dictionary<global::System.Type, object?>>> GetActiveThemeCache()");
            sb.AppendLine("        {");
            sb.AppendLine($"            return {ThemeCacheFullName}.GetOrCreateActiveEntry(this).Overrides;");
            sb.AppendLine("        }");
            sb.AppendLine();

            // ── UpdatePropertyToCurrentTheme ──
            sb.AppendLine($"        public {methodModifier}void UpdatePropertyToCurrentTheme(global::System.String propertyName)");
            sb.AppendLine("        {");
            sb.AppendLine("            var currentThemeType = " + ThemeManagerFullName + ".Current;");
            sb.AppendLine("            if (currentThemeType == null) return;");
            sb.AppendLine();
            sb.AppendLine("            object? value = null;");
            sb.AppendLine("            var found = false;");
            sb.AppendLine();
            sb.AppendLine($"            var activeEntry = {ThemeCacheFullName}.TryGetActiveEntry(this);");
            sb.AppendLine("            if (activeEntry != null && activeEntry.Overrides.TryGetValue(propertyName, out var propCache))");
            sb.AppendLine("            {");
            sb.AppendLine("                var pi = global::System.Linq.Enumerable.FirstOrDefault(propCache.Keys);");
            sb.AppendLine("                if (pi != null && propCache[pi].TryGetValue(currentThemeType, out value))");
            sb.AppendLine("                {");
            sb.AppendLine("                    found = true;");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            if (!found)");
            sb.AppendLine("            {");
            sb.AppendLine($"                found = {ThemeCacheFullName}.TryGetDefaultValue(this.GetType(), propertyName, currentThemeType, out value);");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            if (!found) return;");
            sb.AppendLine();
            sb.AppendLine("            var propertyInfo = typeof(" + classFullTypeName + ").GetProperty(propertyName);");
            sb.AppendLine("            if (propertyInfo == null) return;");
            sb.AppendLine("            try { propertyInfo.SetValue(this, value); } catch { }");
            sb.AppendLine("        }");
            sb.AppendLine();

            // ── UpdateAllPropertiesToCurrentTheme ──
            sb.AppendLine($"        public {methodModifier}void UpdateAllPropertiesToCurrentTheme()");
            sb.AppendLine("        {");
            sb.AppendLine($"            var staticCache = {ThemeCacheFullName}.GetStaticForType(this.GetType());");
            sb.AppendLine("            foreach (var propertyName in staticCache.Keys)");
            sb.AppendLine("            {");
            sb.AppendLine("                UpdatePropertyToCurrentTheme(propertyName);");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();

            // ── InitializeTheme（惰性注册）──
            sb.AppendLine($"        public {methodModifier}void InitializeTheme()");
            sb.AppendLine("        {");
            // 惰性注册：本类型尚未注册才注册
            sb.AppendLine($"            if (!{ThemeCacheFullName}.IsTypeRegistered(typeof({classFullTypeName})))");
            sb.AppendLine("            {");
            sb.AppendLine($"                {ThemeCacheFullName}.RegisterType(typeof({classFullTypeName}), new global::System.Collections.Generic.Dictionary<global::System.String, (global::System.Reflection.PropertyInfo Property, global::System.Collections.Generic.Dictionary<global::System.Type, object?> Values)>");
            sb.AppendLine("                {");
            sb.Append(registrationDict);
            sb.AppendLine("                });");
            sb.AppendLine("            }");
            sb.AppendLine();
            // 先调基类初始化（若适用）
            if (!string.IsNullOrEmpty(baseCallInit))
                sb.AppendLine($"            {baseCallInit}");
            // 向 ThemeManager 注册并应用当前主题值
            sb.AppendLine($"            {ThemeManagerFullName}.Register(this);");
            sb.AppendLine($"            var staticCache = {ThemeCacheFullName}.GetStaticForType(this.GetType());");
            sb.AppendLine("            var currentTheme = " + ThemeManagerFullName + ".Current;");
            sb.AppendLine("            foreach (var kvp in staticCache)");
            sb.AppendLine("            {");
            sb.AppendLine("                var pi = global::System.Linq.Enumerable.FirstOrDefault(kvp.Value.Keys);");
            sb.AppendLine("                if (pi != null && kvp.Value[pi].TryGetValue(currentTheme, out var initialValue))");
            sb.AppendLine("                {");
            sb.AppendLine("                    try { pi.SetValue(this, initialValue); } catch { }");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }
    }
}