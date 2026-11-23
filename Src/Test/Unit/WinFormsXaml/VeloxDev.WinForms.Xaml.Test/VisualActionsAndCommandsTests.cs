using System.Drawing;
using System.Windows.Forms;
using System.Windows.Input;
using Microsoft.CodeAnalysis;
using VeloxDev.TransitionSystem;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The markup forms and runtime behavior of visual trigger actions and interactive command properties.
/// </summary>
[STATestClass]
public sealed class VisualActionsAndCommandsTests
{
    private const string GeneratorSource = """
        using System;
        using VeloxDev.WinForms.Xaml.Visuals;

        namespace ProbeActions;

        public sealed class ProbeAction : VisualTriggerAction
        {
            public int Count { get; set; }

            public override void Invoke(VisualElement target)
            {
            }
        }

        public partial class ActionView : VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel
        {
        }
        """;

    private const string GeneratorXaml = """
        <local:ActionView
            xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            xmlns:local="clr-namespace:ProbeActions"
            x:Class="ProbeActions.ActionView">

          <local:ActionView.Resources>
            <ControlTemplate x:Key="Chrome" TargetType="Border">
              <Border x:Name="PART_Frame" />
              <ControlTemplate.Triggers>
                <Trigger Property="IsEnabled" Value="False">
                  <Trigger.EnterAction>
                    <TriggerActionGroup>
                      <TransitionAction Property="Width" To="90" />
                      <KeyFrameAnimation Property="Height" Duration="0:0:0.1">
                        <KeyFrame Offset="0" Value="10" />
                        <KeyFrame Offset="1" Value="20" />
                      </KeyFrameAnimation>
                    </TriggerActionGroup>
                  </Trigger.EnterAction>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            <Style x:Key="Animated" TargetType="Border">
              <Style.Triggers>
                <Trigger Property="IsEnabled" Value="False">
                  <Trigger.Setters>
                    <Setter Property="Width" Value="80" />
                  </Trigger.Setters>
                  <Trigger.EnterAction>
                    <TransitionAction Property="Width" To="120" Duration="0:0:0.2" Ease="CubicOut" />
                  </Trigger.EnterAction>
                  <Trigger.ExitAction>
                    <KeyFrameAnimation Property="Width" Duration="0:0:0.3" Ease="Linear">
                      <KeyFrame Offset="0" Value="120" />
                      <KeyFrame Offset="1" Value="40" />
                    </KeyFrameAnimation>
                  </Trigger.ExitAction>
                </Trigger>
                <Trigger Property="IsEnabled" Value="True">
                  <Trigger.EnterAction>
                    <local:ProbeAction Count="2" />
                    <TransitionAction Property="Border.CornerRadius" To="8" />
                  </Trigger.EnterAction>
                </Trigger>
              </Style.Triggers>
            </Style>
          </local:ActionView.Resources>

          <Border x:Name="card" Style="{StaticResource Animated}" IsEnabled="False" />
        </local:ActionView>
        """;

    [TestMethod]
    public void VisualTriggerActions_CompileFromMarkup()
    {
        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            GeneratorSource,
            "VisualTriggerActionProbe",
            new InMemoryAdditionalText("ActionView.xaml", GeneratorXaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));

        var errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.IsEmpty(errors, GeneratorProbe.Describe(errors));

        var generated = run.GeneratedText();
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.Visuals.TransitionAction");
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.Visuals.KeyFrameAnimation");
        StringAssert.Contains(generated, "new global::VeloxDev.WinForms.Xaml.Visuals.TriggerActionGroup");
        StringAssert.Contains(generated, "new global::ProbeActions.ProbeAction()");
        StringAssert.Contains(generated, ".EnterAction = ");
        StringAssert.Contains(generated, ".ExitAction = ");
        StringAssert.Contains(generated, ".Actions.Add(");
        StringAssert.Contains(generated, ".KeyFrames.Add(new global::VeloxDev.WinForms.Xaml.Visuals.KeyFrame");
    }

    [TestMethod]
    public void StyleTrigger_RunsEnterAndExitActionsOnlyOnStateChange()
    {
        var border = new VC.Border();
        var enter = new RecordingTriggerAction();
        var exit = new RecordingTriggerAction();
        var style = new VisualStyle { TargetType = typeof(VC.Border) };
        var trigger = new VisualTrigger(VisualElement.IsEnabledProperty, false)
        {
            EnterAction = enter,
            ExitAction = exit,
        };
        style.Triggers.Add(trigger);

        border.Style = style;
        Assert.AreEqual(0, enter.Count, "an inactive trigger does not run its enter action");
        Assert.AreEqual(0, exit.Count);

        border.IsEnabled = false;
        Assert.AreEqual(1, enter.Count);
        Assert.AreEqual(0, exit.Count);

        border.IsEnabled = false;
        Assert.AreEqual(1, enter.Count, "setting the same value again is not a new activation");

        border.IsEnabled = true;
        Assert.AreEqual(1, exit.Count);

        border.IsEnabled = false;
        Assert.AreEqual(2, enter.Count);

        border.Style = null;
        Assert.AreEqual(2, exit.Count, "removing the style exits the trigger that was still active");
        Assert.AreSame(border, exit.Targets[^1]);
    }

    [TestMethod]
    public void TemplateTrigger_AppliesSettersAndActionsToTheNamedPart()
    {
        using var control = new VC.ContentControl();
        var frame = new VC.Border();
        var template = new VisualControlTemplate(scope =>
        {
            scope.Register("PART_Frame", frame);
            return new VC.Border();
        });

        var enter = new RecordingTriggerAction();
        var exit = new RecordingTriggerAction();
        template.Triggers.Add(new VisualTrigger(VisualElement.IsEnabledProperty, false)
        {
            TargetName = "PART_Frame",
            EnterAction = enter,
            ExitAction = exit,
            Setters = { new VisualSetter(VisualElement.WidthProperty, 66d) },
        });

        control.Template = template;
        Assert.AreEqual(0, enter.Count);

        control.IsEnabled = false;

        Assert.AreEqual(1, enter.Count);
        Assert.AreSame(frame, enter.Targets[^1], "the action receives the same named part as the setter");
        Assert.AreEqual(66d, frame.Width, "the trigger setter writes the named part");

        control.IsEnabled = true;
        Assert.AreEqual(1, exit.Count);
        Assert.AreSame(frame, exit.Targets[^1]);
    }

    [TestMethod]
    public void TransitionAction_AnimatesTheVisualProperty()
    {
        using var surface = new VisualHost { Size = new Size(240, 160), DpiScaleOverride = 1f };
        var border = new VC.Border();
        surface.Root = border;
        XamlTestSurface.EnsureHandle(surface);
        border.Width = 10d;

        var action = new TransitionAction
        {
            Property = VisualElement.WidthProperty,
            To = 100d,
            Effect = new TransitionEffect
            {
                Duration = TimeSpan.FromMilliseconds(80),
                Ease = Eases.Default,
            },
        };

        action.Invoke(border);

        Assert.IsTrue(
            XamlTestSurface.PumpUntil(() => Math.Abs(border.Width - 100d) < 0.5d),
            $"the animation should finish at 100; width was {border.Width}");
    }

    [TestMethod]
    public void KeyFrameAnimation_RunsThroughTheFrames()
    {
        using var surface = new VisualHost { Size = new Size(240, 160), DpiScaleOverride = 1f };
        var border = new VC.Border();
        surface.Root = border;
        XamlTestSurface.EnsureHandle(surface);
        border.Width = 10d;

        var animation = new KeyFrameAnimation
        {
            Property = VisualElement.WidthProperty,
            Duration = TimeSpan.FromMilliseconds(120),
            Ease = Eases.Default,
        };
        animation.KeyFrames.Add(new KeyFrame { Offset = 1d, Value = 130d });
        animation.KeyFrames.Add(new KeyFrame { Offset = 0d, Value = 20d });

        animation.Invoke(border);

        Assert.IsTrue(
            XamlTestSurface.PumpUntil(() => Math.Abs(border.Width - 130d) < 0.5d),
            $"the keyframe chain should finish at 130; width was {border.Width}");
    }

    [TestMethod]
    public void NewAnimation_SupersedesThePreviousOneAndStopFreezesIt()
    {
        using var surface = new VisualHost { Size = new Size(240, 160), DpiScaleOverride = 1f };
        var border = new VC.Border();
        surface.Root = border;
        XamlTestSurface.EnsureHandle(surface);
        border.Width = 10d;

        VisualTransition.Animate(
            border,
            VisualElement.WidthProperty,
            200d,
            new TransitionEffect { Duration = TimeSpan.FromMilliseconds(1200), Ease = Eases.Default });

        Assert.IsTrue(XamlTestSurface.PumpUntil(() => border.Width > 20d), "the first animation should start moving");

        VisualTransition.Animate(
            border,
            VisualElement.WidthProperty,
            40d,
            new TransitionEffect { Duration = TimeSpan.FromMilliseconds(80), Ease = Eases.Default });

        Assert.IsTrue(
            XamlTestSurface.PumpUntil(() => Math.Abs(border.Width - 40d) < 0.5d),
            $"the second animation should win; width was {border.Width}");

        XamlTestSurface.Pump(200);
        Assert.AreEqual(40d, border.Width, 1d, "the superseded animation must not resume towards 200");

        VisualTransition.Animate(
            border,
            VisualElement.WidthProperty,
            200d,
            new TransitionEffect { Duration = TimeSpan.FromMilliseconds(1200), Ease = Eases.Default });

        Assert.IsTrue(XamlTestSurface.PumpUntil(() => border.Width > 50d), "the stopped test needs a moving animation");
        VisualTransition.Stop(border);
        var frozen = border.Width;
        XamlTestSurface.Pump(250);

        Assert.IsTrue(border.Width < 150d, $"Stop must not let the animation reach its endpoint; width was {border.Width}");
        Assert.AreEqual(frozen, border.Width, 5d, "Stop freezes the animation where it stands");
    }

    [TestMethod]
    public void Button_ExecutesCommandAndHonorsCanExecute()
    {
        using var surface = new VisualHost { Size = new Size(220, 120) };
        var button = new VC.Button { Text = "Run" };
        var command = new ProbeCommand();
        button.Command = command;
        button.CommandParameter = "payload";
        surface.Root = button;

        Click(surface, button);
        Assert.AreEqual(1, command.ExecuteCount);
        Assert.AreEqual("payload", command.LastParameter);

        command.CanExecuteValue = false;
        command.RaiseCanExecuteChanged();
        Assert.IsFalse(button.IsEnabled, "CanExecute=false disables the button");

        Click(surface, button);
        Assert.AreEqual(1, command.ExecuteCount, "a refusing command must not execute");

        command.CanExecuteValue = true;
        command.RaiseCanExecuteChanged();
        Assert.IsTrue(button.IsEnabled, "the command re-enables the state it disabled");

        Click(surface, button);
        Assert.AreEqual(2, command.ExecuteCount);
    }

    [TestMethod]
    public void ToggleControls_ExecuteCommandAndKeepTheirCheckedBehavior()
    {
        AssertToggleExecutesCommand(new VC.ToggleButton(), static control => Assert.IsTrue(control.IsChecked));
        AssertToggleExecutesCommand(new VC.CheckBox(), static control => Assert.IsTrue(control.IsChecked == true));
        AssertToggleExecutesCommand(new VC.RadioButton(), static control => Assert.IsTrue(control.IsChecked == true));
    }

    [TestMethod]
    public void RepeatButton_ExecutesCommandOnEachClick()
    {
        using var surface = new VisualHost { Size = new Size(220, 120) };
        using var button = new VC.RepeatButton { Text = "Repeat" };
        var command = new ProbeCommand();
        button.Command = command;
        surface.Root = button;

        Click(surface, button);
        Assert.AreEqual(1, command.ExecuteCount, "the initial click executes the command");

        Click(surface, button);
        Assert.AreEqual(2, command.ExecuteCount, "the repeat button keeps the base button's command behavior");
    }

    private static void AssertToggleExecutesCommand<T>(T control, Action<T> assertChecked)
        where T : VC.ButtonBase
    {
        using var surface = new VisualHost { Size = new Size(220, 120) };
        var command = new ProbeCommand();
        control.Command = command;
        control.CommandParameter = control.GetType().Name;
        surface.Root = control;

        Click(surface, control);

        Assert.AreEqual(1, command.ExecuteCount, $"{control.GetType().Name} should execute its command");
        Assert.AreEqual(control.GetType().Name, command.LastParameter);
        assertChecked(control);
    }

    private static void Click(VisualHost surface, VisualElement element)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        surface.RenderTo(graphics, surface.Size);

        var bounds = element.AbsoluteBounds;
        var center = new PointF(
            bounds.X + (bounds.Width / 2f),
            bounds.Y + (bounds.Height / 2f));
        surface.ProcessPointerDown(center, MouseButtons.Left);
        surface.ProcessPointerUp(center, MouseButtons.Left);
    }

    private sealed class RecordingTriggerAction : VisualTriggerAction
    {
        internal List<VisualElement> Targets { get; } = [];

        internal int Count => Targets.Count;

        public override void Invoke(VisualElement target) => Targets.Add(target);
    }

    private sealed class ProbeCommand : ICommand
    {
        internal bool CanExecuteValue { get; set; } = true;

        internal int ExecuteCount { get; private set; }

        internal object? LastParameter { get; private set; }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => CanExecuteValue;

        public void Execute(object? parameter)
        {
            ExecuteCount++;
            LastParameter = parameter;
        }

        internal void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
