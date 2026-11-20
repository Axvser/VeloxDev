namespace VeloxDev.WinForms.Xaml;

/// <summary>
/// A trigger that applies only when every <see cref="Condition"/> holds. Setters and enter/exit actions are reused
/// from <see cref="Trigger"/>.
/// </summary>
public sealed class MultiTrigger : Trigger
{
    /// <summary>Creates an empty multi-trigger.</summary>
    public MultiTrigger()
    {
        Conditions.Changed += OnConditionsChanged;
    }

    /// <summary>The conditions that must all hold.</summary>
    public ConditionCollection Conditions { get; } = [];

    internal override bool Matches(VeloxDev.WinForms.Xaml.Controls.Control control)
    {
        if (Conditions.Count == 0)
        {
            return false;
        }

        foreach (var condition in Conditions)
        {
            if (condition?.Property is null || !Equals(control.GetValue(condition.Property), condition.Value))
            {
                return false;
            }
        }

        return true;
    }

    private void OnConditionsChanged(object? sender, EventArgs e) => OnChanged();
}
