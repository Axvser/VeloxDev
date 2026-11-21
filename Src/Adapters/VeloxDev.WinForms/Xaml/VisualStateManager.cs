using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml;

/// <summary>One named visual state: the setters it applies and the actions it runs on entry and exit.</summary>
/// <remarks>
/// Setters are applied when the state becomes current and restored in reverse when another state in the same group
/// takes over. A setter whose value is a <see cref="Binding"/> is not evaluated by the state machine.
/// </remarks>
public sealed class VisualState
{
    /// <summary>The state name passed to <see cref="VisualStateManager.GoToState"/>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The setters applied while this state is current.</summary>
    public SetterCollection Setters { get; } = new();

    /// <summary>An action run after the state's setters are applied.</summary>
    public TriggerAction? EnterAction { get; set; }

    /// <summary>An action run before the state's setters are restored on the way out.</summary>
    public TriggerAction? ExitAction { get; set; }
}

/// <summary>An optional action run while moving between two states of one <see cref="VisualStateGroup"/>.</summary>
/// <remarks>
/// A transition action is normally a <see cref="TransitionAction"/> or <see cref="KeyFrameAnimation"/>, so it reuses
/// the control's existing animation pipeline instead of a separate timeline. <see cref="Duration"/> is metadata for
/// the caller; the action's own effect owns the actual timing.
/// </remarks>
public sealed class VisualTransition
{
    /// <summary>The state name this transition leaves, <see langword="null"/> or <c>*</c> for any.</summary>
    public string? From { get; set; }

    /// <summary>The state name this transition enters, <see langword="null"/> or <c>*</c> for any.</summary>
    public string? To { get; set; }

    /// <summary>The nominal transition duration; the action's effect controls the real timing.</summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(240);

    /// <summary>The action run when the transition is selected.</summary>
    public TriggerAction? Action { get; set; }
}

/// <summary>A mutually exclusive set of <see cref="VisualState"/> values for one control.</summary>
public sealed class VisualStateGroup
{
    /// <summary>The group name used by <see cref="VisualStateManager.GetCurrentState"/>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The states in this group; only one is current at a time.</summary>
    public IList<VisualState> States { get; } = new List<VisualState>();

    /// <summary>The transitions consulted when this group changes state.</summary>
    public IList<VisualTransition> Transitions { get; } = new List<VisualTransition>();
}

/// <summary>
/// Applies named visual states to a <see cref="VeloxDev.WinForms.Xaml.Controls.Control"/> through attached state groups.
/// </summary>
/// <remarks>
/// <para>
/// The state machine is deliberately small: one group owns one mutually exclusive set of states, a state owns
/// setters and enter/exit actions, and a transition owns an optional action. It does not template or restyle the
/// control; it writes dependency properties through the public control API.
/// </para>
/// <para>
/// State state is held per control in a weak table, so a control that is collected does not keep its groups alive.
/// </para>
/// </remarks>
public static class VisualStateManager
{
    /// <summary>The state groups attached to a control.</summary>
    public static readonly DependencyProperty VisualStateGroupsProperty = DependencyProperty.RegisterAttached(
        "VisualStateGroups",
        typeof(IList<VisualStateGroup>),
        typeof(VisualStateManager),
        new PropertyMetadata(null) { AffectsRender = false });

    private static readonly ConditionalWeakTable<VeloxDev.WinForms.Xaml.Controls.Control, ControlState> ControlStates = new();

    /// <summary>Reads the state groups attached to <paramref name="control"/>.</summary>
    /// <param name="control">The control to inspect.</param>
    public static IList<VisualStateGroup>? GetVisualStateGroups(VeloxDev.WinForms.Xaml.Controls.Control control)
    {
        if (control is null) throw new ArgumentNullException(nameof(control));
        return (IList<VisualStateGroup>?)VisualStateGroupsProperty.GetValue(control);
    }

    /// <summary>Writes the state groups attached to <paramref name="control"/>.</summary>
    /// <param name="control">The control to configure.</param>
    /// <param name="value">The groups, or <see langword="null"/> to remove them.</param>
    public static void SetVisualStateGroups(
        VeloxDev.WinForms.Xaml.Controls.Control control,
        IList<VisualStateGroup>? value)
    {
        if (control is null) throw new ArgumentNullException(nameof(control));
        VisualStateGroupsProperty.SetValue(control, value);
    }

    /// <summary>
    /// Activates <paramref name="stateName"/> in the group that declares it.
    /// </summary>
    /// <param name="control">The control whose state changes.</param>
    /// <param name="stateName">The requested state name.</param>
    /// <param name="useTransitions">Whether a matching <see cref="VisualTransition"/> action should run.</param>
    /// <returns><see langword="true"/> when the state was found and applied; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// The previous state in the same group runs its exit action and restores its setters before the new state applies
    /// its own. A state already current is a no-op that still returns <see langword="true"/>.
    /// </remarks>
    public static bool GoToState(
        VeloxDev.WinForms.Xaml.Controls.Control control,
        string stateName,
        bool useTransitions)
    {
        if (control is null) throw new ArgumentNullException(nameof(control));
        if (string.IsNullOrEmpty(stateName) || control.IsDisposed || control.Disposing)
        {
            return false;
        }

        var groups = GetVisualStateGroups(control);
        if (groups is null)
        {
            return false;
        }

        foreach (var group in groups)
        {
            if (group is null)
            {
                continue;
            }

            VisualState? target = null;
            foreach (var state in group.States)
            {
                if (state is not null && string.Equals(state.Name, stateName, StringComparison.Ordinal))
                {
                    target = state;
                    break;
                }
            }

            if (target is null)
            {
                continue;
            }

            var controlState = ControlStates.GetOrCreateValue(control);
            if (!controlState.Groups.TryGetValue(group, out var groupState))
            {
                groupState = new GroupState();
                controlState.Groups[group] = groupState;
            }

            if (ReferenceEquals(groupState.Current, target))
            {
                return true;
            }

            var previous = groupState.Current;
            if (previous is not null)
            {
                previous.ExitAction?.Invoke(control);
                RestoreSetters(control, groupState);
            }

            if (useTransitions)
            {
                FindTransition(group, previous?.Name, target.Name)?.Action?.Invoke(control);
            }

            ApplySetters(control, groupState, target);
            groupState.Current = target;
            target.EnterAction?.Invoke(control);
            return true;
        }

        return false;
    }

    /// <summary>Returns the current state name in <paramref name="groupName"/>, or <see langword="null"/>.</summary>
    /// <param name="control">The control to inspect.</param>
    /// <param name="groupName">The group to query.</param>
    public static string? GetCurrentState(VeloxDev.WinForms.Xaml.Controls.Control control, string groupName)
    {
        if (control is null) throw new ArgumentNullException(nameof(control));
        if (!ControlStates.TryGetValue(control, out var controlState))
        {
            return null;
        }

        foreach (var pair in controlState.Groups)
        {
            if (string.Equals(pair.Key.Name, groupName, StringComparison.Ordinal))
            {
                return pair.Value.Current?.Name;
            }
        }

        return null;
    }

    private static void ApplySetters(
        VeloxDev.WinForms.Xaml.Controls.Control control,
        GroupState groupState,
        VisualState state)
    {
        foreach (var setter in state.Setters)
        {
            if (setter is null || setter.Binding is not null || setter.Value is Binding)
            {
                continue;
            }

            if (setter.Property is { } property)
            {
                var hadLocal = control.HasLocalValue(property);
                var previous = property.GetValue(control);
                property.SetValue(control, setter.Value);
                groupState.Restore.Add(() =>
                {
                    if (hadLocal)
                    {
                        property.SetValue(control, previous);
                    }
                    else
                    {
                        property.ClearValue(control);
                    }
                });
                continue;
            }

            if (string.IsNullOrEmpty(setter.PropertyName))
            {
                continue;
            }

            var descriptor = TypeDescriptor.GetProperties(control).Find(setter.PropertyName!, ignoreCase: false);
            if (descriptor is null || descriptor.IsReadOnly)
            {
                continue;
            }

            var oldValue = descriptor.GetValue(control);
            descriptor.SetValue(control, XamlRuntime.ConvertValue(setter.Value, descriptor.PropertyType));
            groupState.Restore.Add(() => descriptor.SetValue(control, oldValue));
        }
    }

    private static void RestoreSetters(VeloxDev.WinForms.Xaml.Controls.Control control, GroupState groupState)
    {
        for (var index = groupState.Restore.Count - 1; index >= 0; index--)
        {
            groupState.Restore[index]();
        }

        groupState.Restore.Clear();
    }

    private static VisualTransition? FindTransition(VisualStateGroup group, string? from, string? to)
    {
        foreach (var transition in group.Transitions)
        {
            if (transition is null
                || !MatchesSide(transition.From, from)
                || !MatchesSide(transition.To, to))
            {
                continue;
            }

            return transition;
        }

        return null;
    }

    private static bool MatchesSide(string? pattern, string? stateName)
        => string.IsNullOrEmpty(pattern)
        || pattern == "*"
        || string.Equals(pattern, stateName, StringComparison.Ordinal);

    private sealed class ControlState
    {
        public Dictionary<VisualStateGroup, GroupState> Groups { get; } = [];
    }

    private sealed class GroupState
    {
        public VisualState? Current { get; set; }

        public List<Action> Restore { get; } = [];
    }
}
