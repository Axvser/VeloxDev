using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace VeloxDev.Generators.Base
{
    internal static class AnalizeHelper
    {
        internal const string NAME_ASPECTORIENTED = "AspectOriented";

        internal static bool IsAopClass(ClassDeclarationSyntax classDecl)
        {
            return classDecl.Members
                    .OfType<MemberDeclarationSyntax>()
                    .Any(HasAspectOriented);
        }

        /// <summary>
        /// Every declaration of <paramref name="symbol"/>. A type split over several partial files has
        /// one per file, and the attributed members can be on any of them.
        /// </summary>
        internal static IEnumerable<ClassDeclarationSyntax> Declarations(INamedTypeSymbol symbol)
        {
            return symbol.DeclaringSyntaxReferences
                         .Select(reference => reference.GetSyntax())
                         .OfType<ClassDeclarationSyntax>();
        }

        /// <summary>
        /// Every member declared across all partial declarations of <paramref name="symbol"/>.
        /// </summary>
        internal static IEnumerable<MemberDeclarationSyntax> Members(INamedTypeSymbol symbol)
        {
            return Declarations(symbol).SelectMany(declaration => declaration.Members);
        }

        /// <summary>
        /// True when any declaration of <paramref name="symbol"/> carries an
        /// <c>[AspectOriented]</c> member. Symbol-based so that the answer does not depend on which
        /// partial declaration the generator happened to pick.
        /// </summary>
        internal static bool IsAopClass(INamedTypeSymbol symbol)
        {
            return Members(symbol).Any(HasAspectOriented);
        }

        private static bool HasAspectOriented(MemberDeclarationSyntax member)
        {
            return member.AttributeLists
                         .SelectMany(attributeList => attributeList.Attributes)
                         .Any(attribute => attribute.Name.ToString() == NAME_ASPECTORIENTED);
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
