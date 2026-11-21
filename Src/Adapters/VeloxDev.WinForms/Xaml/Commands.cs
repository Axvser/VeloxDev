using System.Windows.Input;

namespace VeloxDev.WinForms.Xaml;

/// <summary>
/// An <see cref="ICommand"/> whose execute and can-execute behaviour comes from delegates.
/// </summary>
/// <remarks>
/// <para>
/// The command never observes its own state. <see cref="RaiseCanExecuteChanged"/> is the only way to tell command
/// sources to re-query <see cref="CanExecute"/>; call it on the UI thread after the delegates' state changes.
/// </para>
/// <para>
/// A <see langword="null"/> can-execute delegate means the command is always executable.
/// </para>
/// </remarks>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    /// <summary>Creates a command from a parameterized execute delegate.</summary>
    /// <param name="execute">The action run by <see cref="Execute"/>.</param>
    /// <param name="canExecute">The predicate queried by <see cref="CanExecute"/>; null means always executable.</param>
    /// <exception cref="ArgumentNullException"><paramref name="execute"/> is <see langword="null"/>.</exception>
    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <summary>Creates a command from a parameterless execute delegate.</summary>
    /// <param name="execute">The action run by <see cref="Execute"/>.</param>
    /// <param name="canExecute">The predicate queried by <see cref="CanExecute"/>; null means always executable.</param>
    /// <exception cref="ArgumentNullException"><paramref name="execute"/> is <see langword="null"/>.</exception>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        if (execute is null) throw new ArgumentNullException(nameof(execute));

        _execute = _ => execute();
        _canExecute = canExecute is null ? null : _ => canExecute();
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    /// <inheritdoc />
    public void Execute(object? parameter) => _execute(parameter);

    /// <summary>Notifies command sources that <see cref="CanExecute"/> may have changed.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

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
