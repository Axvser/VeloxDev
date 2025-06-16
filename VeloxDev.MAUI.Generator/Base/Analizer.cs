using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Linq;

namespace VeloxDev.MAUI.Generator.Base
{
    public static class Analizer
    {
        public static class Filters
        {
            public static bool IsPartialClass(SyntaxNode node)
            {
                return node is ClassDeclarationSyntax classDecl && classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
            }
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
