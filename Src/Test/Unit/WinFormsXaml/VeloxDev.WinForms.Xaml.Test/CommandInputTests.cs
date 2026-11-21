using System.Windows.Forms;
using System.Windows.Input;
using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Fixtures;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The command, input-binding and focus primitives: <see cref="RelayCommand"/>, button commands,
/// <see cref="KeyBinding"/> / <see cref="MouseBinding"/> matching and <see cref="FocusManager"/>.
/// </summary>
[STATestClass]
public sealed class CommandInputTests
{
    [TestMethod]
    public void RelayCommand_ExecutesWithTheParameterAndQueriesCanExecute()
    {
        object? received = null;
        var canExecuteCalls = 0;
        var command = new RelayCommand(
            parameter => received = parameter,
            _ =>
            {
                canExecuteCalls++;
                return true;
            });

        Assert.IsTrue(command.CanExecute(1));
        command.Execute(2);

        Assert.AreEqual(2, received);
        Assert.AreEqual(1, canExecuteCalls);
    }

    [TestMethod]
    public void RelayCommand_WithoutAPredicate_IsAlwaysExecutable()
    {
        var runs = 0;
        var command = new RelayCommand(() => runs++);

        Assert.IsTrue(command.CanExecute(null));

        command.Execute(null);
        Assert.AreEqual(1, runs);
    }

    [TestMethod]
    public void RelayCommand_RaiseCanExecuteChanged_RaisesTheEvent()
    {
        var command = new RelayCommand(() => { });
        var raised = 0;
        command.CanExecuteChanged += (_, _) => raised++;

        command.RaiseCanExecuteChanged();

        Assert.AreEqual(1, raised);
    }

    [TestMethod]
    public void KeyBinding_MatchesOnlyTheExactKeyAndModifierSet()
    {
        var binding = new KeyBinding
        {
            Gesture = new KeyGesture(Keys.S, ModifierKeys.Control),
            Command = new RelayCommand(_ => { }),
        };

        Assert.IsTrue(binding.MatchesKey(Keys.S, Keys.Control));
        Assert.IsFalse(binding.MatchesKey(Keys.S, Keys.None), "a plain S must not match Ctrl+S");
        Assert.IsFalse(binding.MatchesKey(Keys.S, Keys.Control | Keys.Shift), "an extra modifier must not match");
        Assert.IsFalse(binding.MatchesKey(Keys.A, Keys.Control));
        Assert.AreEqual("Control+S", binding.Gesture!.ToString());
    }

    [TestMethod]
    public void KeyBinding_KeyAndModifiersCreateTheGestureOnDemand()
    {
        var binding = new KeyBinding { Key = Keys.F5 };

        Assert.IsNotNull(binding.Gesture);
        Assert.IsTrue(binding.MatchesKey(Keys.F5, Keys.None));
        Assert.IsFalse(binding.MatchesKey(Keys.F5, Keys.Control));

        binding.Modifiers = ModifierKeys.Control;
        Assert.IsTrue(binding.MatchesKey(Keys.F5, Keys.Control));
        Assert.IsFalse(binding.MatchesKey(Keys.F5, Keys.None));
    }

    [TestMethod]
    public void MouseBinding_MatchesClicksAndDoubleClicks()
    {
        var click = new MouseBinding { Action = MouseAction.LeftClick };
        Assert.IsTrue(click.MatchesMouse(MouseButtons.Left, 1, Keys.None));
        Assert.IsFalse(click.MatchesMouse(MouseButtons.Right, 1, Keys.None));

        var doubleClick = new MouseBinding { Action = MouseAction.LeftDoubleClick };
        Assert.IsFalse(doubleClick.MatchesMouse(MouseButtons.Left, 1, Keys.None));
        Assert.IsTrue(doubleClick.MatchesMouse(MouseButtons.Left, 2, Keys.None));

        var wheel = new MouseBinding { Action = MouseAction.WheelClick };
        Assert.IsTrue(wheel.MatchesMouse(MouseButtons.Middle, 1, Keys.None), "a wheel press is the middle button");

        var modified = new MouseBinding { Action = MouseAction.LeftClick, Modifiers = ModifierKeys.Control };
        Assert.IsFalse(modified.MatchesMouse(MouseButtons.Left, 1, Keys.None));
        Assert.IsTrue(modified.MatchesMouse(MouseButtons.Left, 1, Keys.Control));
    }

    [TestMethod]
    public void InputBindingCollection_SkipsABindingWhoseCommandCannotExecute()
    {
        var first = 0;
        var second = 0;
        var bindings = new InputBindingCollection
        {
            new KeyBinding { Key = Keys.F5, Command = new RelayCommand(_ => first++, _ => false) },
            new KeyBinding { Key = Keys.F5, Command = new RelayCommand(_ => second++) },
        };

        Assert.IsTrue(bindings.TryHandleKey(Keys.F5, Keys.None));
        Assert.AreEqual(0, first);
        Assert.AreEqual(1, second);
    }

    [TestMethod]
    public void InputBindings_ExecuteOnKeyAndMouseDown()
    {
        var executed = 0;
        var command = new RelayCommand(_ => executed++);
        using var control = new InputProbeControl();
        control.InputBindings.Add(new KeyBinding { Key = Keys.S, Modifiers = ModifierKeys.Control, Command = command });
        control.InputBindings.Add(new MouseBinding { Action = MouseAction.LeftClick, Command = command });

        var key = new KeyEventArgs(Keys.Control | Keys.S);
        control.SimulateKeyDown(key);

        Assert.AreEqual(1, executed);
        Assert.IsTrue(key.Handled, "a matched key binding marks the key event handled");

        control.SimulateKeyDown(new KeyEventArgs(Keys.S));
        Assert.AreEqual(1, executed, "a plain S must not execute the Ctrl+S binding");

        control.SimulateMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
        Assert.AreEqual(2, executed);
    }

    [TestMethod]
    public void ButtonCommand_ExecutesOnClickWithTheParameter()
    {
        object? received = null;
        var command = new RelayCommand(parameter => received = parameter);
        using var button = new VeloxDev.WinForms.Xaml.Controls.Button
        {
            Command = command,
            CommandParameter = "save",
        };

        button.PerformClick();

        Assert.AreEqual("save", received);
    }

    [TestMethod]
    public void ButtonCommand_DrivesIsEnabledFromCanExecute()
    {
        var allowed = false;
        var command = new RelayCommand(_ => { }, _ => allowed);
        using var button = new VeloxDev.WinForms.Xaml.Controls.Button { Command = command };

        Assert.IsFalse(button.IsEnabled, "a command that cannot execute disables the button");
        Assert.IsFalse(button.Enabled);

        allowed = true;
        command.RaiseCanExecuteChanged();

        Assert.IsTrue(button.IsEnabled);
        Assert.IsTrue(button.Enabled);
    }

    [TestMethod]
    public void ToggleButtonCommand_RunsAfterTheCheckedStateChanges()
    {
        bool? checkedWhenExecuted = null;
        VeloxDev.WinForms.Xaml.Controls.ToggleButton? toggle = null;
        var command = new RelayCommand(() => checkedWhenExecuted = toggle!.IsChecked);
        toggle = new VeloxDev.WinForms.Xaml.Controls.ToggleButton { Command = command };
        using var disposable = toggle;

        toggle.PerformClick();

        Assert.AreEqual(true, toggle.IsChecked);
        Assert.AreEqual(true, checkedWhenExecuted);
    }

    [TestMethod]
    public void TabIndexAndIsTabStop_AreDependencyPropertiesThatSyncToTheNativeControl()
    {
        Assert.AreEqual(-1, (int)VeloxDev.WinForms.Xaml.Controls.Control.TabIndexProperty.DefaultValue!);
        Assert.AreEqual(true, (bool)VeloxDev.WinForms.Xaml.Controls.Control.IsTabStopProperty.DefaultValue!);

        using var control = new InputProbeControl();
        var native = (System.Windows.Forms.Control)control;

        control.TabIndex = 4;
        Assert.AreEqual(4, control.TabIndex);
        Assert.AreEqual(4, native.TabIndex, "the dependency property writes the native tab index");

        control.IsTabStop = false;
        Assert.IsFalse(control.IsTabStop);
        Assert.IsFalse(native.TabStop, "the dependency property writes the native tab stop");
    }

    [TestMethod]
    public void FocusManager_TracksFocusAndScopes()
    {
        using var scope = new InputProbeControl();
        using var child = new InputProbeControl();
        scope.Controls.Add(child);
        FocusManager.SetIsFocusScope(scope, true);

        child.SimulateGotFocus();

        Assert.AreSame(child, FocusManager.FocusedElement);
        Assert.AreSame(child, FocusManager.GetFocusedElement());
        Assert.AreSame(child, FocusManager.GetFocusedElement(scope));
        Assert.AreSame(scope, FocusManager.GetFocusScope(child));
        Assert.IsTrue(FocusManager.GetIsFocusScope(scope));

        child.SimulateLostFocus();

        Assert.IsNull(FocusManager.FocusedElement);
        Assert.IsNull(FocusManager.GetFocusedElement(scope));
    }

    [TestMethod]
    public void FocusManager_RejectsANonFocusableElement()
    {
        using var control = new InputProbeControl { Focusable = false };

        Assert.IsFalse(FocusManager.SetFocusedElement(control));
        Assert.IsFalse(FocusManager.FocusFirst(control), "a scope with no tab stop cannot take focus");
    }

    [TestMethod]
    public void XamlCommandAndInputBindings_CompileThroughTheGenerator()
    {
        const string source = """
            using System.Windows.Input;
            using VeloxDev.WinForms.Xaml;

            namespace Probe;

            public static class Commands
            {
                public static readonly ICommand Save = new RelayCommand(() => { });
                public static readonly ICommand Open = new RelayCommand(() => { });
            }

            public partial class ProbeView : VeloxDev.WinForms.Xaml.Controls.Control
            {
            }
            """;

        const string xaml = """
            <local:ProbeView
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:Probe"
                xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml"
                x:Class="Probe.ProbeView">
              <v:Button x:Name="save" Command="{Binding SaveCommand}" CommandParameter="row">
                <v:Button.InputBindings>
                  <v:KeyBinding Key="S" Modifiers="Control" Command="{x:Static local:Commands.Save}" />
                  <v:MouseBinding Action="LeftDoubleClick" Command="{x:Static local:Commands.Open}" />
                </v:Button.InputBindings>
              </v:Button>
            </local:ProbeView>
            """;

        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            source,
            "CommandInputProbe",
            new InMemoryAdditionalText("ProbeView.xaml", xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        Assert.IsEmpty(
            output.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            GeneratorProbe.Describe(output.GetDiagnostics()));

        StringAssert.Contains(run.GeneratedText(), ".InputBindings.Add(");
    }
}
