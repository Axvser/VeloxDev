namespace VeloxDev.WinForms.Xaml;

/// <summary>
/// The name registry behind <c>x:Name</c> lookup, attached to a XAML root or to one template instance.
/// </summary>
/// <remarks>
/// <para>
/// A lookup walks from a control up to the nearest control carrying a scope, so a name inside a
/// <see cref="DataTemplate"/> resolves against the template's own scope rather than the outer view.
/// </para>
/// <para>
/// Names are registered while generated code constructs the tree. A binding that asks for a name before it is
/// registered subscribes to <see cref="Registered"/> and retries; that keeps <c>ElementName</c> independent of the
/// declaration order in the markup.
/// </para>
/// </remarks>
public sealed class NameScope
{
    private readonly Dictionary<string, System.Windows.Forms.Control> _names = new(StringComparer.Ordinal);

    /// <summary>Raised after a name is registered or replaced.</summary>
    public event EventHandler? Registered;

    /// <summary>Registers <paramref name="control"/> under <paramref name="name"/>.</summary>
    /// <param name="name">The name written in <c>x:Name</c>.</param>
    /// <param name="control">The control the name refers to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="control"/> is <see langword="null"/>.</exception>
    public void Register(string name, System.Windows.Forms.Control control)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        if (control is null) throw new ArgumentNullException(nameof(control));

        if (_names.TryGetValue(name, out var existing) && ReferenceEquals(existing, control))
        {
            return;
        }

        _names[name] = control;
        Registered?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes <paramref name="control"/> from <paramref name="name"/> when it is still registered there.</summary>
    /// <param name="name">The registered name.</param>
    /// <param name="control">The control to remove.</param>
    public void Unregister(string name, System.Windows.Forms.Control control)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        if (control is null) throw new ArgumentNullException(nameof(control));

        if (_names.TryGetValue(name, out var existing) && ReferenceEquals(existing, control))
        {
            _names.Remove(name);
        }
    }

    /// <summary>Finds the control registered under <paramref name="name"/>.</summary>
    /// <param name="name">The registered name.</param>
    /// <returns>The control, or <see langword="null"/> when the name is unknown.</returns>
    public System.Windows.Forms.Control? Find(string name)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        return _names.TryGetValue(name, out var control) ? control : null;
    }

    /// <summary>Finds the control registered under <paramref name="name"/>.</summary>
    /// <param name="name">The registered name.</param>
    /// <param name="control">The control when this returns <see langword="true"/>.</param>
    public bool TryFind(string name, out System.Windows.Forms.Control? control)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        return _names.TryGetValue(name, out control);
    }

    /// <summary>True when <paramref name="name"/> is registered.</summary>
    /// <param name="name">The registered name.</param>
    public bool Contains(string name)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        return _names.ContainsKey(name);
    }

    /// <summary>Finds the nearest scope on <paramref name="element"/> or one of its ancestors.</summary>
    /// <param name="element">The control to start from.</param>
    public static NameScope? Find(System.Windows.Forms.Control? element)
    {
        for (var current = element; current is not null; current = current.Parent)
        {
            if (current is Controls.Control xaml && xaml.NameScope is { } scope)
            {
                return scope;
            }
        }

        return null;
    }
}
