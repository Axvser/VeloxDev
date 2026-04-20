using Microsoft.CodeAnalysis;

namespace PartialCraft.CSharp;

public static class ContextEx
{
    public static TSyntax GetSyntax<TSyntax>(this GeneratorSyntaxContext context)
        where TSyntax : SyntaxNode
    {
        var classDeclaration = (TSyntax)context.Node;
        return classDeclaration;
    }
}
