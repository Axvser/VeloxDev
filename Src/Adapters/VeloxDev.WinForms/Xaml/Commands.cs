using System.Windows.Input;

namespace VeloxDev.WinForms.Xaml;

/// <summary>
/// Pairs an <see cref="ICommand"/> with optional delegates that replace its execute and can-execute behaviour.
/// </summary>
/// <remarks>
/// This is the declarative shape: every member has a public setter, so XAML can create one and assign a command,
/// a can-execute predicate and an executed action. When a delegate is absent the command's own member is used.
/// </remarks>
public sealed class CommandBinding
{
    /// <summary>Creates an empty binding.</summary>
    public CommandBinding()
    {
    }

    /// <summary>Creates a binding for <paramref name="command"/>.</summary>
    /// <param name="command">The command to pair with the delegates.</param>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
    public CommandBinding(ICommand command)
    {
        Command = command ?? throw new ArgumentNullException(nameof(command));
    }

    /// <summary>The command being bound; <see langword="null"/> means the binding never executes.</summary>
    public ICommand? Command { get; set; }

    /// <summary>The action run instead of <see cref="ICommand.Execute"/>; <see langword="null"/> uses the command's own.</summary>
    public Action<object?>? Executed { get; set; }

    /// <summary>The predicate queried instead of <see cref="ICommand.CanExecute"/>; <see langword="null"/> uses the command's own.</summary>
    public Func<object?, bool>? CanExecute { get; set; }

    /// <summary>Asks the command (or the can-execute delegate) whether it can execute with <paramref name="parameter"/>.</summary>
    /// <param name="parameter">The value passed to the command.</param>
    /// <returns><see langword="true"/> when the command exists and allows execution.</returns>
    public bool CanExecuteCommand(object? parameter)
        => Command is not null && (CanExecute?.Invoke(parameter) ?? Command.CanExecute(parameter));

    /// <summary>Executes the command (or the executed delegate) when it can execute.</summary>
    /// <param name="parameter">The value passed to the command.</param>
    /// <returns><see langword="true"/> when the command ran.</returns>
    public bool TryExecute(object? parameter)
    {
        if (!CanExecuteCommand(parameter))
        {
            return false;
        }

        if (Executed is not null)
        {
            Executed(parameter);
        }
        else
        {
            Command!.Execute(parameter);
        }

        return true;
    }
}
