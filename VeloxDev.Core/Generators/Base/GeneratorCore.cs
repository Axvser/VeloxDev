using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;

namespace VeloxDev.Core.Generators.Base
{
    public abstract class GeneratorCore : IIncrementalGenerator
    {
        protected readonly List<ICodeWriter> codeWriters = [];

        public virtual void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(Analizer.Filters.FilterContext(context), GenerateSource);
        }

        public virtual void AddCodeWriter<T>(params T[] writers) where T : ICodeWriter
        {
            for (int i = 0; i < writers.Length; i++)
            {
                codeWriters.Add(writers[i]);
            }
        }

        public virtual void GenerateSource(SourceProductionContext context, (Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> Classes) input)
        {
            var values = GetFilteredContext(input);
            foreach (var kvp in values)
            {
                for (int i = 0; i < codeWriters.Count; i++)
                {
                    if (codeWriters[i].CanWrite())
                    {
                        codeWriters[i].Initialize(kvp.Value, kvp.Key);
                        context.AddSource(
                            codeWriters[i].GetFileName(),
                            SourceText.From(
                                codeWriters[i].Write(),
                                Encoding.UTF8));
                    }
                }
            }
        }

        public virtual Dictionary<INamedTypeSymbol, ClassDeclarationSyntax> GetFilteredContext((Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> Classes) input)
        {
            Dictionary<INamedTypeSymbol, ClassDeclarationSyntax> uniqueTargets = [];
            foreach (var classDeclaration in input.Classes)
            {
                SemanticModel model = input.Compilation.GetSemanticModel(classDeclaration.SyntaxTree);
                var classSymbol = model.GetDeclaredSymbol(classDeclaration);
                if (classSymbol == null)
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
