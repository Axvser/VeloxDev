using Microsoft.CodeAnalysis;

namespace VeloxDev.Generators
{
    /// <summary>
    /// The diagnostics the generators report.
    /// </summary>
    /// <remarks>
    /// <para>
    /// They exist to move a failure out of the generated file and onto the author's own line. Without one, an
    /// unsupported <c>[VeloxCommand]</c> shape surfaced as <c>CS1503</c>/<c>CS0407</c> inside
    /// <c>*_Commands.g.cs</c>, which names a method group and a constructor the author never wrote.
    /// </para>
    /// <para>
    /// IDs are prefixed by module and then by kind: <c>VELOX_MVVM_CMD…</c> for commands, <c>VELOX_MVVM_PROP…</c>
    /// for properties. This deliberately exceeds two of the guidelines in "Choose diagnostic IDs" — the IDs run
    /// longer than 15 characters and carry an underscore — because the module and kind segments are what make a
    /// diagnostic identifiable at a glance. Renaming the earlier <c>VELOXCMD001</c> was a source-breaking change:
    /// any existing <c>#pragma warning disable VELOXCMD001</c> no longer suppresses it.
    /// </para>
    /// </remarks>
    public static class Diagnostics
    {
        private const string Category = "VeloxDev.MVVM";
        private const string JsonCategory = "VeloxDev.Serialization";

        /// <summary>
        /// The project's language version is below what the generated code is written in.
        /// </summary>
        /// <remarks>
        /// Warning rather than error, and raised on the author's own type rather than left to surface inside a
        /// <c>.g.cs</c>: the generated file is valid C#, it is the project's <c>LangVersion</c> that is too low, and
        /// the author is the only one who can raise it. Collection expressions are what the generated code needs
        /// today; when that moves, this moves with it.
        /// </remarks>
        public static readonly DiagnosticDescriptor LanguageVersionTooLow = new(
            id: "VELOX_LANGVERSION001",
            title: "LangVersion is below what the generated code needs",
            messageFormat: "Generated code uses C# {0} syntax; this project's LangVersion maps to {1}. Raise <LangVersion> to at least {0} (or 'latest').",
            category: "VeloxDev.Generators",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <summary>
        /// A <c>[VeloxCommand]</c> method whose shape cannot be turned into a command.
        /// </summary>
        /// <remarks>
        /// Error rather than warning: the generated file would not have compiled anyway, so the only question is
        /// which message the author gets.
        /// </remarks>
        public static readonly DiagnosticDescriptor UnsupportedCommandSignature = new(
            id: "VELOX_MVVM_CMD001",
            title: "Unsupported [VeloxCommand] signature",
            messageFormat: "'{0}' cannot be turned into a command: {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// A <c>[VeloxProperty]</c> declaration that cannot produce the property it asks for.
        /// </summary>
        /// <remarks>
        /// Error rather than warning: every cause — a broken pair, an ambiguous field name, or a field that
        /// cannot back a writable property — means the generated member is not emitted at all. A warning would
        /// leave a silent gap where the author expected a property.
        /// </remarks>
        public static readonly DiagnosticDescriptor ConflictingPropertyDeclaration = new(
            id: "VELOX_MVVM_PROP001",
            title: "Conflicting [VeloxProperty] declaration",
            messageFormat: "'{0}' cannot be generated: {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// A <c>[VeloxProperty]</c> name the generator cannot derive a valid counterpart from.
        /// </summary>
        /// <remarks>
        /// Warning rather than error: the name itself is legal C#, it just yields nothing usable — for example a
        /// field named <c>_</c> or one starting with a digit. Skipping it keeps an uncompilable member out of the
        /// generated file.
        /// </remarks>
        public static readonly DiagnosticDescriptor UnusablePropertyName = new(
            id: "VELOX_MVVM_PROP002",
            title: "Unusable [VeloxProperty] name",
            messageFormat: "'{0}' cannot be generated: {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <summary>
        /// A <c>[VeloxProperty]</c> property the generator cannot complete the accessors of.
        /// </summary>
        /// <remarks>
        /// Warning rather than error: the declaration is legal C# and compiles on its own — it just never gets the
        /// notification wiring, which surfaces later at a binding rather than at this line. The generator can only
        /// add code, so a property whose accessors are already written cannot be rewritten into a notifying one.
        /// </remarks>
        public static readonly DiagnosticDescriptor NonPartialProperty = new(
            id: "VELOX_MVVM_PROP003",
            title: "Uncompletable [VeloxProperty] property",
            messageFormat: "'{0}' cannot be generated: {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <summary>
        /// Two methods share a name and an argument count, so the agent context tree can expose only one.
        /// </summary>
        /// <remarks>
        /// Warning rather than error: the tree is a description of the surface, and losing one of two
        /// indistinguishable overloads is a smaller loss than failing the build. It is still worth saying out
        /// loud — the reflection path picks whichever the runtime happened to enumerate first, so the two
        /// disagree silently otherwise.
        /// </remarks>
        public static readonly DiagnosticDescriptor AmbiguousMethodOverload = new(
            id: "VELOX_AI_TREE001",
            title: "Ambiguous method overload in the agent context tree",
            messageFormat: "'{0}' takes {1} argument(s) in more than one overload; only the first is reachable through the agent context tree",
            category: "VeloxDev.AI",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <summary>
        /// A type named by <c>[Archivable(typeof(…))]</c> that this assembly cannot emit an entry for.
        /// </summary>
        /// <remarks>
        /// Error rather than warning: the declaration asks for a type to take part in the archive format, and the
        /// generator cannot honour it — the readers and writers are emitted into the assembly that declares the
        /// type, so a type from another assembly, an abstract one, or one generated code cannot reach simply gets
        /// no entry. Skipping it would surface far away as a <c>MissingWriter</c> at run time, long after the line
        /// that could have fixed it.
        /// </remarks>
        public static readonly DiagnosticDescriptor UnsupportedArchivableRoot = new(
            id: "VELOX_JSON_ARCH001",
            title: "Unsupported [Archivable] root",
            messageFormat: "'{0}' named by '{1}' cannot take part in the archive format: {2}",
            category: JsonCategory,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// More than one method on a type carries the same serialization callback attribute.
        /// </summary>
        /// <remarks>
        /// Warning rather than error: the BCL formatter throws on this, but here the choice is deterministic
        /// (declaration order), so the build can proceed — the author still needs to know that only one of the
        /// two runs and which one it picked.
        /// </remarks>
        public static readonly DiagnosticDescriptor AmbiguousSerializationHook = new(
            id: "VELOX_JSON_HOOK001",
            title: "Ambiguous serialization callback",
            messageFormat: "'{1}' carries '{0}' on more than one method; only the first is called",
            category: JsonCategory,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <summary>
        /// A member declaration the generator cannot honour — an unreachable member, a rename with nothing to
        /// rename it to, or a <c>[JsonIgnore]</c> condition this format has no reading for.
        /// </summary>
        /// <remarks>
        /// Error rather than warning: the generated file would either not compile or quietly carry a different
        /// document than the declaration asks for, and a silently missing member is the one failure a reader of
        /// the archive has no way to notice.
        /// </remarks>
        public static readonly DiagnosticDescriptor UnusableArchiveDeclaration = new(
            id: "VELOX_JSON_MEMBER001",
            title: "Unusable member declaration",
            messageFormat: "'{0}' cannot be marked: {1}",
            category: JsonCategory,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// <c>[Archive(KeepField)]</c> on a field that already has a property beside it.
        /// </summary>
        /// <remarks>
        /// Warning rather than error: the declaration is contradictory but not unrepresentable — the member is
        /// emitted either way, and the default rules already take the property. Speaking up matters because the
        /// author asked for the field and is silently getting the property, which is a different member with the
        /// same name.
        /// </remarks>
        public static readonly DiagnosticDescriptor ConflictingArchiveField = new(
            id: "VELOX_JSON_MEMBER002",
            title: "Conflicting [Archive(KeepField)]",
            messageFormat: "'{0}' cannot be taken as a field: {1}",
            category: JsonCategory,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <summary>
        /// A serialization callback the generated code cannot reach, or whose signature it cannot call.
        /// </summary>
        /// <remarks>
        /// Error rather than warning: the callback would silently not run, and a callback exists to change what
        /// the document holds — <c>Anchor</c> uses <c>[OnSerializing]</c> to expand a collapsed transient into
        /// its raw values. Skipping it would change the bytes without any other symptom.
        /// </remarks>
        public static readonly DiagnosticDescriptor UnreachableSerializationHook = new(
            id: "VELOX_JSON_HOOK002",
            title: "Unreachable serialization callback",
            messageFormat: "'{0}' on '{1}' cannot be called by the generated serializer: {2}",
            category: JsonCategory,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// A type parameter whose constraints name no class or interface the generator can follow.
        /// </summary>
        /// <remarks>
        /// Warning rather than error: the declaration is legal and the build can proceed — what is lost is the
        /// family of types the parameter may hold, which is exactly what cannot be worked out. Saying so matters
        /// because the symptom otherwise waits until run time, as a <c>MissingWriter</c> naming a type the author
        /// never mentioned. See <c>Base/VeloxJsonModel.cs</c>.
        /// </remarks>
        public static readonly DiagnosticDescriptor UnresolvableTypeParameter = new(
            id: "VELOX_JSON_GENERIC001",
            title: "Type parameter with no resolvable constraint",
            messageFormat: "'{0}' on '{1}' has no class or interface constraint, so the types it may hold cannot be taken into the archive format",
            category: JsonCategory,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <summary>
        /// A type no declaration names, which the archive format can nevertheless write, and why.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Informational rather than a warning: nothing is wrong. It exists because the set of types a document can
        /// hold is decided by a closure over member types, derived types and type-parameter constraints — so a type
        /// reached only through inheritance, a map key or a constraint is not visible in any one declaration.
        /// </para>
        /// <para>
        /// Only those are reported. A root, a member's declared type, a type named by <c>[Archivable]</c> and a
        /// constraint type are all written down in source, so listing them would restate what the author already
        /// said — and it is those that multiply, once per target framework. The generated file carries the full
        /// list in its header instead. <see cref="UnresolvableTypeParameter"/> covers the case where the closure
        /// cannot be worked out at all.
        /// </para>
        /// </remarks>
        public static readonly DiagnosticDescriptor SerializationSurface = new(
            id: "VELOX_JSON_INCLUDE001",
            title: "Type taken into the archive format",
            messageFormat: "'{0}' is serialized by VeloxDev: {1}",
            category: JsonCategory,
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true);
    }
}
