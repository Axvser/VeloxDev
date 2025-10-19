using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using VeloxDev.Core.Generator.Base;
using VeloxDev.Core.Generator.Writers;

namespace VeloxDev.Core.Generator
{
    [Generator(LanguageNames.CSharp)]
    public class MVVMGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(Analizer.Filters.FilterContext(context), GenerateSource);
        }

        public void GenerateSource(SourceProductionContext context, (Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> Classes) input)
        {
            var values = GetFilteredContext(input);
            foreach (var kvp in values)
            {
                var writer = new MVVMWriter();
                writer.Initialize(kvp.Value, kvp.Key);
                if (writer.CanWrite())
                {
                    context.AddSource(
                        writer.GetFileName(),
                        SourceText.From(writer.Write(), Encoding.UTF8));
                }
            }
        }

        private Dictionary<INamedTypeSymbol, ClassDeclarationSyntax> GetFilteredContext((Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> Classes) input)
        {
            Dictionary<INamedTypeSymbol, ClassDeclarationSyntax> uniqueTargets = [];
            foreach (var classDeclaration in input.Classes)
            {
                SemanticModel model = input.Compilation.GetSemanticModel(classDeclaration.SyntaxTree);
                if (model.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
                    continue;

                if (!uniqueTargets.TryGetValue(classSymbol, out _))
                {
                    uniqueTargets.Add(classSymbol, classDeclaration);
                }
            }
            return uniqueTargets;
        }
    }
}