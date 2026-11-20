using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml;

/// <summary>
/// A focused style for <see cref="VeloxDev.WinForms.Xaml.Controls.Control"/> dependency properties and native <see cref="System.Windows.Forms.Control"/> properties:
/// a target type, setters, triggers, an optional content template and an optional base style.
/// </summary>
/// <remarks>
/// <para>
/// Styles participate in the value precedence <c>local &gt; style trigger &gt; style setter &gt; default</c>. A style
/// never overwrites a local value; clearing the local value reveals the style value again. Explicitly authored
/// setters on <see cref="VeloxDev.WinForms.Xaml.Controls.Control.Triggers"/> keep their pre-existing explicit-override behavior: a direct
/// trigger setter is written as a local value.
/// </para>
/// <para>
/// Native controls support style setters only (by native CLR property name); triggers and templates require a
/// <see cref="VeloxDev.WinForms.Xaml.Controls.Control"/>. A native style is applied explicitly through
/// <see cref="XamlRuntime.ApplyStyle(System.Windows.Forms.Control, Style?)"/>.
/// </para>
/// <para>
/// A style notifies the controls it is applied to when <see cref="Setters"/>, <see cref="Triggers"/>,
/// <see cref="Template"/>, <see cref="BasedOn"/> or <see cref="TargetType"/> change, so an applied style refreshes
/// in place. A <see cref="BasedOn"/> chain is resolved base-first; derived setters override base setters and derived
/// triggers win when both set the same property.
/// </para>
/// </remarks>
public sealed class Style
{
    private readonly List<WeakReference<System.Windows.Forms.Control>> _targets = [];
    private readonly HashSet<Trigger> _observedTriggers = [];
    private Type? _targetType;
    private Style? _basedOn;
    private ControlTemplate? _template;
    private bool _notifying;

    /// <summary>Creates an empty style.</summary>
    public Style()
    {
        Setters.Changed += OnSettersChanged;
        Triggers.Changed += OnTriggersChanged;
    }

    /// <summary>The type this style applies to; required when the style is applied.</summary>
    public Type? TargetType
    {
        get => _targetType;
        set
        {
            if (_targetType == value)
            {
                return;
            }

            _targetType = value;
            NotifyMutated();
        }
    }

    /// <summary>The setters applied while the style is active.</summary>
    public SetterCollection Setters { get; } = [];

    /// <summary>The triggers active while the style is active.</summary>
    public TriggerCollection Triggers { get; } = [];

    /// <summary>An optional content template created under the target control while the style is active.</summary>
    public ControlTemplate? Template
    {
        get => _template;
        set
        {
            if (ReferenceEquals(_template, value))
            {
                return;
            }

            _template = value;
            NotifyMutated();
        }
    }

    /// <summary>An optional base style whose setters, triggers and template come first.</summary>
    public Style? BasedOn
    {
        get => _basedOn;
        set
        {
            if (ReferenceEquals(_basedOn, value))
            {
                return;
            }

            _basedOn = value;
            NotifyMutated();
        }
    }

    /// <summary>Applies this style explicitly to <paramref name="control"/>.</summary>
    /// <param name="control">The control receiving the style.</param>
    /// <exception cref="ArgumentNullException"><paramref name="control"/> is <see langword="null"/>.</exception>
    public void Apply(VeloxDev.WinForms.Xaml.Controls.Control control)
    {
        if (control is null) throw new ArgumentNullException(nameof(control));
        control.Style = this;
    }

    /// <summary>Removes this style from <paramref name="control"/> when it is the explicit style currently applied.</summary>
    /// <param name="control">The control losing the style.</param>
    /// <exception cref="ArgumentNullException"><paramref name="control"/> is <see langword="null"/>.</exception>
    public void Unapply(VeloxDev.WinForms.Xaml.Controls.Control control)
    {
        if (control is null) throw new ArgumentNullException(nameof(control));
        if (ReferenceEquals(control.ExplicitStyle, this))
        {
            control.Style = null;
        }
    }

    internal void RegisterTarget(System.Windows.Forms.Control target)
    {
        for (var index = _targets.Count - 1; index >= 0; index--)
        {
            if (!_targets[index].TryGetTarget(out var existing))
            {
                _targets.RemoveAt(index);
                continue;
            }

            if (ReferenceEquals(existing, target))
            {
                return;
            }
        }

        _targets.Add(new WeakReference<System.Windows.Forms.Control>(target));
    }

    internal void UnregisterTarget(System.Windows.Forms.Control target)
    {
        for (var index = _targets.Count - 1; index >= 0; index--)
        {
            if (!_targets[index].TryGetTarget(out var existing) || ReferenceEquals(existing, target))
            {
                _targets.RemoveAt(index);
            }
        }
    }

    private void OnSettersChanged(object? sender, EventArgs e) => NotifyMutated();

    private void OnTriggersChanged(object? sender, EventArgs e)
    {
        SyncTriggerSubscriptions();
        NotifyMutated();
    }

    private void OnTriggerChanged(object? sender, EventArgs e) => NotifyMutated();

    private void SyncTriggerSubscriptions()
    {
        var current = new HashSet<Trigger>(Triggers);
        foreach (var observed in _observedTriggers.ToArray())
        {
            if (!current.Contains(observed))
            {
                observed.Changed -= OnTriggerChanged;
                _observedTriggers.Remove(observed);
            }
        }

        foreach (var trigger in current)
        {
            if (trigger is not null && _observedTriggers.Add(trigger))
            {
                trigger.Changed += OnTriggerChanged;
            }
        }
    }

    private void NotifyMutated()
    {
        if (_notifying || _targets.Count == 0)
        {
            return;
        }

        _notifying = true;
        try
        {
            var targets = new List<System.Windows.Forms.Control>();
            for (var index = _targets.Count - 1; index >= 0; index--)
            {
                if (_targets[index].TryGetTarget(out var target))
                {
                    targets.Add(target);
                }
                else
                {
                    _targets.RemoveAt(index);
                }
            }

            foreach (var target in targets)
            {
                if (target.IsDisposed || target.Disposing)
                {
                    continue;
                }

                if (target is VeloxDev.WinForms.Xaml.Controls.Control xaml)
                {
                    xaml.OnStyleMutated();
                }
                else
                {
                    XamlRuntime.ReapplyNativeStyle(target);
                }
            }
        }
        finally
        {
            _notifying = false;
        }
    }
}
