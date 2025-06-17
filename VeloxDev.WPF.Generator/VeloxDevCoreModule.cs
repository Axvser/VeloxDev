using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using VeloxDev.WPF.Generator.Base;
using VeloxDev.WPF.Generator.Writers;

namespace VeloxDev.WPF.Generator
{
    [Generator]
    public class VeloxDevCoreModule : IIncrementalGenerator
    {
        protected static readonly List<ICodeWriter> codeWriters = [];

        static VeloxDevCoreModule()
        {
            codeWriters.Add(new CoreWriter());
        }

        public virtual void Initialize(IncrementalGeneratorInitializationContext context)
        {
            
            context.RegisterSourceOutput(Analizer.Filters.FilterContext(context), GenerateSource);
        }

        public virtual void GenerateSource(SourceProductionContext context, (Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> Classes) input)
        {
            var values = GetFilteredContext(input);
            foreach (var kvp in values)
            {
                for (int i = 0; i < codeWriters.Count; i++)
                {
                    codeWriters[i].Initialize(kvp.Value, kvp.Key);
                    if (codeWriters[i].CanWrite())
                    {
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
                var classSymbol = model.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
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
