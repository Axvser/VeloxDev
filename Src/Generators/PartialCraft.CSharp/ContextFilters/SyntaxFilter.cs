using Microsoft.CodeAnalysis;
using PartialCraft.CSharp.Interfaces;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace PartialCraft.CSharp.ContextFilters;

public class SyntaxFilter<TSyntax, TSymbol> : IContextFilter<Tuple<ICodeWeaver<TSyntax, TSymbol>, IncrementalGeneratorInitializationContext>, Tuple<ICodeWeaver<TSyntax, TSymbol>, IncrementalValueProvider<(Compilation, ImmutableArray<TSyntax>)>>>
    where TSyntax : SyntaxNode
    where TSymbol : ISymbol
{
    public Func<TSyntax, bool>? OnFilter;

    public Tuple<ICodeWeaver<TSyntax, TSymbol>, IncrementalValueProvider<(Compilation, ImmutableArray<TSyntax>)>> Filter(Tuple<ICodeWeaver<TSyntax, TSymbol>, IncrementalGeneratorInitializationContext> context)
    {
        IncrementalValuesProvider<TSyntax> classDeclarations =
            context.Item2.SyntaxProvider.CreateSyntaxProvider<TSyntax>(
                predicate: (node, cancellationToken) => Filter(node as TSyntax, cancellationToken),
                transform: (ctx, cancellationToken) => Transform(ctx, cancellationToken))
            .Where(static m => m != null)!;
        return Tuple.Create(context.Item1, context.Item2.CompilationProvider.Combine(classDeclarations.Collect()));
    }

    public virtual bool Filter(TSyntax? node, CancellationToken token)
    {
        if (node is null) return false;
        return node.IsPartial() && (OnFilter?.Invoke(node) ?? true);
    }

    public virtual TSyntax Transform(GeneratorSyntaxContext context, CancellationToken token)
    {
        return context.GetSyntax<TSyntax>();
    }
}
