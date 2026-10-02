using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;

namespace VeloxDev.Generators.Base
{
    /// <summary>
    /// The names other generators give to members they write.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Source generators cannot see one another's output, so a generator that wants to reference a member another
    /// generator produces has to reproduce its naming rule rather than read the result. Two rules matter here:
    /// <c>[VeloxProperty]</c> fields become properties, and <c>[VeloxCommand]</c> methods become command properties.
    /// </para>
    /// <para>
    /// Both rules therefore live in exactly one place, shared with the writers that apply them. If a rule moves and
    /// this copy does not, the context tree starts naming members that do not exist — which fails at the consumer's
    /// compile, not here.
    /// </para>
    /// </remarks>
    internal static class AIContextNaming
    {
        private const string VeloxCommandAttributeName = "VeloxDev.MVVM.VeloxCommandAttribute";

        /// <summary>The automatic command name, replaced by the method's own name when the author leaves it alone.</summary>
        internal const string AutoCommandName = "Auto";

        /// <summary>
        /// The property name a <c>[VeloxProperty]</c> field is promoted to.
        /// </summary>
        /// <param name="fieldName">The field's own name.</param>
        /// <returns>
        /// The property name, or an empty string when the field's name yields no legal identifier — the caller
        /// reports a diagnostic rather than emitting something that will not compile.
        /// </returns>
        internal static string PromotedPropertyName(string fieldName)
        {
            var start = fieldName.StartsWith("_") ? 1 : 0;

            // 单字符 "_" 会越界；首字符不是字母/下划线（如 "_1x"）推不出合法标识符。
            if (start >= fieldName.Length) return string.Empty;

            var first = fieldName[start];
            if (!char.IsLetter(first) && first != '_') return string.Empty;

            return char.ToUpper(first) + fieldName.Substring(start + 1);
        }

        /// <summary>
        /// The command property name a <c>[VeloxCommand]</c> method is written as.
        /// </summary>
        /// <param name="method">The annotated method.</param>
        /// <returns>The property name, such as <c>SaveCommand</c>.</returns>
        internal static string CommandPropertyName(IMethodSymbol method)
            => CommandBaseName(method) + "Command";

        /// <summary>
        /// The command's name before the <c>Command</c> suffix — what <c>Auto</c> resolves to.
        /// </summary>
        /// <param name="method">The annotated method.</param>
        /// <returns>The base name the property is built from.</returns>
        internal static string CommandBaseName(IMethodSymbol method)
        {
            var name = ReadCommandName(method);

            if (name == AutoCommandName)
                name = method.Name.Replace("Async", "");

            return name;
        }

        /// <summary>The command's configured name, positional or named, defaulting to <c>Auto</c>.</summary>
        private static string ReadCommandName(IMethodSymbol method)
        {
            var attribute = method.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Replace("global::", string.Empty) == VeloxCommandAttributeName);

            if (attribute is null) return AutoCommandName;

            if (attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is string positional)
                return positional;

            foreach (var named in attribute.NamedArguments)
            {
                if (named.Key == "Name" && named.Value.Value is string value) return value;
            }

            return AutoCommandName;
        }

        /// <summary>
        /// Whether the attribute is present at all, whatever its arguments.
        /// </summary>
        /// <param name="symbol">The member to test.</param>
        /// <param name="metadataName">The attribute's full metadata name.</param>
        /// <returns><see langword="true"/> when the member carries it.</returns>
        internal static bool HasAttribute(ISymbol symbol, string metadataName)
            => symbol.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Replace("global::", string.Empty) == metadataName);
    }
}
