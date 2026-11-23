using System.Windows.Input;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>
/// The shared command behavior of the self-drawn button controls.
/// </summary>
/// <remarks>
/// <para>
/// A left pointer release and <c>Space</c>/<c>Enter</c> activate a derived control; the derived control then calls
/// <see cref="ExecuteCommand"/> before changing its own state. While a command is assigned,
/// <see cref="ICommand.CanExecute"/> drives <see cref="VisualElement.IsEnabled"/>: the control is disabled when the
/// command refuses and re-enabled when it allows. An <see cref="VisualElement.IsEnabled"/>=false set by the author is
/// preserved until the command refuses at least once; from then on the command owns the value.
/// </para>
/// <para>
/// Commands are executed only when they can execute; a command that refuses while activated is skipped and the
/// control's own click or checked behavior still runs.
/// </para>
/// </remarks>
public abstract class ButtonBase : VisualElement
{
    /// <summary>The command executed when the control is activated.</summary>
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command),
        typeof(ICommand),
        typeof(ButtonBase),
        new PropertyMetadata(null) { AffectsRender = false });

    /// <summary>The value passed to <see cref="Command"/> when it is evaluated and executed.</summary>
    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
        nameof(CommandParameter),
        typeof(object),
        typeof(ButtonBase),
        new PropertyMetadata(null) { AffectsRender = false });

    private ICommand? _subscribedCommand;
    private bool _commandDisabledByCommand;

    /// <summary>The command executed when the control is activated, or <see langword="null"/>.</summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>The value passed to <see cref="Command"/> when it is evaluated and executed.</summary>
    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>Executes <see cref="Command"/> with <see cref="CommandParameter"/> when it can execute.</summary>
    /// <returns><see langword="true"/> when the command ran.</returns>
    protected bool ExecuteCommand()
    {
        if (Command is not { } command)
        {
            return false;
        }

        var parameter = CommandParameter;
        if (!command.CanExecute(parameter))
        {
            return false;
        }

        command.Execute(parameter);
        return true;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (ReferenceEquals(e.Property, CommandProperty))
        {
            if (_subscribedCommand is { } previous)
            {
                previous.CanExecuteChanged -= OnCommandCanExecuteChanged;
            }

            _subscribedCommand = Command;
            if (_subscribedCommand is { } current)
            {
                current.CanExecuteChanged += OnCommandCanExecuteChanged;
            }

            UpdateCommandEnabled();
        }
        else if (ReferenceEquals(e.Property, CommandParameterProperty))
        {
            UpdateCommandEnabled();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && _subscribedCommand is { } command)
        {
            command.CanExecuteChanged -= OnCommandCanExecuteChanged;
            _subscribedCommand = null;
        }

        base.Dispose(disposing);
    }

    private void OnCommandCanExecuteChanged(object? sender, EventArgs e) => UpdateCommandEnabled();

    // 只有命令自己关掉的状态由命令恢复；作者显式设置的 IsEnabled=false 不被覆盖。
    private void UpdateCommandEnabled()
    {
        var command = Command;
        if (command is not null && !command.CanExecute(CommandParameter))
        {
            _commandDisabledByCommand = true;
            IsEnabled = false;
            return;
        }

        if (_commandDisabledByCommand)
        {
            _commandDisabledByCommand = false;
            IsEnabled = true;
        }
    }
}
