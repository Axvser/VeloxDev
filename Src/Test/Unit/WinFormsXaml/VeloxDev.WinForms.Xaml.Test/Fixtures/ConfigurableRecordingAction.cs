namespace VeloxDev.WinForms.Xaml.Test.Fixtures;

/// <summary>A custom trigger action used by the XAML custom-action tests.</summary>
public sealed class ConfigurableRecordingAction : TriggerAction
{
    /// <summary>A string set from XAML.</summary>
    public string? Tag { get; set; }

    /// <summary>An integer set from XAML.</summary>
    public int Amount { get; set; }

    /// <summary>How many times <see cref="Invoke"/> ran.</summary>
    public int Invocations { get; private set; }

    /// <summary>The control the last invocation ran against.</summary>
    public VeloxDev.WinForms.Xaml.Controls.Control? LastControl { get; private set; }

    /// <inheritdoc />
    public override void Invoke(VeloxDev.WinForms.Xaml.Controls.Control control)
    {
        Invocations++;
        LastControl = control;
    }
}
