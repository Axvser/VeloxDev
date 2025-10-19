using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Linq;
using VeloxDev.Core.Generator.Writers;

namespace VeloxDev.Core.Generator
{
    [Generator(LanguageNames.CSharp)]
    public class MVVMGenerator : IIncrementalGenerator
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

            // 检查类是否有MVVM相关的字段属性
            var hasMVVMFields = classDeclaration.Members
                .OfType<FieldDeclarationSyntax>()
                .Any(field => field.AttributeLists
                    .SelectMany(al => al.Attributes)
                    .Any(attr => IsMVVMAttribute(attr, context.SemanticModel)));

            return hasMVVMFields ? classDeclaration : null;
        }

        private static bool IsMVVMAttribute(AttributeSyntax attribute, SemanticModel semanticModel)
        {
            var attributeSymbol = semanticModel.GetSymbolInfo(attribute).Symbol?.ContainingSymbol;
            if (attributeSymbol is not INamedTypeSymbol attributeType) return false;

            var attributeName = attributeType.ToDisplayString();
            return attributeName.Contains("VeloxProperty");
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
            var classSymbol = semanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;

            if (classSymbol == null) return;

            var writer = new MVVMWriter();
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