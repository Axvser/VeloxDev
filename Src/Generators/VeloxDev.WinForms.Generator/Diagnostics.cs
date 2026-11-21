using Microsoft.CodeAnalysis;

namespace VeloxDev.WinForms.Generator;

/// <summary>The diagnostics this generator reports. IDs are stable once released.</summary>
internal static class Diagnostics
{
    private const string XamlCategory = "VeloxDev.Xaml";
    private const string PropertyCategory = "VeloxDev.Xaml";

    public static readonly DiagnosticDescriptor UnknownType = new(
        "VELOX_XAML001", "Unknown XAML type",
        "The XAML type '{0}' could not be resolved",
        XamlCategory, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnknownMember = new(
        "VELOX_XAML002", "Unknown XAML member",
        "'{0}' has no property or event named '{1}'",
        XamlCategory, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedValue = new(
        "VELOX_XAML003", "Unsupported XAML value",
        "The value '{0}' cannot be converted to '{1}'",
        XamlCategory, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedConstruct = new(
        "VELOX_XAML004", "Unsupported XAML construct",
        "{0}",
        XamlCategory, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingClass = new(
        "VELOX_XAML005", "Missing x:Class",
        "A XAML file consumed by this generator needs an x:Class attribute on its root element",
        XamlCategory, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ClassNotFound = new(
        "VELOX_XAML006", "x:Class type not found",
        "The x:Class type '{0}' was not found in the compilation",
        XamlCategory, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateName = new(
        "VELOX_XAML007", "Duplicate x:Name",
        "The name '{0}' is already a member of '{1}'",
        XamlCategory, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingEventHandler = new(
        "VELOX_XAML008", "Missing event handler",
        "The event handler '{0}' was not found in '{1}'",
        XamlCategory, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NotPartial = new(
        "VELOX_XAML009", "x:Class type is not partial",
        "The x:Class type '{0}' must be declared partial",
        XamlCategory, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedResource = new(
        "VELOX_XAML010", "Unsupported resource",
        "{0}",
        XamlCategory, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedRoot = new(
        "VELOX_XAML011", "Unsupported root",
        "The root element '{0}' is not assignable from '{1}'",
        XamlCategory, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ParseError = new(
        "VELOX_XAML012", "XAML parse error",
        "The XAML file could not be parsed: {0}",
        XamlCategory, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnknownAttachedProperty = new(
        "VELOX_XAML013", "Unknown attached property",
        "The attached property '{0}.{1}' was not found; declare a static {1}Property field or a [XamlAttachedProperty] accessor",
        XamlCategory, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidStyleTargetType = new(
        "VELOX_XAML014", "Invalid style TargetType",
        "{0}",
        XamlCategory, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InitializeComponentAlreadyDeclared = new(
        "VELOX_XAML015", "InitializeComponent already declared",
        "'{0}' already declares InitializeComponent; the XAML generator emits a private non-partial method and will not override an existing member",
        XamlCategory, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AmbiguousEventHandler = new(
        "VELOX_XAML016", "Ambiguous event handler",
        "The event handler '{0}' is ambiguous in '{1}'; rename it so only one method matches",
        XamlCategory, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidEventHandlerSignature = new(
        "VELOX_XAML017", "Invalid event handler signature",
        "The event handler '{0}' does not match the '{1}' event signature",
        XamlCategory, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AmbiguousType = new(
        "VELOX_XAML018", "Ambiguous XAML type",
        "The XAML type '{0}' is ambiguous: both '{1}' and '{2}' define it; use an explicit prefix (for example 'v:{0}' or 'local:{0}')",
        XamlCategory, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnprefixedTypeNotNative = new(
        "VELOX_XAML019", "Unprefixed type is not native",
        "The unprefixed type '{0}' is not a native WinForms type; write 'v:{0}' for a VeloxDev control or 'local:{0}' for a template/Demo type",
        XamlCategory, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PropertyNotPartial = new(
        "VELOX_XAMLP001", "XamlProperty must be partial",
        "A [XamlProperty] property must be declared partial and have no accessor bodies",
        PropertyCategory, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PropertyOwnerUnsupported = new(
        "VELOX_XAMLP002", "XamlProperty owner unsupported",
        "[XamlProperty] requires a partial type derived from VeloxDev.WinForms.Xaml.Controls.Control",
        PropertyCategory, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AttachedInvalid = new(
        "VELOX_XAMLP003", "Invalid attached property accessors",
        "{0}",
        PropertyCategory, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PropertyNameConflict = new(
        "VELOX_XAMLP004", "Dependency property name conflict",
        "'{0}' already declares a member named '{1}Property'",
        PropertyCategory, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PropertyDefaultValueRequired = new(
        "VELOX_XAMLP005", "Dependency property default value required",
        "The dependency property '{0}' is typed '{1}' (non-nullable reference); set DefaultValue or declare it nullable",
        PropertyCategory, DiagnosticSeverity.Error, isEnabledByDefault: true);
}
