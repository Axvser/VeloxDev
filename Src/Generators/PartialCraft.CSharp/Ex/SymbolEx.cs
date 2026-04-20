using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace PartialCraft.CSharp;

public static class SymbolEx
{
    public static bool IsPartial(this ISymbol symbol) 
        => symbol switch
        {
            INamedTypeSymbol cla => IsPartial(cla),
            ITypeSymbol type => IsPartial(type),
            IMethodSymbol meth => IsPartial(meth),
            IPropertySymbol pro => IsPartial(pro),
            IEventSymbol eve => IsPartial(eve),
            IFieldSymbol fie => IsPartial(fie),
            INamespaceSymbol nam => IsPartial(nam),
            _ => false,
        };
    public static bool IsPartial(this INamedTypeSymbol symbol)
        => symbol.DeclaringSyntaxReferences.FirstOrDefault(
            syntaxRef => syntaxRef.GetSyntax() is PropertyDeclarationSyntax propertySyntax &&
            propertySyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword))) is not null;
    public static bool IsPartial(this ITypeSymbol symbol)
    => symbol.DeclaringSyntaxReferences.FirstOrDefault(
        syntaxRef => syntaxRef.GetSyntax() is PropertyDeclarationSyntax propertySyntax &&
        propertySyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword))) is not null;
    public static bool IsPartial(this IMethodSymbol symbol)
        => symbol.DeclaringSyntaxReferences.FirstOrDefault(
            syntaxRef => syntaxRef.GetSyntax() is PropertyDeclarationSyntax propertySyntax &&
            propertySyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword))) is not null;
    public static bool IsPartial(this IPropertySymbol symbol)
        => symbol.DeclaringSyntaxReferences.FirstOrDefault(
            syntaxRef => syntaxRef.GetSyntax() is PropertyDeclarationSyntax propertySyntax &&
            propertySyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword))) is not null;
    public static bool IsPartial(this IEventSymbol symbol)
        => symbol.DeclaringSyntaxReferences.FirstOrDefault(
            syntaxRef => syntaxRef.GetSyntax() is PropertyDeclarationSyntax propertySyntax &&
            propertySyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword))) is not null;
    public static bool IsPartial(this IFieldSymbol symbol)
        => symbol.DeclaringSyntaxReferences.FirstOrDefault(
            syntaxRef => syntaxRef.GetSyntax() is PropertyDeclarationSyntax propertySyntax &&
            propertySyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword))) is not null;
    public static bool IsPartial(this INamespaceSymbol symbol)
        => symbol.DeclaringSyntaxReferences.FirstOrDefault(
            syntaxRef => syntaxRef.GetSyntax() is PropertyDeclarationSyntax propertySyntax &&
            propertySyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword))) is not null;

    public static string GetAutoFieldName(this IPropertySymbol symbol) => symbol.Name.StartsWith("_") ? char.ToUpper(symbol.Name[1]) + symbol.Name.Substring(2) : char.ToUpper(symbol.Name[0]) + symbol.Name.Substring(1);
    public static string GetAutoPropertyName(this IFieldSymbol symbol) => char.IsUpper(symbol.Name[0]) ? "_" + char.ToLower(symbol.Name[0]) + symbol.Name.Substring(1) : "_" + symbol.Name;
    public static string GetAutoCommandName(this IMethodSymbol symbol) => symbol.Name + "Command";
    public static string GetAutoGetterAccess(this IPropertySymbol symbol)
    {
        var accessorMethod = symbol.GetMethod;
        if (accessorMethod == null) return string.Empty;
        var propertyAccessibility = symbol.DeclaredAccessibility;
        var accessorAccessibility = accessorMethod.DeclaredAccessibility;
        if (accessorAccessibility == propertyAccessibility)
            return string.Empty;
        return accessorAccessibility switch
        {
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.Internal => "internal",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.Public => "public",
            _ => string.Empty
        };
    }
    public static string GetAutoSetterAccess(this IPropertySymbol symbol)
    {
        var accessorMethod = symbol.SetMethod;
        if (accessorMethod == null) return string.Empty;
        var propertyAccessibility = symbol.DeclaredAccessibility;
        var accessorAccessibility = accessorMethod.DeclaredAccessibility;
        if (accessorAccessibility == propertyAccessibility)
            return string.Empty;
        return accessorAccessibility switch
        {
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.Internal => "internal",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.Public => "public",
            _ => string.Empty
        };
    }
    
    public static string GetGlobalName(this ITypeSymbol typeSymbol)
        => typeSymbol.ToDisplayString(new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));

    public static bool IsNullable(this ITypeSymbol typeSymbol)
    {
        if (typeSymbol is INamedTypeSymbol namedType &&
            namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return true;
        }
        return typeSymbol.NullableAnnotation == NullableAnnotation.Annotated;
    }
}
