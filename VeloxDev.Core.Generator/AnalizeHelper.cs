using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Linq;

namespace VeloxDev.Core.Generator
{
    internal static class AnalizeHelper
    {
        internal const string NAME_ASPECTORIENTED = "AspectOriented";

        internal static bool IsAopClass(ClassDeclarationSyntax classDecl)
        {
            return classDecl.Members
                    .OfType<MemberDeclarationSyntax>()
                    .Any(member => member.AttributeLists
                    .SelectMany(al => al.Attributes)
                    .Any(attr => attr.Name.ToString() == NAME_ASPECTORIENTED));
        }
        internal static string GetPropertyNameByFieldName(VariableDeclaratorSyntax variable)
        {
            if (variable.Identifier.Text.StartsWith("_"))
            {
                return char.ToUpper(variable.Identifier.Text[1]) + variable.Identifier.Text.Substring(2);
            }
            else
            {
                return char.ToUpper(variable.Identifier.Text[0]) + variable.Identifier.Text.Substring(1);
            }
        }
    }
}
