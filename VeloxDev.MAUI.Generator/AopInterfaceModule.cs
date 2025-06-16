using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace VeloxDev.MAUI.Generator
{
    [Generator]
    public class AopInterfaceModule : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var classDeclarations = AnalizeHelper.DefiningFilter(context);
            var compilationAndClasses = AnalizeHelper.GetValue(context, classDeclarations);
            context.RegisterSourceOutput(compilationAndClasses, GenerateSource);
        }
        private static void GenerateSource(SourceProductionContext context, (Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> Classes) input)
        {
            foreach (var classDeclaration in input.Classes)
            {
                SemanticModel model = input.Compilation.GetSemanticModel(classDeclaration.SyntaxTree);
                var classSymbol = model.GetDeclaredSymbol(classDeclaration);
                if (!AnalizeHelper.IsAopClass(classDeclaration) || classSymbol is null) continue;

                string interfaceName = $"{classDeclaration.Identifier.Text}_{classSymbol.ContainingNamespace.ToDisplayString().Replace('.', '_')}_Aop";
                var baseList = SyntaxFactory.BaseList(
                    SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                        SyntaxFactory.SimpleBaseType(
                            SyntaxFactory.QualifiedName(
                                SyntaxFactory.ParseName("global::VeloxDev.Core.Interfaces.AspectOriented"),
                                SyntaxFactory.IdentifierName("IProxy")))));
                var interfaceDeclaration = SyntaxFactory.InterfaceDeclaration(interfaceName)
                    .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                    .WithBaseList(baseList);

                foreach (var field in classDeclaration.Members.OfType<FieldDeclarationSyntax>()
                    .Where(fd => fd.AttributeLists.Any(atts => atts.Attributes.Any(att => att.ToString().Contains("Observable")))
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

                foreach (var property in classDeclaration.Members.OfType<PropertyDeclarationSyntax>()
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

                foreach (var method in classDeclaration.Members.OfType<MethodDeclarationSyntax>()
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

                NamespaceDeclarationSyntax namespaceDeclaration = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName("VeloxDev.Core.AopInterfaces"))
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
