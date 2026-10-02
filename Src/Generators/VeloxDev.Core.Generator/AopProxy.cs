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
    public class AopProxy : IIncrementalGenerator
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
                var writer = new AopWriter();
                writer.Initialize(syntax, symbol);
                if (!writer.CanWrite()) continue;

                // 只出扩展方法。那句 bodyless partial 已经不需要了 —— 见 AopWriter 上的注释。
                context.AddSource(
                    writer.GetExtensionFileName(),
                    SourceText.From(writer.WriteExtension(), Encoding.UTF8));
            }
        }
    }
}