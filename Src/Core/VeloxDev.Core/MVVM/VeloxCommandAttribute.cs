using System.Windows.Input;

namespace VeloxDev.MVVM
{
    /// <summary>
    /// Marks and automatically constructs an <see cref="IVeloxCommand"/> : <see cref="ICommand"/> instance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The marked method may take nothing, <c>(object? parameter)</c>, <c>(CancellationToken ct)</c>, or
    /// <c>(object? parameter, CancellationToken ct)</c>, and may return <see cref="Task"/>, <c>Task&lt;T&gt;</c>,
    /// <c>ValueTask</c>, <c>ValueTask&lt;T&gt;</c>, or <see langword="void"/>.
    /// </para>
    /// <para>
    /// One combination of those is rejected: a <see langword="void"/> method that takes a
    /// <see cref="CancellationToken"/>. Nothing a synchronous body could do with the token, and the generated
    /// code does not compile — return <see cref="Task"/> when the body is meant to observe cancellation.
    /// </para>
    /// <para>
    /// Only a <see cref="CancellationToken"/> parameter lets the command actually stop the body: the other shapes
    /// build a command whose body never receives a token, so an interrupted execution reports
    /// <see cref="CommandEventType.Canceled"/> while the body runs on to completion. See
    /// <see cref="VeloxCommand.CreateTaskOnlyWithParameter"/>.
    /// </para>
    /// <para>
    /// A generated <c>{Name}Command</c> property is built lazily on first read, so the command object does not
    /// exist until something asks for it.
    /// </para>
    /// </remarks>
    /// <param name="name">
    /// The name of the command. If set to <c>"Auto"</c> (default), the command property name is automatically generated from the method name (e.g., <c>MyMethod</c> → <c>MyCommand</c>).
    /// </param>
    /// <param name="canValidate">
    /// Whether to enable command executability validation. If set to <see langword="true"/>, the hosting class must also implement a
    /// <c>private partial bool CanExecute{Name}Command(object? parameter)</c> for each generated command (e.g., <c>CanExecuteSaveCommand</c> accompanies <c>SaveCommand</c>).
    /// That partial declaration is <c>private</c> and returns a value, so omitting the implementation is a compile error rather than a command that is silently always executable.
    /// </param>
    /// <param name="semaphore">
    /// The maximum number of concurrent executions for the command (semaphore capacity). Default is 1 (serial execution). Setting it to a value greater than 1 allows multiple instances to run in parallel.
    /// Must be ≥ 1; a smaller value makes reading the generated <c>{Name}Command</c> property throw <see cref="ArgumentOutOfRangeException"/>.
    /// </param>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class VeloxCommandAttribute(
        string name = "Auto",
        bool canValidate = false,
        int semaphore = 1) : Attribute
    {
        public string Name { get; } = name;
        public bool CanValidate { get; } = canValidate;
        public int Semaphore { get; } = semaphore;
    }
}