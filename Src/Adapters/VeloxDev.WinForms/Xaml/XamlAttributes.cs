namespace VeloxDev.WinForms.Xaml;

/// <summary>
/// Marks a partial property of a <see cref="VeloxDev.WinForms.Xaml.Controls.Control"/>-derived type for dependency-property generation.
/// </summary>
/// <remarks>
/// <para>
/// The source generator emits a static <c>{Name}Property</c> field and the implementing part of the partial
/// property. The declaration has no body:
/// </para>
/// <code>
/// [XamlProperty]
/// public partial string Title { get; set; }
/// </code>
/// <para>
/// C# 13 partial properties are used because they let the declaration stay normal C#: the generator fills in the
/// accessor bodies and no source rewriting is involved.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class XamlPropertyAttribute : Attribute
{
    /// <summary>Creates an attribute that uses the property name as the dependency-property name.</summary>
    public XamlPropertyAttribute()
    {
    }

    /// <summary>Creates an attribute with an explicit dependency-property name.</summary>
    /// <param name="name">The name of the generated <c>{name}Property</c> field.</param>
    public XamlPropertyAttribute(string name)
    {
        Name = name;
    }

    /// <summary>The dependency-property name, or <see langword="null"/> to use the CLR property name.</summary>
    public string? Name { get; }

    /// <summary>An optional default value, parsed against the property type by the generator.</summary>
    public string? DefaultValue { get; set; }
}

/// <summary>
/// Marks a pair of static partial attached-property accessors for dependency-property generation.
/// </summary>
/// <remarks>
/// <para>
/// The accessors are declared without bodies and the generator emits their implementing parts together with the
/// static <c>{Name}Property</c> field:
/// </para>
/// <code>
/// [XamlAttachedProperty]
/// public static partial string? GetTag(System.Windows.Forms.Control control);
/// public static partial void SetTag(System.Windows.Forms.Control control, string? value);
/// </code>
/// <para>
/// The attribute may be placed on either accessor; the pair is matched by the <c>Get</c>/<c>Set</c> name. The
/// property name may also be given explicitly when the accessors do not follow that convention.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class XamlAttachedPropertyAttribute : Attribute
{
    /// <summary>Creates an attribute that derives the property name from the accessor name.</summary>
    public XamlAttachedPropertyAttribute()
    {
    }

    /// <summary>Creates an attribute with an explicit property name.</summary>
    /// <param name="name">The attached property name.</param>
    public XamlAttachedPropertyAttribute(string name)
    {
        Name = name;
    }

    /// <summary>The attached-property name, or <see langword="null"/> to strip <c>Get</c>/<c>Set</c> from the accessor.</summary>
    public string? Name { get; }

    /// <summary>An optional default value, parsed against the property type by the generator.</summary>
    public string? DefaultValue { get; set; }
}
