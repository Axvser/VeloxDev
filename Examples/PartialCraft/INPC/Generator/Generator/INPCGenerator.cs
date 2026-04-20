using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PartialCraft.CSharp;

namespace Generator;

[Generator(LanguageNames.CSharp)]
public class INPCGenerator : IncrementalCore<ClassDeclarationSyntax, INamedTypeSymbol>
{
    public INPCGenerator()
    {
        var inpc = new INPCWeaver();

        inpc.SyntaxFilter.OnFilter = (cls) => cls.Identifier.Text.Contains("ViewModel");

        Weavers.Add(inpc);
    }
}
