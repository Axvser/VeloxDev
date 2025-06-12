using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace VeloxDev.Core.Generator.Generator
{
    public abstract class GeneratorBase : IIncrementalGenerator
    {
        public virtual void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var classDeclarations = AnalizeHelper.DefiningFilter(context);
            var compilationAndClasses = AnalizeHelper.GetValue(context, classDeclarations);
            context.RegisterSourceOutput(compilationAndClasses, GenerateSource);
        }

        public abstract void GenerateSource(SourceProductionContext context, (Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> Classes) input);

        public virtual (Dictionary<INamedTypeSymbol, ClassDeclarationSyntax>, Dictionary<string, StringBuilder>) GetFiltered((Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> Classes) input)
        {
            Dictionary<INamedTypeSymbol, ClassDeclarationSyntax> uniqueTargets = [];  // 过滤多个分部类导致的重复上下文
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

            Dictionary<string, StringBuilder> generatedSources = []; // 缓存最终的生成结果 [ Key:文件名 | Value:源码 ]

            return (uniqueTargets, generatedSources);
        }
    }
}
