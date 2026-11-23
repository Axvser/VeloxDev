using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace VeloxDev.WinForms.Generator;

/// <summary>
/// Emits <see cref="DependencyProperty"/> fields and CLR wrappers for <c>[XamlProperty]</c> partial properties and
/// <c>[XamlAttachedProperty]</c> static partial accessors.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class DependencyPropertyGenerator : IIncrementalGenerator
{
    private const string PropertyAttributeMetadataName = "VeloxDev.WinForms.Xaml.XamlPropertyAttribute";
    private const string AttachedAttributeMetadataName = "VeloxDev.WinForms.Xaml.XamlAttachedPropertyAttribute";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var properties = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                PropertyAttributeMetadataName,
                static (node, _) => node is PropertyDeclarationSyntax,
                static (syntaxContext, _) => CreatePropertyModel(syntaxContext))
            .Where(static model => model is not null);

        context.RegisterSourceOutput(properties.Collect(), static (production, models) => EmitProperties(production, models));

        var attached = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttachedAttributeMetadataName,
                static (node, _) => node is MethodDeclarationSyntax,
                static (syntaxContext, _) => CreateAttachedModel(syntaxContext))
            .Where(static model => model is not null);

        context.RegisterSourceOutput(attached.Collect(), static (production, models) => EmitAttachedProperties(production, models));
    }

    private static PropertyModel? CreatePropertyModel(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not IPropertySymbol property
            || context.TargetNode is not PropertyDeclarationSyntax declaration
            || context.Attributes.Length == 0)
        {
            return null;
        }

        var attribute = context.Attributes[0];
        var owner = property.ContainingType;
        var location = declaration.Identifier.GetLocation();

        SyntaxHelpers.TryGetAttributeName(attribute, property.Name, out var dpName);
        var model = new PropertyModel(owner, property, dpName, location);

        if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword)
            || declaration.AccessorList is null
            || declaration.AccessorList.Accessors.Any(accessor => accessor.Body is not null || accessor.ExpressionBody is not null))
        {
            model.Diagnostic = Diagnostic.Create(Diagnostics.PropertyNotPartial, location);
            return model;
        }

        if (!SyntaxHelpers.IsPartial(owner))
        {
            model.Diagnostic = Diagnostic.Create(Diagnostics.PropertyNotPartial, location);
            return model;
        }

        if (property.IsStatic || !SyntaxHelpers.DerivesFrom(owner, SyntaxHelpers.VisualElementMetadataName))
        {
            model.Diagnostic = Diagnostic.Create(Diagnostics.PropertyOwnerUnsupported, location);
            return model;
        }

        if (property.GetMethod is null || property.SetMethod is null)
        {
            model.Diagnostic = Diagnostic.Create(Diagnostics.PropertyNotPartial, location);
            return model;
        }

        if (owner.GetMembers(dpName + "Property").Length > 0)
        {
            model.Diagnostic = Diagnostic.Create(Diagnostics.PropertyNameConflict, location, owner.Name, dpName);
            return model;
        }

        var defaultValue = SyntaxHelpers.GetNamedString(attribute, "DefaultValue");
        model.MetadataOptions = MetadataOptions(attribute);

        // 没有 DefaultValue 时 default(string) 是 null，而生成的 getter 会把 null 断言成非空返回值 ——
        // 调用方在 setter 之前读一次就是 NRE。非可空引用类型必须显式给默认值（或把属性声明成可空）。
        if (defaultValue is null
            && property.Type.IsReferenceType
            && property.Type.NullableAnnotation == NullableAnnotation.NotAnnotated)
        {
            model.Diagnostic = Diagnostic.Create(
                Diagnostics.PropertyDefaultValueRequired,
                location,
                dpName,
                property.Type.ToDisplayString());
            return model;
        }

        if (!LiteralFactory.TryCreate(property.Type, defaultValue, out var expression, out _))
        {
            model.Warning = Diagnostic.Create(
                Diagnostics.UnsupportedValue,
                location,
                defaultValue ?? string.Empty,
                property.Type.ToDisplayString());
            expression = $"default({SyntaxHelpers.TypeName(property.Type)})";
        }

        model.DefaultExpression = expression;
        model.CanEmit = true;
        return model;
    }

    private static void EmitProperties(SourceProductionContext context, ImmutableArray<PropertyModel?> models)
    {
        foreach (var model in models)
        {
            if (model is null)
            {
                continue;
            }

            if (model.Diagnostic is not null)
            {
                context.ReportDiagnostic(model.Diagnostic);
                continue;
            }

            if (model.Warning is not null)
            {
                context.ReportDiagnostic(model.Warning);
            }

            if (!model.CanEmit)
            {
                continue;
            }

            var members = new CodeBuilder();
            var typeName = SyntaxHelpers.TypeName(model.Property.Type);

            members.Line($"/// <summary>The backing dependency property for <see cref=\"{model.Property.Name}\"/>.</summary>");
            members.Line($"public static readonly {SyntaxHelpers.Fqn("DependencyProperty")} {model.DpName}Property =");
            members.Line($"    {SyntaxHelpers.Fqn("DependencyProperty")}.Register(");
            members.Line($"        {SyntaxHelpers.Quote(model.DpName)},");
            members.Line($"        typeof({SyntaxHelpers.TypeNameForTypeof(model.Property.Type)}),");
            members.Line($"        typeof({SyntaxHelpers.TypeName(model.Owner)}),");
            members.Line($"        new {SyntaxHelpers.Fqn("PropertyMetadata")}({model.DefaultExpression}){model.MetadataOptions});");
            members.Line();

            members.Line("/// <summary>The CLR wrapper over the generated dependency property.</summary>");
            members.Line("[global::System.ComponentModel.DesignerSerializationVisibility(global::System.ComponentModel.DesignerSerializationVisibility.Hidden)]");
            members.Open($"{SyntaxHelpers.AccessibilityText(model.Property.DeclaredAccessibility)} partial {typeName} {model.Property.Name}");
            members.Line($"get => ({typeName})GetValue({model.DpName}Property)!;");
            members.Line($"set => SetValue({model.DpName}Property, value);");
            members.Close();
            var code = Header + SyntaxHelpers.WrapInPartialType(model.Owner, members.ToString());
            var hint = $"XamlProperty.{SyntaxHelpers.SanitizeIdentifier(model.Owner.ToDisplayString())}.{model.Property.Name}.g.cs";
            context.AddSource(hint, SourceText.From(code, Encoding.UTF8));
        }
    }

    private static AttachedModel? CreateAttachedModel(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not IMethodSymbol method
            || context.TargetNode is not MethodDeclarationSyntax declaration
            || context.Attributes.Length == 0)
        {
            return null;
        }

        var attribute = context.Attributes[0];
        var owner = method.ContainingType;
        var location = declaration.Identifier.GetLocation();
        var isGetter = !method.ReturnsVoid;

        var inferred = method.Name.Length > 3 && (method.Name.StartsWith("Get") || method.Name.StartsWith("Set"))
            ? method.Name.Substring(3)
            : method.Name;

        SyntaxHelpers.TryGetAttributeName(attribute, inferred, out var propertyName);
        var model = new AttachedModel(owner, method, propertyName, isGetter, location)
        {
            DefaultValue = SyntaxHelpers.GetNamedString(attribute, "DefaultValue"),
            MetadataOptions = MetadataOptions(attribute),
        };

        if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword) || !method.IsStatic)
        {
            model.Diagnostic = Diagnostic.Create(
                Diagnostics.AttachedInvalid,
                location,
                "An attached property accessor must be a static partial method.");
            return model;
        }

        if (!SyntaxHelpers.IsPartial(owner))
        {
            model.Diagnostic = Diagnostic.Create(
                Diagnostics.AttachedInvalid,
                location,
                "The type declaring an attached property accessor must be partial.");
            return model;
        }

        if (string.IsNullOrEmpty(propertyName))
        {
            model.Diagnostic = Diagnostic.Create(
                Diagnostics.AttachedInvalid,
                location,
                "The attached property name could not be derived from the accessor name.");
            return model;
        }

        if (isGetter)
        {
            if (method.Parameters.Length != 1
                || !SyntaxHelpers.DerivesFrom(method.Parameters[0].Type, SyntaxHelpers.VisualElementMetadataName))
            {
                model.Diagnostic = Diagnostic.Create(
                    Diagnostics.AttachedInvalid,
                    location,
                    "An attached property getter must take one VisualElement parameter and return the property value.");
                return model;
            }
        }
        else if (method.Parameters.Length != 2
            || !SyntaxHelpers.DerivesFrom(method.Parameters[0].Type, SyntaxHelpers.VisualElementMetadataName))
        {
            model.Diagnostic = Diagnostic.Create(
                Diagnostics.AttachedInvalid,
                location,
                "An attached property setter must take a VisualElement and a value, and return void.");
            return model;
        }

        if (owner.GetMembers(propertyName + "Property").Length > 0)
        {
            model.Diagnostic = Diagnostic.Create(Diagnostics.PropertyNameConflict, location, owner.Name, propertyName);
            return model;
        }

        model.CanEmit = true;
        return model;
    }

    private static void EmitAttachedProperties(SourceProductionContext context, ImmutableArray<AttachedModel?> models)
    {
        var groups = new Dictionary<string, List<AttachedModel>>(StringComparer.Ordinal);

        foreach (var model in models)
        {
            if (model is null)
            {
                continue;
            }

            if (model.Diagnostic is not null)
            {
                context.ReportDiagnostic(model.Diagnostic);
                continue;
            }

            if (!model.CanEmit)
            {
                continue;
            }

            var key = model.Owner.ToDisplayString() + "|" + model.PropertyName;
            if (!groups.TryGetValue(key, out var group))
            {
                group = [];
                groups.Add(key, group);
            }

            group.Add(model);
        }

        foreach (var group in groups.Values)
        {
            var owner = group[0].Owner;
            var members = new CodeBuilder();
            var byName = new Dictionary<string, (AttachedModel? Getter, AttachedModel? Setter)>(StringComparer.Ordinal);

            foreach (var model in group)
            {
                if (!byName.TryGetValue(model.PropertyName, out var pair))
                {
                    pair = (null, null);
                }

                if (model.IsGetter)
                {
                    if (pair.Getter is not null)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.AttachedInvalid,
                            model.Location,
                            $"Duplicate attached property getter for '{model.PropertyName}'."));
                        continue;
                    }

                    pair.Getter = model;
                }
                else
                {
                    if (pair.Setter is not null)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.AttachedInvalid,
                            model.Location,
                            $"Duplicate attached property setter for '{model.PropertyName}'."));
                        continue;
                    }

                    pair.Setter = model;
                }

                byName[model.PropertyName] = pair;
            }

            foreach (var pair in byName.Values)
            {
                var getter = pair.Getter;
                var setter = pair.Setter;
                if (getter is null && setter is null)
                {
                    continue;
                }

                var propertyType = getter?.Method.ReturnType ?? setter!.Method.Parameters[1].Type;
                var dpName = getter?.PropertyName ?? setter!.PropertyName;
                var defaultValue = getter?.DefaultValue ?? setter!.DefaultValue;
                var metadataOptions = getter?.MetadataOptions ?? setter!.MetadataOptions;
                var location = getter?.Location ?? setter!.Location;

                // 同 [XamlProperty]：没有默认值时的 null 会被 getter 的 ! 伪装成非空返回值。
                if (defaultValue is null
                    && propertyType.IsReferenceType
                    && propertyType.NullableAnnotation == NullableAnnotation.NotAnnotated)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.PropertyDefaultValueRequired,
                        location,
                        dpName,
                        propertyType.ToDisplayString()));
                    continue;
                }

                if (!LiteralFactory.TryCreate(propertyType, defaultValue, out var defaultExpression, out _))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.UnsupportedValue,
                        location,
                        defaultValue ?? string.Empty,
                        propertyType.ToDisplayString()));
                    defaultExpression = $"default({SyntaxHelpers.TypeName(propertyType)})";
                }

                members.Line($"/// <summary>The backing attached dependency property for <c>{dpName}</c>.</summary>");
                members.Line($"public static readonly {SyntaxHelpers.Fqn("DependencyProperty")} {dpName}Property =");
                members.Line($"    {SyntaxHelpers.Fqn("DependencyProperty")}.RegisterAttached(");
                members.Line($"        {SyntaxHelpers.Quote(dpName)},");
                members.Line($"        typeof({SyntaxHelpers.TypeNameForTypeof(propertyType)}),");
                members.Line($"        typeof({SyntaxHelpers.TypeName(owner)}),");
                members.Line($"        new {SyntaxHelpers.Fqn("PropertyMetadata")}({defaultExpression}){metadataOptions});");
                members.Line();

                if (getter is not null)
                {
                    var parameter = getter.Method.Parameters[0];
                    members.Line(
                        $"{SyntaxHelpers.AccessibilityText(getter.Method.DeclaredAccessibility)} static partial "
                        + $"{SyntaxHelpers.TypeName(getter.Method.ReturnType)} {getter.Method.Name}"
                        + $"({SyntaxHelpers.TypeName(parameter.Type)} {parameter.Name})");
                    members.Line(
                        $"    => ({SyntaxHelpers.TypeName(getter.Method.ReturnType)}){dpName}Property.GetValue({parameter.Name})!;");
                    members.Line();
                }

                if (setter is not null)
                {
                    var target = setter.Method.Parameters[0];
                    var value = setter.Method.Parameters[1];
                    members.Line(
                        $"{SyntaxHelpers.AccessibilityText(setter.Method.DeclaredAccessibility)} static partial void {setter.Method.Name}"
                        + $"({SyntaxHelpers.TypeName(target.Type)} {target.Name}, {SyntaxHelpers.TypeName(value.Type)} {value.Name})");
                    members.Line($"    => {dpName}Property.SetValue({target.Name}, {value.Name});");
                    members.Line();
                }
            }

            var code = Header + SyntaxHelpers.WrapInPartialType(owner, members.ToString());
            var hint = $"XamlAttached.{SyntaxHelpers.SanitizeIdentifier(owner.ToDisplayString())}.g.cs";
            context.AddSource(hint, SourceText.From(code, Encoding.UTF8));
        }
    }

    // [XamlProperty]/[XamlAttachedProperty] 上的布局标志映射到 PropertyMetadata；默认值不重复写。
    private static string MetadataOptions(AttributeData attribute)
    {
        var parts = new List<string>();
        foreach (var argument in attribute.NamedArguments)
        {
            var enabled = argument.Value.Value is true;
            switch (argument.Key)
            {
                case "AffectsMeasure" when enabled:
                    parts.Add("AffectsMeasure = true");
                    break;
                case "AffectsArrange" when enabled:
                    parts.Add("AffectsArrange = true");
                    break;
                case "AffectsRender" when argument.Value.Value is false:
                    parts.Add("AffectsRender = false");
                    break;
                case "Inherits" when enabled:
                    parts.Add("Inherits = true");
                    break;
            }
        }

        return parts.Count == 0 ? string.Empty : " { " + string.Join(", ", parts) + " }";
    }

    private const string Header = "// <auto-generated/>\n#nullable enable\n";

    private sealed class PropertyModel
    {
        public PropertyModel(INamedTypeSymbol owner, IPropertySymbol property, string dpName, Location location)
        {
            Owner = owner;
            Property = property;
            DpName = dpName;
            Location = location;
        }

        public INamedTypeSymbol Owner { get; }
        public IPropertySymbol Property { get; }
        public string DpName { get; }
        public string DefaultExpression { get; set; } = "null";
        public string MetadataOptions { get; set; } = string.Empty;
        public Location Location { get; }
        public Diagnostic? Diagnostic { get; set; }
        public Diagnostic? Warning { get; set; }
        public bool CanEmit { get; set; }
    }

    private sealed class AttachedModel
    {
        public AttachedModel(INamedTypeSymbol owner, IMethodSymbol method, string propertyName, bool isGetter, Location location)
        {
            Owner = owner;
            Method = method;
            PropertyName = propertyName;
            IsGetter = isGetter;
            Location = location;
        }

        public INamedTypeSymbol Owner { get; }
        public IMethodSymbol Method { get; }
        public string PropertyName { get; }
        public bool IsGetter { get; }
        public string? DefaultValue { get; set; }
        public string MetadataOptions { get; set; } = string.Empty;
        public Location Location { get; }
        public Diagnostic? Diagnostic { get; set; }
        public bool CanEmit { get; set; }
    }
}
