using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
using VeloxDev.Generators.Base;

namespace VeloxDev.Generators
{
    [Generator(LanguageNames.CSharp)]
    public class AopInterface : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(
                Analizer.Filters.Targets(context).Combine(context.CompilationProvider),
                GenerateSource);
        }

        private void GenerateSource(SourceProductionContext context, (ImmutableArray<Analizer.Filters.GeneratorTarget> Targets, Compilation Compilation) input)
        {
            foreach (var (classDeclaration, classSymbol) in Analizer.Filters.Resolve(input.Targets, input.Compilation))
            {
                if (!AnalizeHelper.IsAopClass(classSymbol)) continue;

                // Mirror every attributed member of the type, not just the ones on the declaration
                // this generator was handed: the interface has to match the proxy contract that
                // AopWriter builds, and both now ignore which partial file a member came from.
                var members = AnalizeHelper.Members(classSymbol).ToList();

                string interfaceName = $"{classDeclaration.Identifier.Text}_{classSymbol.ContainingNamespace.ToDisplayString().Replace('.', '_')}_Aop";
                var baseList = SyntaxFactory.BaseList(
                    SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                        SyntaxFactory.SimpleBaseType(
                            SyntaxFactory.QualifiedName(
                                SyntaxFactory.ParseName("global::VeloxDev.AspectOriented"),
                                SyntaxFactory.IdentifierName("IAspectOriented")))));
                var interfaceDeclaration = SyntaxFactory.InterfaceDeclaration(interfaceName)
                    .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                    .WithBaseList(baseList);

                foreach (var field in members.OfType<FieldDeclarationSyntax>()
                    .Where(fd => fd.AttributeLists.Any(atts => atts.Attributes.Any(att => att.ToString().Contains("Observable") || att.ToString().Contains("Property")))
                              && fd.AttributeLists.Any(atts => atts.Attributes.Any(att => att.ToString() == "AspectOriented"))))
                {
                    foreach (var variable in field.Declaration.Variables)
                    {
                        var propertyName = AnalizeHelper.GetPropertyNameByFieldName(variable);
                        TypeSyntax propertyType = field.Declaration.Type;

                        propertyType = GetFullyQualifiedType(input.Compilation, propertyType);

                        var propertyDeclaration = SyntaxFactory.PropertyDeclaration(propertyType, propertyName)
                            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                            .WithAccessorList(
                                SyntaxFactory.AccessorList(
                                    SyntaxFactory.List(
                                    [
                                        SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                                        SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                                    ])));

                        interfaceDeclaration = interfaceDeclaration.AddMembers(propertyDeclaration);
                    }
                }

                foreach (var property in members.OfType<PropertyDeclarationSyntax>()
                    .Where(p => p.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword))
                             && p.AttributeLists.Any(atts => atts.Attributes.Any(att => att.ToString() == "AspectOriented"))))
                {
                    TypeSyntax propertyType = property.Type;

                    propertyType = GetFullyQualifiedType(input.Compilation, propertyType);

                    var prop = SyntaxFactory.PropertyDeclaration(propertyType, property.Identifier)
                        .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)));

                    if (property.AccessorList != null)
                    {
                        if (!property.AccessorList.Accessors.Any(a => a.Kind() == SyntaxKind.GetAccessorDeclaration) ||
                            property.AccessorList.Accessors.Any(a => a.Kind() == SyntaxKind.GetAccessorDeclaration && a.Modifiers.All(m => m.IsKind(SyntaxKind.PublicKeyword))))
                        {
                            prop = prop.AddAccessorListAccessors(
                                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));
                        }

                        if (property.AccessorList.Accessors.Any(a => a.Kind() == SyntaxKind.SetAccessorDeclaration && a.Modifiers.All(m => m.IsKind(SyntaxKind.PublicKeyword))))
                        {
                            prop = prop.AddAccessorListAccessors(
                                SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));
                        }
                    }

                    interfaceDeclaration = interfaceDeclaration.AddMembers(prop);
                }

                foreach (var method in members.OfType<MethodDeclarationSyntax>()
                    .Where(m => m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.PublicKeyword))
                             && m.AttributeLists.Any(atts => atts.Attributes.Any(att => att.ToString() == "AspectOriented"))))
                {
                    TypeSyntax returnType = method.ReturnType;
                    ParameterListSyntax parameterList = method.ParameterList;

                    returnType = GetFullyQualifiedType(input.Compilation, returnType);
                    parameterList = GetFullyQualifiedParameterList(input.Compilation, parameterList);

                    var methodSignature = SyntaxFactory.MethodDeclaration(returnType, method.Identifier)
                        .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                        .WithParameterList(parameterList)
                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
                    interfaceDeclaration = interfaceDeclaration.AddMembers(methodSignature);
                }

                NamespaceDeclarationSyntax namespaceDeclaration = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName("VeloxDev.AopInterfaces"))
                    .AddMembers(interfaceDeclaration);
                string generatedCode = namespaceDeclaration.NormalizeWhitespace().ToFullString();
                context.AddSource($"{interfaceName}.g.cs", SourceText.From(generatedCode, Encoding.UTF8));
            }
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
