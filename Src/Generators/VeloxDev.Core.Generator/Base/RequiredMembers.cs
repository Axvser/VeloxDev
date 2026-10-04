using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace VeloxDev.Generators.Base
{
    /// <summary>
    /// C#'s <c>required</c> members — the ones every generator that writes a constructor call has to assign.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A required member is not optional at the construction site: <c>new T()</c> does not compile unless every one
    /// of them is assigned, so a generator emitting a factory has to emit the object initializer with it. Without
    /// this, any type with a required member produced a file that could not be built — a hole wide enough to make
    /// the whole type unusable.
    /// </para>
    /// <para>
    /// Read through <c>IsRequired</c> rather than by looking for <c>RequiredMemberAttribute</c>: the compiler
    /// synthesises that attribute while emitting, so it is <b>not</b> among the attributes of a symbol declared in
    /// the compilation being built — only of one read back from metadata.
    /// </para>
    /// </remarks>
    internal static class RequiredMembers
    {
        /// <summary>
        /// The names to assign in an object initializer, the type's own before its bases'.
        /// </summary>
        /// <remarks>
        /// The chain matters: <c>required</c> is inherited, and the compiler applies the same rule to a base's
        /// required member as to the type's own.
        /// </remarks>
        internal static IReadOnlyList<string> NamesOf(INamedTypeSymbol symbol)
        {
            var names = new List<string>();

            for (var current = symbol; current is not null; current = current.BaseType)
            {
                if (current.SpecialType == SpecialType.System_Object) break;

                foreach (var member in current.GetMembers())
                {
                    if (!IsRequired(member) || names.Contains(member.Name)) continue;
                    names.Add(member.Name);
                }
            }

            return names;
        }

        /// <summary>
        /// The object initializer a constructor call needs, or an empty string when the type requires nothing.
        /// </summary>
        /// <remarks>
        /// The values are <c>default!</c> on purpose: the factory only has to produce an instance the reader can
        /// fill, and the reader refuses a document that leaves a required member out.
        /// </remarks>
        internal static string InitializerFor(INamedTypeSymbol symbol)
        {
            var names = NamesOf(symbol);
            if (names.Count == 0) return string.Empty;

            var assignments = new List<string>(names.Count);
            foreach (var name in names) assignments.Add($"{name} = default!");

            return " { " + string.Join(", ", assignments) + " }";
        }

        /// <summary>Whether C# requires the member to be assigned at the construction site.</summary>
        internal static bool IsRequired(ISymbol member)
            => member is IPropertySymbol { IsRequired: true }
               || member is IFieldSymbol { IsRequired: true };
    }
}
