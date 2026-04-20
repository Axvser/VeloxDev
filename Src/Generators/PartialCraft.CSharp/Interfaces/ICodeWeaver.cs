using Microsoft.CodeAnalysis;
using PartialCraft.CSharp.ContextFilters;
using System.Text;

namespace PartialCraft.CSharp.Interfaces;

public interface ICodeWeaver<TSyntax, TSymbol>
    where TSyntax : SyntaxNode
    where TSymbol : ISymbol
{
    public SyntaxFilter<TSyntax, TSymbol>
        SyntaxFilter
    { get; }

    public SymbolFilter<TSyntax, TSymbol>
        SymbolFilter
    { get; }

    public void Initialize(TSyntax syntax, TSymbol symbol);
    public bool CanWeave();
    public Encoding GetEncoding();
    public string Weave();
    public string GetFileName();
}
