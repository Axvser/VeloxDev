using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace VeloxDev.Core.Generator.Generator
{
    [Generator(LanguageNames.CSharp)]
    public class MonoBehaviourModule : GeneratorBase
    {
        public override void GenerateSource(SourceProductionContext context, (Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> Classes) input)
        {
            var value = GetFiltered(input);
        }
    }
}
