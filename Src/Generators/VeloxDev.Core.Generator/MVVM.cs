using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using VeloxDev.Generators.Base;
using VeloxDev.Generators.Writers;

namespace VeloxDev.Generators
{
    [Generator(LanguageNames.CSharp)]
    public class MVVM : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(
                Analizer.Filters.Targets(context).Combine(context.CompilationProvider),
                GenerateSource);
        }

        public void GenerateSource(SourceProductionContext context, (ImmutableArray<Analizer.Filters.GeneratorTarget> Targets, Compilation Compilation) input)
        {
            foreach (var (syntax, symbol) in Analizer.Filters.Resolve(input.Targets, input.Compilation))
            {
                var writer = new MVVMWriter();
                writer.Initialize(syntax, symbol);

                // 先报诊断：被拒的声明不会进产物，作者该看到的是这一条，
                // 而不是「属性凭空消失」。
                foreach (var diagnostic in writer.Diagnostics)
                {
                    context.ReportDiagnostic(diagnostic);
                }

                if (writer.CanWrite())
                {
                    context.AddSource(
                        writer.GetFileName(),
                        SourceText.From(writer.Write(), Encoding.UTF8));
                }
            }
        }

    }
}