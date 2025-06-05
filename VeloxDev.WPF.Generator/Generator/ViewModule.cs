using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace VeloxDev.WPF.Generator
{
    [Generator(LanguageNames.CSharp)] // 视图驱动型
    public class ViewModule : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var classDeclarations = AnalizeHelper.DefiningFilter(context);
            var compilationAndClasses = AnalizeHelper.GetValue(context, classDeclarations);
            context.RegisterSourceOutput(compilationAndClasses, GenerateSource);
        }
        private static void GenerateSource(SourceProductionContext context, (Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> Classes) input)
        {
            var (compilation, classes) = input;

            Dictionary<INamedTypeSymbol, ClassDeclarationSyntax> uniqueTargets = [];  // 类唯一性
            foreach (var classDeclaration in classes)
            {
                SemanticModel model = compilation.GetSemanticModel(classDeclaration.SyntaxTree);
                var classSymbol = model.GetDeclaredSymbol(classDeclaration);
                if (classSymbol == null)
                    continue;

                if (!uniqueTargets.TryGetValue(classSymbol, out _))
                {
                    uniqueTargets.Add(classSymbol, classDeclaration);
                }
            }

            Dictionary<string, StringBuilder> generatedSources = []; // [ Key:文件名 | Value:源码 ]

            foreach (var roslyn in uniqueTargets.Select(t => new ViewRoslyn(t.Value, t.Key, compilation)).Where(r => r.IsView))
            {
                var fullName = AnalizeHelper.GetViewFileName(roslyn.Symbol, roslyn.Syntax);
                if (generatedSources.TryGetValue(fullName, out var builder))
                {
                    builder.Append(roslyn.Generate());
                }
                else
                {
                    var newBuilder = new StringBuilder();
                    newBuilder.Append(roslyn.Generate());
                    generatedSources.Add(fullName, newBuilder);
                }
            }

            foreach (var kvp in generatedSources)
            {
                context.AddSource(kvp.Key, SourceText.From(kvp.Value.ToString().ReplaceBrushes().ReplaceGradientSpreadMethods(), Encoding.UTF8));
            }  // 输出生成内容
        }
    }
}
