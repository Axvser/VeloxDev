using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace VeloxDev.WinForms.Generator;

/// <summary>A small indented source writer.</summary>
internal sealed class CodeBuilder
{
    private readonly StringBuilder _builder = new();

    public int IndentLevel { get; private set; }

    public void Line(string text = "")
    {
        if (text.Length > 0)
        {
            _builder.Append(' ', IndentLevel * 4);
            _builder.Append(text);
        }

        _builder.Append('\n');
    }

    public void Open(string text)
    {
        Line(text);
        Line("{");
        IndentLevel++;
    }

    /// <summary>Appends already-formatted text, prefixing each line with the current indentation.</summary>
    public void Raw(string text)
    {
        var normalized = text.Replace("\r\n", "\n").TrimEnd('\n');
        foreach (var line in normalized.Split('\n'))
        {
            if (line.Length == 0)
            {
                _builder.Append('\n');
                continue;
            }

            _builder.Append(' ', IndentLevel * 4);
            _builder.Append(line);
            _builder.Append('\n');
        }
    }

    public void Close(string suffix = "")
    {
        IndentLevel--;
        Line("}" + suffix);
    }

    public override string ToString() => _builder.ToString();
}

/// <summary>Symbol, syntax and literal helpers shared by the two generators.</summary>
internal static class SyntaxHelpers
{
    public const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    public const string PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    public const string RuntimeNamespace = "VeloxDev.WinForms.Xaml";
    public const string ControlsNamespace = "VeloxDev.WinForms.Xaml.Controls";
    public const string ShapesNamespace = "VeloxDev.WinForms.Xaml.Controls.Shapes";
    public const string XamlControlMetadataName = "VeloxDev.WinForms.Xaml.Controls.Control";
    public const string ControlMetadataName = "System.Windows.Forms.Control";
    public const string DependencyPropertyMetadataName = "VeloxDev.WinForms.Xaml.DependencyProperty";
    public const string VisualElementMetadataName = "VeloxDev.WinForms.Xaml.Visuals.VisualElement";

    public static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static string TypeName(ITypeSymbol type) => type.ToDisplayString(TypeFormat);

    /// <summary>Type name for <c>typeof</c> and other metadata positions, where nullable annotations are invalid.</summary>
    public static string TypeNameForTypeof(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    public static string Fqn(string simpleName) => $"global::{RuntimeNamespace}.{simpleName}";

    public static string Quote(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    public static bool IsPartial(INamedTypeSymbol type)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is TypeDeclarationSyntax declaration
                && declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                return true;
            }
        }

        return false;
    }

    public static bool DerivesFrom(ITypeSymbol? type, string metadataName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == metadataName)
            {
                return true;
            }
        }

        return false;
    }

    public static string AccessibilityText(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Protected => "protected",
        Accessibility.Private => "private",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => "public",
    };

    public static string TypeParameters(INamedTypeSymbol type)
    {
        if (type.TypeParameters.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder("<");
        for (var index = 0; index < type.TypeParameters.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append(type.TypeParameters[index].Name);
        }

        builder.Append('>');
        return builder.ToString();
    }

    /// <summary>Wraps <paramref name="members"/> in a <c>partial</c> declaration chain for <paramref name="owner"/>.</summary>
    public static string WrapInPartialType(INamedTypeSymbol owner, string members)
    {
        var chain = new Stack<INamedTypeSymbol>();
        for (var current = owner; current is not null; current = current.ContainingType)
        {
            chain.Push(current);
        }

        var levels = 0;
        var builder = new StringBuilder();

        var ns = owner.ContainingNamespace;
        if (ns is { IsGlobalNamespace: false })
        {
            builder.Append("namespace ").Append(ns.ToDisplayString()).Append("\n{\n");
            levels++;
        }

        while (chain.Count > 0)
        {
            var type = chain.Pop();
            builder.Append(' ', levels * 4)
                .Append("partial class ")
                .Append(type.Name)
                .Append(TypeParameters(type))
                .Append("\n")
                .Append(' ', levels * 4)
                .Append("{\n");
            levels++;
        }

        builder.Append(Indent(members, levels));

        while (levels > 0)
        {
            levels--;
            builder.Append(' ', levels * 4).Append("}\n");
        }

        return builder.ToString();
    }

    public static string Indent(string text, int levels)
    {
        if (levels == 0)
        {
            return text;
        }

        var prefix = new string(' ', levels * 4);
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var builder = new StringBuilder();

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length > 0)
            {
                builder.Append(prefix);
            }

            builder.Append(line);
            if (index < lines.Length - 1)
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    public static string SanitizeIdentifier(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        }

        if (builder.Length == 0 || char.IsDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }

    public static string StableSuffix(string text)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in text)
            {
                hash = (hash ^ character) * 16777619u;
            }

            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }
    }

    public static bool TryGetAttributeName(AttributeData attribute, string fallback, out string name)
    {
        name = fallback;
        if (attribute.ConstructorArguments.Length > 0
            && attribute.ConstructorArguments[0].Value is string explicitName
            && !string.IsNullOrWhiteSpace(explicitName))
        {
            name = explicitName;
            return true;
        }

        return false;
    }

    public static string? GetNamedString(AttributeData attribute, string argumentName)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == argumentName && argument.Value.Value is string value)
            {
                return value;
            }
        }

        return null;
    }
}

/// <summary>Converts XAML and attribute default-value text into C# expressions.</summary>
internal static class LiteralFactory
{
    public static bool TryCreate(ITypeSymbol type, string? text, out string expression, out string? error)
    {
        error = null;

        if (text is null)
        {
            expression = $"default({SyntaxHelpers.TypeName(type)})";
            return true;
        }

        if (text.StartsWith("{}", StringComparison.Ordinal))
        {
            text = text.Substring(2);
        }

        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            if (!TryCreate(nullable.TypeArguments[0], text, out var inner, out error))
            {
                expression = string.Empty;
                return false;
            }

            expression = $"({SyntaxHelpers.TypeName(type)})({inner})";
            return true;
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_String:
            case SpecialType.System_Object:
                expression = SyntaxHelpers.Quote(text);
                return true;

            case SpecialType.System_Boolean:
                if (bool.TryParse(text, out var boolean))
                {
                    expression = boolean ? "true" : "false";
                    return true;
                }

                break;

            case SpecialType.System_Char:
                if (text.Length == 1)
                {
                    expression = SymbolDisplay.FormatLiteral(text[0], quote: true);
                    return true;
                }

                break;

            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
                var hasUnsigned = ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer);
                var hasSigned = long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signed);
                if (hasUnsigned || hasSigned)
                {
                    expression = type.SpecialType switch
                    {
                        SpecialType.System_Int64 => signed.ToString(CultureInfo.InvariantCulture) + "L",
                        SpecialType.System_UInt64 => integer.ToString(CultureInfo.InvariantCulture) + "UL",
                        SpecialType.System_UInt32 => integer.ToString(CultureInfo.InvariantCulture) + "U",
                        SpecialType.System_Byte => "(byte)" + integer.ToString(CultureInfo.InvariantCulture),
                        SpecialType.System_SByte => "(sbyte)" + signed.ToString(CultureInfo.InvariantCulture),
                        SpecialType.System_Int16 => "(short)" + signed.ToString(CultureInfo.InvariantCulture),
                        SpecialType.System_UInt16 => "(ushort)" + integer.ToString(CultureInfo.InvariantCulture),
                        _ => signed.ToString(CultureInfo.InvariantCulture),
                    };
                    return true;
                }

                break;

            case SpecialType.System_Single:
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var single))
                {
                    expression = single.ToString("R", CultureInfo.InvariantCulture) + "F";
                    return true;
                }

                break;

            case SpecialType.System_Double:
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    expression = number.ToString("R", CultureInfo.InvariantCulture) + "D";
                    return true;
                }

                break;

            case SpecialType.System_Decimal:
                if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue))
                {
                    expression = decimalValue.ToString(CultureInfo.InvariantCulture) + "M";
                    return true;
                }

                break;
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            return TryCreateEnum((INamedTypeSymbol)type, text, out expression, out error);
        }

        var metadataName = type.ToDisplayString();
        switch (metadataName)
        {
            case "System.Drawing.Color":
                expression = $"{SyntaxHelpers.Fqn("XamlRuntime")}.ParseColor({SyntaxHelpers.Quote(text)})";
                return true;

            case "System.Windows.Forms.Padding":
                expression = $"{SyntaxHelpers.Fqn("XamlRuntime")}.ParsePadding({SyntaxHelpers.Quote(text)})";
                return true;

            case "VeloxDev.WinForms.Xaml.Controls.Thickness":
                expression = $"{SyntaxHelpers.Fqn("XamlRuntime")}.ParseThickness({SyntaxHelpers.Quote(text)})";
                return true;

            case "VeloxDev.WinForms.Xaml.Controls.GridLength":
                expression = $"global::{SyntaxHelpers.ControlsNamespace}.GridLength.Parse({SyntaxHelpers.Quote(text)})";
                return true;

            case "System.Drawing.Size":
                expression = $"{SyntaxHelpers.Fqn("XamlRuntime")}.ParseSize({SyntaxHelpers.Quote(text)})";
                return true;

            case "System.Drawing.SizeF":
                expression = $"{SyntaxHelpers.Fqn("XamlRuntime")}.ParseSizeF({SyntaxHelpers.Quote(text)})";
                return true;

            case "System.Drawing.Point":
                expression = $"{SyntaxHelpers.Fqn("XamlRuntime")}.ParsePoint({SyntaxHelpers.Quote(text)})";
                return true;

            case "System.Drawing.PointF":
                expression = $"{SyntaxHelpers.Fqn("XamlRuntime")}.ParsePointF({SyntaxHelpers.Quote(text)})";
                return true;

            case "System.TimeSpan":
                expression = $"global::System.TimeSpan.Parse({SyntaxHelpers.Quote(text)}, global::System.Globalization.CultureInfo.InvariantCulture)";
                return true;
        }

        expression = string.Empty;
        error = $"no literal conversion exists for '{metadataName}'";
        return false;
    }

    private static bool TryCreateEnum(INamedTypeSymbol enumType, string text, out string expression, out string? error)
    {
        error = null;
        var pieces = text.Split(',');
        var values = new List<string>();

        foreach (var piece in pieces)
        {
            var name = piece.Trim();
            var field = enumType.GetMembers(name)
                .OfType<IFieldSymbol>()
                .FirstOrDefault(candidate => candidate.HasConstantValue);

            if (field is null)
            {
                if (int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                {
                    values.Add($"(({SyntaxHelpers.TypeName(enumType)})({number}))");
                    continue;
                }

                expression = string.Empty;
                error = $"'{name}' is not a member of enum '{enumType.Name}'";
                return false;
            }

            values.Add($"{SyntaxHelpers.TypeName(enumType)}.{field.Name}");
        }

        expression = string.Join(" | ", values);
        return true;
    }
}
