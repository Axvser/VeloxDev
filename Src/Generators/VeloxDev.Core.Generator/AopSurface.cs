using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VeloxDev.Generators.Base;

namespace VeloxDev.Generators
{
    /// <summary>
    /// Emits the two halves of an AOP surface: the mirror interface a caller sees, and the proxy class that
    /// implements it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both come from one walk over the same member list. That is deliberate: the proxy must implement exactly the
    /// interface, and a member added to one but not the other would be a compile error in generated code rather
    /// than a message about the author's own line. Sharing the walk makes "the two lists agree" structural.
    /// </para>
    /// <para>
    /// The proxy used to be built at runtime by <c>DispatchProxy</c>, which needs <c>Reflection.Emit</c> and is
    /// annotated <c>RequiresDynamicCode</c> — so no AOT-published app could use this module, and every call went
    /// through <c>MethodInfo.Invoke</c>. Emitting the class here removes both: the proxy is ordinary code in the
    /// consumer's own assembly.
    /// </para>
    /// </remarks>
    [Generator(LanguageNames.CSharp)]
    public class AopSurface : IIncrementalGenerator
    {
        /// <inheritdoc />
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(
                Analizer.Filters.Targets(context).Combine(context.CompilationProvider),
                GenerateSource);
        }

        // 一个「可挂钩单元」：属性拆成 getter/setter 两条，方法一条。
        // 接口成员与代理实现都从这些描述产出，所以两边的签名天生一致。
        private sealed class Hookable(string hookKey, string signature, string argumentList, string returnType, bool returnsValue)
        {
            public string HookKey { get; } = hookKey;
            public string Slot { get; } = Slot(hookKey);
            public string Signature { get; } = signature;
            public string ArgumentList { get; } = argumentList;
            public string ReturnType { get; } = returnType;
            public bool ReturnsValue { get; } = returnsValue;
        }

        private sealed class HookableProperty(string name, string type, bool hasGetter, bool hasSetter)
        {
            public string Name { get; } = name;
            public string Type { get; } = type;
            public bool HasGetter { get; } = hasGetter;
            public bool HasSetter { get; } = hasSetter;
        }

        private void GenerateSource(SourceProductionContext context, (ImmutableArray<Analizer.Filters.GeneratorTarget> Targets, Compilation Compilation) input)
        {
            foreach (var (classDeclaration, classSymbol) in Analizer.Filters.Resolve(input.Targets, input.Compilation))
            {
                if (!AnalizeHelper.IsAopClass(classSymbol)) continue;

                // 镜像该类型所有带特性的成员，而不只是本生成器拿到的那份声明上的：接口必须匹配 AopWriter 构建的代理契约，两者现在都不看成员来自哪个 partial 文件。
                var members = AnalizeHelper.Members(classSymbol).ToList();

                string className = classDeclaration.Identifier.Text;
                string interfaceName = AopNames.InterfaceFor(classSymbol);
                string fullClassName = $"global::{classSymbol.ToDisplayString()}";

                var hookables = new List<Hookable>();
                var properties = new List<HookableProperty>();
                var baseList = SyntaxFactory.BaseList(
                    SyntaxFactory.SeparatedList<BaseTypeSyntax>(
                    [
                        SyntaxFactory.SimpleBaseType(
                            SyntaxFactory.QualifiedName(
                                SyntaxFactory.ParseName("global::VeloxDev.AspectOriented"),
                                SyntaxFactory.IdentifierName("IAspectOriented"))),
                        SyntaxFactory.SimpleBaseType(
                            SyntaxFactory.QualifiedName(
                                SyntaxFactory.ParseName("global::VeloxDev.AspectOriented"),
                                SyntaxFactory.IdentifierName("IAopHookTarget"))),
                    ]));
                var interfaceDeclaration = SyntaxFactory.InterfaceDeclaration(interfaceName)
                    .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                    .WithBaseList(baseList);

                // ── 字段：[VeloxProperty][AspectOriented] —— 接口成员是生成出来的那个属性 ──
                foreach (var field in members.OfType<FieldDeclarationSyntax>()
                    .Where(fd => fd.AttributeLists.Any(atts => atts.Attributes.Any(att => att.ToString().Contains("Observable") || att.ToString().Contains("Property")))
                              && fd.AttributeLists.Any(atts => atts.Attributes.Any(att => att.ToString() == "AspectOriented"))))
                {
                    foreach (var variable in field.Declaration.Variables)
                    {
                        var propertyName = AnalizeHelper.GetPropertyNameByFieldName(variable);
                        var propertyType = GetFullyQualifiedType(input.Compilation, field.Declaration.Type).ToString();

                        interfaceDeclaration = interfaceDeclaration.AddMembers(
                            SyntaxFactory.PropertyDeclaration(SyntaxFactory.ParseTypeName(propertyType), propertyName)
                                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                                .WithAccessorList(accessorList(get: true, set: true)));

                        properties.Add(new HookableProperty(propertyName, propertyType, hasGetter: true, hasSetter: true));
                    }
                }

                // ── 属性：[AspectOriented] 且 public ──
                foreach (var property in members.OfType<PropertyDeclarationSyntax>()
                    .Where(p => p.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword))
                             && p.AttributeLists.Any(atts => atts.Attributes.Any(att => att.ToString() == "AspectOriented"))))
                {
                    var propertyType = GetFullyQualifiedType(input.Compilation, property.Type);

                    bool hasGetter = false;
                    bool hasSetter = false;
                    if (property.AccessorList != null)
                    {
                        hasGetter = !property.AccessorList.Accessors.Any(a => a.Kind() == SyntaxKind.GetAccessorDeclaration)
                                 || property.AccessorList.Accessors.Any(a => a.Kind() == SyntaxKind.GetAccessorDeclaration && a.Modifiers.All(m => m.IsKind(SyntaxKind.PublicKeyword)));
                        hasSetter = property.AccessorList.Accessors.Any(a => a.Kind() == SyntaxKind.SetAccessorDeclaration && a.Modifiers.All(m => m.IsKind(SyntaxKind.PublicKeyword)));
                    }

                    interfaceDeclaration = interfaceDeclaration.AddMembers(
                        SyntaxFactory.PropertyDeclaration(propertyType, property.Identifier)
                            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                            .WithAccessorList(accessorList(hasGetter, hasSetter)));

                    properties.Add(new HookableProperty(property.Identifier.Text, propertyType.ToString(), hasGetter, hasSetter));
                }

                // ── 方法：[AspectOriented] 且 public ──
                foreach (var method in members.OfType<MethodDeclarationSyntax>()
                    .Where(m => m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.PublicKeyword))
                             && m.AttributeLists.Any(atts => atts.Attributes.Any(att => att.ToString() == "AspectOriented"))))
                {
                    var returnType = GetFullyQualifiedType(input.Compilation, method.ReturnType);
                    var parameterList = GetFullyQualifiedParameterList(input.Compilation, method.ParameterList);

                    interfaceDeclaration = interfaceDeclaration.AddMembers(
                        SyntaxFactory.MethodDeclaration(returnType, method.Identifier)
                            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                            .WithParameterList(parameterList)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));

                    var returnTypeText = returnType.ToString();
                    var argumentList = string.Join(", ", method.ParameterList.Parameters.Select(static p => p.Identifier.Text));
                    // 参数表要 NormalizeWhitespace().ToFullString()：SyntaxNode.ToString() 抹掉 trivia，
                    // "(object sender, EventArgs e)" 会变成 "(objectsender, EventArgse)"。
                    hookables.Add(new Hookable(
                        method.Identifier.Text,
                        $"{returnTypeText} {method.Identifier.Text}{parameterList.NormalizeWhitespace().ToFullString()}",
                        argumentList,
                        returnTypeText,
                        returnsValue: returnTypeText != "void"));
                }

                var namespaceDeclaration = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(AopNames.InterfaceNamespace))
                    .AddMembers(interfaceDeclaration);
                context.AddSource(
                    $"{interfaceName}.g.cs",
                    SourceText.From(namespaceDeclaration.NormalizeWhitespace().ToFullString(), Encoding.UTF8));

                context.AddSource(
                    $"{interfaceName}Proxy.g.cs",
                    SourceText.From(
                        WriteProxy(classSymbol, className, fullClassName, interfaceName, hookables, properties),
                        Encoding.UTF8));
            }
        }

        private static AccessorListSyntax accessorList(bool get, bool set)
        {
            var accessors = new List<AccessorDeclarationSyntax>();
            if (get)
            {
                accessors.Add(SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));
            }

            if (set)
            {
                accessors.Add(SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));
            }

            return SyntaxFactory.AccessorList(SyntaxFactory.List(accessors));
        }

        // 槽名由挂钩键推导，保证唯一且是合法标识符（键里可能带 get_ 前缀与方法名的下划线）。
        private static string Slot(string hookKey)
            => "_hooks_" + hookKey.Replace("get_", "get_").Replace('.', '_');

        private static string WriteProxy(
            INamedTypeSymbol classSymbol,
            string className,
            string fullClassName,
            string interfaceName,
            List<Hookable> hookables,
            List<HookableProperty> properties)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("#pragma warning disable");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine($"namespace {AopNames.InterfaceNamespace};");
            sb.AppendLine();
            sb.AppendLine("/// <summary>");
            sb.AppendLine($"/// The compile-time proxy over <see cref=\"{fullClassName}\"/>. Created by <c>Aop()</c>; not for direct use.");
            sb.AppendLine("/// </summary>");
            sb.AppendLine($"internal sealed class {interfaceName}Proxy : {interfaceName}");
            sb.AppendLine("{");

            foreach (var hookable in hookables)
            {
                sb.AppendLine($"    private global::VeloxDev.AspectOriented.AspectHooks? {hookable.Slot};");
            }

            foreach (var property in properties)
            {
                if (property.HasGetter)
                {
                    sb.AppendLine($"    private global::VeloxDev.AspectOriented.AspectHooks? {Slot("get_" + property.Name)};");
                }

                if (property.HasSetter)
                {
                    sb.AppendLine($"    private global::VeloxDev.AspectOriented.AspectHooks? {Slot("set_" + property.Name)};");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"    private readonly {fullClassName} _target;");
            sb.AppendLine();
            sb.AppendLine($"    internal {interfaceName}Proxy({fullClassName} target) => _target = target;");
            sb.AppendLine();

            foreach (var property in properties)
            {
                sb.AppendLine($"    public {property.Type} {property.Name}");
                sb.AppendLine("    {");
                if (property.HasGetter)
                {
                    sb.AppendLine($"        get");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            var h = {Slot("get_" + property.Name)};");
                    // 无钩子时直接读真身 —— 今天是每次调用都走 MethodInfo.Invoke，这条快路径是本次改动的实际收益。
                    sb.AppendLine("            if (h is null)");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                return _target.{property.Name};");
                    sb.AppendLine("            }");
                    sb.AppendLine();
                    sb.AppendLine("            var r0 = h.Start?.Invoke(null, null);");
                    sb.AppendLine("            var r1 = h.Coverage is null");
                    sb.AppendLine($"                ? (object?)_target.{property.Name}");
                    sb.AppendLine("                : h.Coverage.Invoke(null, r0);");
                    sb.AppendLine("            h.End?.Invoke(null, r1);");
                    sb.AppendLine($"            return ({property.Type})r1!;");
                    sb.AppendLine("        }");
                }

                if (property.HasSetter)
                {
                    sb.AppendLine($"        set");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            var h = {Slot("set_" + property.Name)};");
                    sb.AppendLine("            if (h is null)");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                _target.{property.Name} = value;");
                    sb.AppendLine("                return;");
                    sb.AppendLine("            }");
                    sb.AppendLine();
                    sb.AppendLine("            var args = new object?[] { value };");
                    sb.AppendLine("            var r0 = h.Start?.Invoke(args, null);");
                    sb.AppendLine("            object? r1;");
                    sb.AppendLine("            if (h.Coverage is null)");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                _target.{property.Name} = value;");
                    sb.AppendLine("                r1 = null;");
                    sb.AppendLine("            }");
                    sb.AppendLine("            else");
                    sb.AppendLine("            {");
                    sb.AppendLine("                r1 = h.Coverage.Invoke(args, r0);");
                    sb.AppendLine("            }");
                    sb.AppendLine();
                    sb.AppendLine("            h.End?.Invoke(args, r1);");
                    sb.AppendLine("        }");
                }

                sb.AppendLine("    }");
                sb.AppendLine();
            }

            foreach (var hookable in hookables)
            {
                sb.AppendLine($"    public {hookable.Signature}");
                sb.AppendLine("    {");
                sb.AppendLine($"        var h = {hookable.Slot};");
                sb.AppendLine("        if (h is null)");
                sb.AppendLine("        {");
                var bareCall = $"_target.{hookable.HookKey}({hookable.ArgumentList})";
                sb.AppendLine(hookable.ReturnsValue ? $"            return {bareCall};" : $"            {bareCall};");
                sb.AppendLine(hookable.ReturnsValue ? string.Empty : "            return;");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine(hookable.ArgumentList.Length == 0
                    ? "        object?[]? args = null;"
                    : $"        var args = new object?[] {{ {hookable.ArgumentList} }};");
                sb.AppendLine("        var r0 = h.Start?.Invoke(args, null);");
                sb.AppendLine("        object? r1;");
                sb.AppendLine("        if (h.Coverage is null)");
                sb.AppendLine("        {");
                sb.AppendLine(hookable.ReturnsValue
                    ? $"            r1 = {bareCall};"
                    : $"            {bareCall};\n            r1 = null;");
                sb.AppendLine("        }");
                sb.AppendLine("        else");
                sb.AppendLine("        {");
                sb.AppendLine("            r1 = h.Coverage.Invoke(args, r0);");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        h.End?.Invoke(args, r1);");
                if (hookable.ReturnsValue)
                {
                    sb.AppendLine($"        return ({hookable.ReturnType})r1!;");
                }

                sb.AppendLine("    }");
                sb.AppendLine();
            }

            sb.AppendLine("    void global::VeloxDev.AspectOriented.IAopHookTarget.SetHooks(string memberKey, global::VeloxDev.AspectOriented.AspectHooks? hooks)");
            sb.AppendLine("    {");
            sb.AppendLine("        switch (memberKey)");
            sb.AppendLine("        {");
            foreach (var hookable in hookables)
            {
                sb.AppendLine($"            case \"{hookable.HookKey}\": {hookable.Slot} = hooks; return;");
            }

            foreach (var property in properties)
            {
                if (property.HasGetter)
                {
                    sb.AppendLine($"            case \"get_{property.Name}\": {Slot("get_" + property.Name)} = hooks; return;");
                }

                if (property.HasSetter)
                {
                    sb.AppendLine($"            case \"set_{property.Name}\": {Slot("set_" + property.Name)} = hooks; return;");
                }
            }

            sb.AppendLine("            default:");
            sb.AppendLine($"                throw new global::System.ArgumentOutOfRangeException(nameof(memberKey), memberKey, \"not an interceptable member of {classSymbol.ToDisplayString()}\");");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static TypeSyntax GetFullyQualifiedType(Compilation compilation, TypeSyntax typeSyntax)
        {
            SemanticModel model = compilation.GetSemanticModel(typeSyntax.SyntaxTree);
            var symbol = model.GetTypeInfo(typeSyntax).Type;
            if (symbol != null)
            {
                if (symbol.SpecialType == SpecialType.System_Void)
                {
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword));
                }

                return SyntaxFactory.ParseTypeName(symbol.ToDisplayString());
            }
            return typeSyntax;
        }

        private static ParameterListSyntax GetFullyQualifiedParameterList(Compilation compilation, ParameterListSyntax parameterList)
        {
            List<ParameterSyntax> newParameters = [];
            foreach (var parameter in parameterList.Parameters)
            {
                if (parameter.Type == null) continue;

                TypeSyntax fullyQualifiedType = GetFullyQualifiedType(compilation, parameter.Type);
                ParameterSyntax newParameter = parameter.WithType(fullyQualifiedType);
                newParameters.Add(newParameter);
            }
            return parameterList.WithParameters(SyntaxFactory.SeparatedList(newParameters));
        }
    }
}
