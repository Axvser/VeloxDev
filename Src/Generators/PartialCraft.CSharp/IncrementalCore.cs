using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using PartialCraft.CSharp.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace PartialCraft.CSharp;

public abstract class IncrementalCore<TSyntax, TSymbol> : IIncrementalGenerator
    where TSyntax : SyntaxNode
    where TSymbol : ISymbol
{
    protected readonly HashSet<ICodeWeaver<TSyntax, TSymbol>> Weavers = [];

    public virtual void Initialize(IncrementalGeneratorInitializationContext context)
    {
        foreach (var weaver in Weavers)
        {
            var filterResult = weaver.SyntaxFilter.Filter(Tuple.Create(weaver, context));
            var compilationWithSyntax = filterResult.Item2;

            var combinedProvider = compilationWithSyntax.Select(
                (tuple, cancellationToken) =>
                {
                    var (compilation, syntaxArray) = tuple;
                    return Tuple.Create(weaver, compilation, syntaxArray);
                });

            context.RegisterSourceOutput(combinedProvider, GenerateSource);
        }
    }

    protected virtual void GenerateSource(SourceProductionContext context,
        Tuple<ICodeWeaver<TSyntax, TSymbol>, Compilation, ImmutableArray<TSyntax>> input)
    {
        var symbols = input.Item1.SymbolFilter.Filter(
            Tuple.Create(context, Tuple.Create(input.Item1, input.Item2, input.Item3)));

        foreach (var weaver in Weavers)
        {
            foreach (var kvp in symbols)
            {
                weaver.Initialize(kvp.Value, kvp.Key);
                if (weaver.CanWeave())
                {
                    context.AddSource(
                        weaver.GetFileName(),
                        SourceText.From(weaver.Weave(), weaver.GetEncoding()));
                }
            }
        }
    }
}