using Microsoft.CodeAnalysis;
using PartialCraft.CSharp.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace PartialCraft.CSharp.ContextFilters;

public class SymbolFilter<TSyntax, TSymbol> : IContextFilter<Tuple<SourceProductionContext, Tuple<ICodeWeaver<TSyntax, TSymbol>, Compilation, ImmutableArray<TSyntax>>>, IDictionary<TSymbol, TSyntax>>
    where TSyntax : SyntaxNode
    where TSymbol : ISymbol
{
    public Func<TSymbol, bool>? OnFilter;

    public IDictionary<TSymbol, TSyntax> Filter(Tuple<SourceProductionContext, Tuple<ICodeWeaver<TSyntax, TSymbol>, Compilation, ImmutableArray<TSyntax>>> context)
    {
        Dictionary<TSymbol, TSyntax> values = [];
        foreach (var classDeclaration in context.Item2.Item3)
        {
            SemanticModel model = context.Item2.Item2.GetSemanticModel(classDeclaration.SyntaxTree);
#pragma warning disable RS1039
            if (model.GetDeclaredSymbol(classDeclaration) is not TSymbol classSymbol)
                continue;
#pragma warning restore RS1039

            if (!values.TryGetValue(classSymbol, out _) && (OnFilter?.Invoke(classSymbol) ?? true))
            {
                values.Add(classSymbol, classDeclaration);
            }
        }
        return values;
    }
}
