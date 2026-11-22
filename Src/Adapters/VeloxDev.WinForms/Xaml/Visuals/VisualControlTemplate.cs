using System.Collections.ObjectModel;

namespace VeloxDev.WinForms.Xaml.Visuals;

/// <summary>One created template tree and the <see cref="VisualNameScope"/> that owns its named parts.</summary>
/// <param name="Root">The template's visual root.</param>
/// <param name="Scope">The scope holding the named parts of this instance.</param>
public readonly record struct VisualTemplateInstance(VisualElement Root, VisualNameScope Scope);

/// <summary>
/// A visual-backend control template: one factory that creates the content shown in place of a
/// <see cref="ContentControl"/>'s own children.
/// </summary>
/// <remarks>
/// <para>
/// The factory receives a fresh <see cref="VisualNameScope"/>; every element named with <c>x:Name</c> inside the
/// template registers itself there, and <see cref="ContentControl.GetTemplateChild{T}(string)"/> resolves those parts.
/// Every element of the created tree reports the applying control as its
/// <see cref="VisualElement.TemplatedParent"/>, so <c>{TemplateBinding}</c> has a source.
/// </para>
/// <para>
/// <see cref="Triggers"/> are evaluated against the templated control while the template is applied. A trigger with
/// <see cref="VisualTrigger.TargetName"/> writes to that named part; otherwise it writes to the templated control.
/// Template trigger values go to <see cref="BindingPriority.Template"/>, below style triggers and above style
/// setters.
/// </para>
/// </remarks>
public sealed class VisualControlTemplate
{
    private readonly Func<VisualNameScope, VisualElement> _factory;

    /// <summary>Creates a template over <paramref name="factory"/>.</summary>
    /// <param name="factory">Creates one instance of the template's content; the argument is its name scope.</param>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    public VisualControlTemplate(Func<VisualNameScope, VisualElement> factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>The control type the template expects, or <see langword="null"/> to accept any visual element.</summary>
    public Type? TargetType { get; set; }

    /// <summary>The triggers evaluated against the templated control while the template is applied.</summary>
    public IList<VisualTrigger> Triggers { get; } = new Collection<VisualTrigger>();

    /// <summary>Creates one instance of the template's content and points it at <paramref name="templatedParent"/>.</summary>
    /// <param name="templatedParent">The control whose style or <see cref="ContentControl.Template"/> applied this template.</param>
    /// <exception cref="ArgumentNullException"><paramref name="templatedParent"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><see cref="TargetType"/> does not accept <paramref name="templatedParent"/>.</exception>
    public VisualTemplateInstance CreateContent(VisualElement templatedParent)
    {
        if (templatedParent is null) throw new ArgumentNullException(nameof(templatedParent));
        if (TargetType is not null && !TargetType.IsInstanceOfType(templatedParent))
        {
            throw new InvalidOperationException(
                $"VisualControlTemplate TargetType '{TargetType}' does not apply to '{templatedParent.GetType()}'.");
        }

        var scope = new VisualNameScope();
        var root = _factory(scope);
        root.SetTemplatedParentRecursive(templatedParent);
        return new VisualTemplateInstance(root, scope);
    }
}

// 模板触发器与样式触发器是两条层：模板触发器写 Template 层，样式触发器退出时不会误清掉它写过的值。
internal sealed class VisualTemplateTriggerApplication : IDisposable
{
    private readonly IWinformsObject _owner;
    private readonly VisualNameScope _scope;
    private readonly IList<VisualTrigger> _triggers;
    private readonly List<(IWinformsObject Target, DependencyProperty Property)> _written = [];
    private bool _evaluating;
    private bool _disposed;

    internal VisualTemplateTriggerApplication(
        IWinformsObject owner,
        VisualNameScope scope,
        IList<VisualTrigger> triggers)
    {
        _owner = owner;
        _scope = scope;
        _triggers = triggers;

        if (owner is WinformsObject observable)
        {
            observable.PropertyChanged += OnOwnerPropertyChanged;
        }

        Evaluate();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_owner is WinformsObject observable)
        {
            observable.PropertyChanged -= OnOwnerPropertyChanged;
        }

        ClearWritten();
    }

    private void OnOwnerPropertyChanged(object? sender, DependencyPropertyChangedEventArgs e) => Evaluate();

    // 先算完整张表再落值：同一属性被两个激活触发器写时，后声明的赢；退出时只清本轮写过的。
    private void Evaluate()
    {
        if (_evaluating || _disposed)
        {
            return;
        }

        _evaluating = true;
        try
        {
            Dictionary<(IWinformsObject Target, DependencyProperty Property), object?>? active = null;
            foreach (var trigger in _triggers)
            {
                if (trigger?.Property is null || !trigger.Matches(_owner))
                {
                    continue;
                }

                var target = ResolveTarget(trigger) ?? _owner;
                foreach (var setter in trigger.Setters)
                {
                    if (setter?.Property is not null)
                    {
                        active ??= [];
                        active[(target, setter.Property)] = setter.Value;
                    }
                }
            }

            foreach (var pair in _written)
            {
                if (active is null || !active.ContainsKey(pair))
                {
                    pair.Property.ClearValue(pair.Target, BindingPriority.Template);
                }
            }

            _written.Clear();
            if (active is null)
            {
                return;
            }

            foreach (var pair in active)
            {
                pair.Key.Property.SetValue(pair.Key.Target, pair.Value, BindingPriority.Template);
                _written.Add(pair.Key);
            }
        }
        finally
        {
            _evaluating = false;
        }
    }

    private IWinformsObject? ResolveTarget(VisualTrigger trigger)
    {
        if (string.IsNullOrEmpty(trigger.TargetName))
        {
            return _owner;
        }

        return _scope.Find(trigger.TargetName) as IWinformsObject;
    }

    private void ClearWritten()
    {
        foreach (var pair in _written)
        {
            pair.Property.ClearValue(pair.Target, BindingPriority.Template);
        }

        _written.Clear();
    }
}
