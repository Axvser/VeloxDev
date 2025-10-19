using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Immutable;
using System.Linq;
using VeloxDev.Core.Generator.Writers;

namespace VeloxDev.Core.Generator
{
    [Generator(LanguageNames.CSharp)]
    public class AOPGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var classDeclarations = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (s, _) => IsSyntaxTargetForGeneration(s),
                    transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx))
                .Where(static m => m is not null);

            var compilationAndClasses = context.CompilationProvider.Combine(classDeclarations.Collect());

            context.RegisterSourceOutput(compilationAndClasses, Execute);
        }

        private static bool IsSyntaxTargetForGeneration(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax classDeclaration &&
                   classDeclaration.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword));
        }

        private static ClassDeclarationSyntax? GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;

            // 检查类成员是否有AOP相关的属性
            var hasAopMembers = classDeclaration.Members
                .OfType<MemberDeclarationSyntax>()
                .Any(member => member.AttributeLists
                    .SelectMany(al => al.Attributes)
                    .Any(attr => IsAopAttribute(attr, context.SemanticModel)));

            return hasAopMembers ? classDeclaration : null;
        }

        private static bool IsAopAttribute(AttributeSyntax attribute, SemanticModel semanticModel)
        {
            var attributeSymbol = semanticModel.GetSymbolInfo(attribute).Symbol?.ContainingSymbol;
            if (attributeSymbol is not INamedTypeSymbol attributeType) return false;

            var attributeName = attributeType.ToDisplayString();
            return attributeName.Contains("AspectOriented");
        }

        private void Execute(SourceProductionContext context, (Compilation Left, ImmutableArray<ClassDeclarationSyntax?> Right) source)
        {
            foreach (var classDecl in source.Right.Distinct())
            {
                if (classDecl is null) break;
                ProcessClass(source.Left, classDecl, context);
            }
        }

        private void ProcessClass(Compilation compilation, ClassDeclarationSyntax classDecl, SourceProductionContext context)
        {
            var semanticModel = compilation.GetSemanticModel(classDecl.SyntaxTree);

            if (semanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol classSymbol) return;

            var writer = new AOPWriter();
            writer.Initialize(classDecl, classSymbol);

            if (writer.CanWrite())
            {
                var sourceCode = writer.Write();
                var fileName = writer.GetFileName();

                if (!string.IsNullOrEmpty(sourceCode) && !string.IsNullOrEmpty(fileName))
                {
                    context.AddSource(fileName, sourceCode);
                }
            }
        }
    }
}