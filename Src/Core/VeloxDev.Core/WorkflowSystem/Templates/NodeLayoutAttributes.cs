namespace VeloxDev.WorkflowSystem;

/// <summary>
/// [ Generator ] Declares the code-level default <see cref="Anchor"/> of a Node component.
///
/// The source generator bakes the value into the generated backing-field initializer, so the
/// node carries a meaningful default position before any layout runs. The Agent reads this
/// default through ListNodes/GetNodeDetail instead of relying on a toolkit-side hard-coded
/// fallback, which keeps the reported default in sync with the component definition.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DefaultAnchorAttribute(double horizontal = 0d, double vertical = 0d, int layer = 0) : Attribute
{
    /// <summary>Horizontal coordinate, in pixels.</summary>
    public double Horizontal { get; } = horizontal;

    /// <summary>Vertical coordinate, in pixels.</summary>
    public double Vertical { get; } = vertical;

    /// <summary>Layer; behavior depends on the GUI.</summary>
    public int Layer { get; } = layer;
}

/// <summary>
/// [ Generator ] Declares the code-level default <see cref="Size"/> of a Node component.
///
/// The source generator bakes the value into the generated backing-field initializer, so a
/// node reports a non-zero size immediately after construction (even when created via
/// <see cref="System.Activator.CreateInstance(System.Type)"/>). This replaces the unreliable
/// Agent-side default-size constraint with a value owned by the component itself.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DefaultSizeAttribute(double width = 0d, double height = 0d) : Attribute
{
    /// <summary>Width, in pixels.</summary>
    public double Width { get; } = width;

    /// <summary>Height, in pixels.</summary>
    public double Height { get; } = height;
}
