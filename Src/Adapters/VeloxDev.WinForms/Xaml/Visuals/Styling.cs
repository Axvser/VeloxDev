using System.Collections.ObjectModel;

namespace VeloxDev.WinForms.Xaml.Visuals;

/// <summary>One dependency property and the value a <see cref="VisualStyle"/> or <see cref="VisualTrigger"/> writes.</summary>
public sealed class VisualSetter
{
    /// <summary>Creates an unconfigured setter.</summary>
    public VisualSetter()
    {
    }

    /// <summary>Creates a setter for <paramref name="property"/>.</summary>
    /// <param name="property">The dependency property to write.</param>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    public VisualSetter(DependencyProperty property)
    {
        Property = property ?? throw new ArgumentNullException(nameof(property));
    }

    /// <summary>Creates a setter for <paramref name="property"/> with <paramref name="value"/>.</summary>
    /// <param name="property">The dependency property to write.</param>
    /// <param name="value">The value to write.</param>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    public VisualSetter(DependencyProperty property, object? value)
        : this(property)
    {
        Value = value;
    }

    /// <summary>The dependency property to write, or <see langword="null"/> while unconfigured.</summary>
    public DependencyProperty? Property { get; set; }

    /// <summary>The value written while the owning style or trigger is active.</summary>
    public object? Value { get; set; }

    /// <summary>
    /// A binding that writes <see cref="Property"/> while the setter is active, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// A setter binding is attached when the style is applied and detached when it is removed. It writes the local
    /// layer, so an explicit local value or binding on the element still wins, exactly as a WPF setter binding loses to
    /// a local value.
    /// </remarks>
    public Binding? Binding { get; set; }

    /// <summary>
    /// The named template part this setter writes, or <see langword="null"/> for the styled element.
    /// </summary>
    /// <remarks>
    /// The name is resolved through the styled element's applied control template. When that template has no such part
    /// the setter is skipped for that element instead of writing the wrong node.
    /// </remarks>
    public string? TargetName { get; set; }
}

/// <summary>
/// A condition on one dependency property of a visual element. While the property equals <see cref="Value"/> the
/// trigger's <see cref="Setters"/> are applied; when it no longer does, the values are removed again.
/// </summary>
/// <remarks>
/// <para>
/// Trigger setters are written at <see cref="BindingPriority.StyleTrigger"/>, one step above
/// <see cref="BindingPriority.Style"/>: a local value still wins, and exiting the trigger reveals the style setter
/// that was underneath instead of the default value.
/// </para>
/// <para>
/// <see cref="EnterAction"/> runs once when the condition starts matching and <see cref="ExitAction"/> once when it
/// stops; neither runs again while the state is unchanged. In a template trigger both actions receive the same
/// target as the setters: <see cref="TargetName"/> when set, otherwise the templated control.
/// </para>
/// </remarks>
public sealed class VisualTrigger
{
    /// <summary>Creates an unconfigured trigger.</summary>
    public VisualTrigger()
    {
    }

    /// <summary>Creates a trigger that is active when <paramref name="property"/> equals <see langword="null"/>.</summary>
    /// <param name="property">The dependency property to observe.</param>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    public VisualTrigger(DependencyProperty property)
    {
        Property = property ?? throw new ArgumentNullException(nameof(property));
    }

    /// <summary>Creates a trigger that is active when <paramref name="property"/> equals <paramref name="value"/>.</summary>
    /// <param name="property">The dependency property to observe.</param>
    /// <param name="value">The value that activates the trigger.</param>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    public VisualTrigger(DependencyProperty property, object? value)
        : this(property)
    {
        Value = value;
    }

    /// <summary>The dependency property to observe, or <see langword="null"/> while unconfigured.</summary>
    public DependencyProperty? Property { get; set; }

    /// <summary>The value that activates the trigger.</summary>
    public object? Value { get; set; }

    /// <summary>The named template part the setters write to, or <see langword="null"/> for the templated control.</summary>
    /// <remarks>Only <see cref="VisualControlTemplate"/> triggers honour this name; style triggers always write the styled element.</remarks>
    public string? TargetName { get; set; }

    /// <summary>The setters applied while <see cref="Property"/> equals <see cref="Value"/>.</summary>
    public IList<VisualSetter> Setters { get; } = new Collection<VisualSetter>();

    /// <summary>The action run when the trigger becomes active, or <see langword="null"/>.</summary>
    public VisualTriggerAction? EnterAction { get; set; }

    /// <summary>The action run when the trigger becomes inactive, or <see langword="null"/>.</summary>
    public VisualTriggerAction? ExitAction { get; set; }

    internal bool Matches(IWinformsObject target)
        => Property is not null && Equals(target.GetValue(Property), Value);
}

/// <summary>A style for visual elements: a target type, setters, triggers and an optional base style.</summary>
/// <remarks>
/// <para>
/// Values go through the priority layers of <see cref="IWinformsObject"/>: setters write at
/// <see cref="BindingPriority.Style"/> and trigger setters at <see cref="BindingPriority.StyleTrigger"/>. A local
/// value therefore always wins, and removing the style or a trigger clears only its own layer, revealing whatever
/// was underneath.
/// </para>
/// <para>
/// <see cref="BasedOn"/> is applied first and this style last. A property written more than once in the chain is
/// written once, with the most derived value, so a shared base setter is never duplicated. Triggers run after the
/// setters in the same base-first order; for two active triggers on one property the later one wins.
/// </para>
/// <para>
/// A style is a description, not a live object: mutating its setters or triggers after it is applied does not
/// reapply it. Assign it again, or change the collection that produced the implicit style, to apply new contents.
/// </para>
/// </remarks>
public sealed class VisualStyle
{
    /// <summary>The element type this style matches, or <see langword="null"/> to match nothing.</summary>
    public Type? TargetType { get; set; }

    /// <summary>A base style whose setters and triggers are applied first, or <see langword="null"/>.</summary>
    public VisualStyle? BasedOn { get; set; }

    /// <summary>The setters applied while the style is active.</summary>
    public IList<VisualSetter> Setters { get; } = new Collection<VisualSetter>();

    /// <summary>The triggers evaluated while the style is active.</summary>
    public IList<VisualTrigger> Triggers { get; } = new Collection<VisualTrigger>();

    /// <summary>The control template created while the style is active, or <see langword="null"/>.</summary>
    /// <remarks>
    /// A <see cref="Controls.ContentControl"/> or <see cref="Controls.TemplatedControl"/> applies a style template;
    /// other elements ignore it. A template assigned directly to the control's <c>Template</c> wins over this one.
    /// </remarks>
    public VisualControlTemplate? Template { get; set; }

    /// <summary>Returns whether <see cref="TargetType"/> accepts <paramref name="target"/>.</summary>
    /// <param name="target">The object the style would be applied to.</param>
    /// <returns><see langword="false"/> when <see cref="TargetType"/> is <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    public bool Matches(IWinformsObject target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        return TargetType is not null && TargetType.IsInstanceOfType(target);
    }

    // BasedOn 链展开成 base-first 列表；环引用在重复处截断，恶意/手误的链不会把应用过程挂死。
    internal List<VisualStyle> Flatten()
    {
        var chain = new List<VisualStyle>();
        var visited = new HashSet<VisualStyle>();
        for (var current = this; current is not null; current = current.BasedOn)
        {
            if (!visited.Add(current))
            {
                break;
            }

            chain.Add(current);
        }

        chain.Reverse();
        return chain;
    }
}

/// <summary>The styles a surface or an element offers implicitly to matching elements in its subtree.</summary>
/// <remarks>
/// Declaration order is precedence: for one element the first style whose <see cref="VisualStyle.TargetType"/>
/// accepts it wins. A style with a <see langword="null"/> target type never matches implicitly.
/// </remarks>
public sealed class VisualStyleCollection : Collection<VisualStyle>
{
    /// <summary>Raised after an item is inserted, replaced, removed or cleared.</summary>
    public event EventHandler? Changed;

    /// <inheritdoc />
    protected override void InsertItem(int index, VisualStyle item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        base.InsertItem(index, item);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    protected override void SetItem(int index, VisualStyle item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        base.SetItem(index, item);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    protected override void RemoveItem(int index)
    {
        base.RemoveItem(index);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    protected override void ClearItems()
    {
        base.ClearItems();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal VisualStyle? FindMatch(IWinformsObject target)
    {
        foreach (var style in this)
        {
            if (style is not null && style.Matches(target))
            {
                return style;
            }
        }

        return null;
    }
}

// 一个元素上一次应用样式留下的状态：记录压进 Style / StyleTrigger 两个层的属性，卸载时才能只摘自己那几层。
internal sealed class VisualStyleApplication
{
    private readonly VisualElement _element;
    private readonly List<VisualTrigger> _triggers = [];
    private readonly HashSet<(IWinformsObject Target, DependencyProperty Property)> _styleProperties = [];
    private readonly HashSet<(IWinformsObject Target, DependencyProperty Property)> _triggerProperties = [];
    private readonly HashSet<VisualTrigger> _activeTriggers = [];
    private readonly List<IDisposable> _bindings = [];
    private readonly List<(IWinformsObject Target, DependencyProperty Property)> _bindingProperties = [];
    private VisualStyle? _style;
    private bool _subscribed;
    private bool _evaluating;

    internal VisualStyleApplication(VisualElement element)
    {
        _element = element;
    }

    internal void Apply(VisualStyle? style)
    {
        if (ReferenceEquals(_style, style))
        {
            return;
        }

        // 先退订再摘层：摘层会发 PropertyChanged，若还挂着触发器会被当成一次真实变更重新求值。
        Unsubscribe();
        ExitActiveTriggers();
        ClearBindings();
        ClearTriggerLayer();
        ClearStyleLayer();

        _style = style;
        _triggers.Clear();
        _triggerProperties.Clear();

        VisualControlTemplate? template = null;
        if (style is not null)
        {
            // 模板跟 setter 一样按 BasedOn 链 base-first 展开，派生样式最后声明的那一个生效。
            foreach (var link in style.Flatten())
            {
                if (link.Template is not null)
                {
                    template = link.Template;
                }
            }
        }

        _element.ApplyStyleTemplate(template);

        if (style is null)
        {
            return;
        }

        var values = new Dictionary<(IWinformsObject Target, DependencyProperty Property), object?>();
        var bindings = new List<(IWinformsObject Target, DependencyProperty Property, Binding Binding)>();
        foreach (var link in style.Flatten())
        {
            foreach (var setter in link.Setters)
            {
                // 用字典按“目标 + 属性”去重：BasedOn 链上同一个属性只写一次，derived 的值覆盖 base。
                if (setter?.Property is null)
                {
                    continue;
                }

                var target = ResolveTarget(setter.TargetName);
                if (target is null)
                {
                    continue;
                }

                if (setter.Binding is not null)
                {
                    bindings.Add((target, setter.Property, setter.Binding));
                }
                else
                {
                    values[(target, setter.Property)] = setter.Value;
                }
            }

            foreach (var trigger in link.Triggers)
            {
                if (trigger?.Property is null)
                {
                    continue;
                }

                _triggers.Add(trigger);
                foreach (var setter in trigger.Setters)
                {
                    if (setter?.Property is null)
                    {
                        continue;
                    }

                    // Setter.TargetName（WPF 形状）优先；旧的 Trigger.TargetName 作为兼容回落。
                    var target = ResolveTarget(setter.TargetName ?? trigger.TargetName);
                    if (target is not null)
                    {
                        _triggerProperties.Add((target, setter.Property));
                    }
                }
            }
        }

        foreach (var pair in values)
        {
            pair.Key.Property.SetValue(pair.Key.Target, pair.Value, BindingPriority.Style);
            _styleProperties.Add(pair.Key);
        }

        foreach (var (target, property, binding) in bindings)
        {
            _bindings.Add(VisualBindingOperations.Bind(target, property, binding));
            _bindingProperties.Add((target, property));
        }

        if (_triggers.Count > 0)
        {
            _element.PropertyChanged += OnElementPropertyChanged;
            _subscribed = true;
            EvaluateTriggers();
        }
    }

    // 命名部件从当前应用的控制模板里取；没有名字就写元素本身。
    private IWinformsObject? ResolveTarget(string? targetName)
        => string.IsNullOrEmpty(targetName)
            ? _element
            : _element.GetTemplateChild<VisualElement>(targetName);

    private void OnElementPropertyChanged(object? sender, DependencyPropertyChangedEventArgs e) => EvaluateTriggers();

    // 每次求值都按当前值整体重算触发器层：active 的最后一个写入者胜出，没写到的属性清层。
    // 这样触发器的退出不必知道当初是谁写的，也就不会误清另一个激活触发器的值。
    private void EvaluateTriggers()
    {
        if (_evaluating)
        {
            return;
        }

        _evaluating = true;
        List<VisualTrigger>? entered = null;
        List<VisualTrigger>? exited = null;
        try
        {
            Dictionary<(IWinformsObject Target, DependencyProperty Property), object?>? active = null;
            var nowActive = new HashSet<VisualTrigger>();
            foreach (var trigger in _triggers)
            {
                if (!trigger.Matches(_element))
                {
                    continue;
                }

                nowActive.Add(trigger);
                foreach (var setter in trigger.Setters)
                {
                    if (setter?.Property is null)
                    {
                        continue;
                    }

                    var target = ResolveTarget(setter.TargetName ?? trigger.TargetName);
                    if (target is null)
                    {
                        continue;
                    }

                    active ??= [];
                    active[(target, setter.Property)] = setter.Value;
                }
            }

            foreach (var pair in _triggerProperties)
            {
                if (active is not null && active.TryGetValue(pair, out var value))
                {
                    pair.Property.SetValue(pair.Target, value, BindingPriority.StyleTrigger);
                }
                else
                {
                    pair.Property.ClearValue(pair.Target, BindingPriority.StyleTrigger);
                }
            }

            foreach (var trigger in nowActive)
            {
                if (!_activeTriggers.Contains(trigger))
                {
                    (entered ??= []).Add(trigger);
                }
            }

            foreach (var trigger in _activeTriggers)
            {
                if (!nowActive.Contains(trigger))
                {
                    (exited ??= []).Add(trigger);
                }
            }

            _activeTriggers.Clear();
            _activeTriggers.UnionWith(nowActive);
        }
        finally
        {
            _evaluating = false;
        }

        // 动作放在求值标记之外：动作改属性时要能触发下一轮求值，而不是被当成重入吞掉。
        if (exited is not null)
        {
            foreach (var trigger in exited)
            {
                trigger.ExitAction?.Invoke(_element);
            }
        }

        if (entered is not null)
        {
            foreach (var trigger in entered)
            {
                trigger.EnterAction?.Invoke(_element);
            }
        }
    }

    // 卸载样式时把仍处于激活态的触发器补一次退出动作；集合先清空，动作里的属性变化不会再触发求值。
    private void ExitActiveTriggers()
    {
        if (_activeTriggers.Count == 0)
        {
            return;
        }

        var active = _activeTriggers.ToArray();
        _activeTriggers.Clear();
        foreach (var trigger in active)
        {
            trigger.ExitAction?.Invoke(_element);
        }
    }

    // 样式 setter 的绑定写在 Local 层；卸载时必须连绑定和它写过的本地值一起摘掉，否则值会留在元素上。
    private void ClearBindings()
    {
        foreach (var binding in _bindings)
        {
            binding.Dispose();
        }

        _bindings.Clear();
        foreach (var pair in _bindingProperties)
        {
            pair.Property.ClearValue(pair.Target);
        }

        _bindingProperties.Clear();
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
        {
            return;
        }

        _element.PropertyChanged -= OnElementPropertyChanged;
        _subscribed = false;
    }

    private void ClearStyleLayer()
    {
        foreach (var pair in _styleProperties)
        {
            pair.Property.ClearValue(pair.Target, BindingPriority.Style);
        }

        _styleProperties.Clear();
    }

    private void ClearTriggerLayer()
    {
        foreach (var pair in _triggerProperties)
        {
            pair.Property.ClearValue(pair.Target, BindingPriority.StyleTrigger);
        }
    }
}
