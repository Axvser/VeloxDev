using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Test.Fixtures;

/// <summary>The code-behind of the command/input XAML smoke fixture.</summary>
public partial class CommandView : VeloxDev.WinForms.Xaml.Controls.Control
{
    /// <summary>Creates the view and runs the generated initializer.</summary>
    public CommandView()
    {
        InitializeComponent();
    }

    /// <summary>The button whose command comes from a binding.</summary>
    public VeloxDev.WinForms.Xaml.Controls.Button CommandButton => commandButton;

    /// <summary>The button carrying a key binding.</summary>
    public VeloxDev.WinForms.Xaml.Controls.Button KeyButton => keyButton;

    /// <summary>Runs the first input binding directly, avoiding a real keyboard focus dependency.</summary>
    public bool RunFirstInputBinding() => keyButton.InputBindings[0].TryExecute();

    /// <summary>The key code of the first input binding.</summary>
    public Keys FirstBindingKey => ((KeyBinding)keyButton.InputBindings[0]).Key;
}
