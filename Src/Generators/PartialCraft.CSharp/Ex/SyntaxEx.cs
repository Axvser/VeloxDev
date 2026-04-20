using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace PartialCraft.CSharp;

public static class SyntaxEx
{
    public static bool IsPartial(this SyntaxNode node)
        => node switch
        {
            ClassDeclarationSyntax cla => IsPartial(cla),
            StructDeclarationSyntax str => IsPartial(str),
            InterfaceDeclarationSyntax inter => IsPartial(inter),
            RecordDeclarationSyntax rec => IsPartial(rec),
            MethodDeclarationSyntax meth => IsPartial(meth),
            EnumDeclarationSyntax enu => IsPartial(enu),
            DelegateDeclarationSyntax del => IsPartial(del),
            ConstructorDeclarationSyntax con => IsPartial(con),
            DestructorDeclarationSyntax des => IsPartial(des),
            OperatorDeclarationSyntax ope => IsPartial(ope),
            ConversionOperatorDeclarationSyntax cov => IsPartial(cov),
            PropertyDeclarationSyntax pro => IsPartial(pro),
            IndexerDeclarationSyntax ind => IsPartial(ind),
            EventDeclarationSyntax eve => IsPartial(eve),
            EventFieldDeclarationSyntax evf => IsPartial(evf),
            FieldDeclarationSyntax fie => IsPartial(fie),
            NamespaceDeclarationSyntax nam => IsPartial(nam),
            FileScopedNamespaceDeclarationSyntax fil => IsPartial(fil),
            GlobalStatementSyntax glo => IsPartial(glo),
            IncompleteMemberSyntax inc => IsPartial(inc),
            _ => false,
        };
    public static bool IsPartial(this ClassDeclarationSyntax node)
        => node.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    public static bool IsPartial(this StructDeclarationSyntax node)
        => node.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    public static bool IsPartial(this InterfaceDeclarationSyntax node)
        => node.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    public static bool IsPartial(this RecordDeclarationSyntax node)
        => node.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    public static bool IsPartial(this MethodDeclarationSyntax node)
        => node.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    public static bool IsPartial(this EnumDeclarationSyntax node)
        => node.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    public static bool IsPartial(this DelegateDeclarationSyntax node)
        => node.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    public static bool IsPartial(this ConstructorDeclarationSyntax node)
        => node.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    public static bool IsPartial(this DestructorDeclarationSyntax node)
        => node.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    public static bool IsPartial(this OperatorDeclarationSyntax node)
        => node.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    public static bool IsPartial(this ConversionOperatorDeclarationSyntax node)
        => node.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    public static bool IsPartial(this PropertyDeclarationSyntax node)
        => node.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    public static bool IsPartial(this IndexerDeclarationSyntax node)
        => node.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    public static bool IsPartial(this EventDeclarationSyntax node)
        => node.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    public static bool IsPartial(this EventFieldDeclarationSyntax node)
        => node.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    public static bool IsPartial(this FieldDeclarationSyntax node)
        => node.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    public static bool IsPartial(this NamespaceDeclarationSyntax node)
        => node.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    public static bool IsPartial(this FileScopedNamespaceDeclarationSyntax node)
        => node.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    public static bool IsPartial(this GlobalStatementSyntax node)
        => node.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    public static bool IsPartial(this IncompleteMemberSyntax node)
        => node.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
}
