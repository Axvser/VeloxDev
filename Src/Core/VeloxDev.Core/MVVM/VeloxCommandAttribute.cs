using System.Windows.Input;
using VeloxDev.Core.Interfaces.MVVM;

namespace VeloxDev.Core.MVVM
{
    /// <summary>
    /// Marks and automatically constructs an <see cref="IVeloxCommand"/> : <see cref="ICommand"/> instance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The marked method must satisfy one of the following signatures (return value must be <see cref="Task"/> or <see langword="true"/>):
    /// </para>
    /// <list type="bullet">
    ///   <item><c>Task MethodName(object? parameter, CancellationToken ct)</c></item>
    ///   <item><c>Task MethodName(object? parameter)</c></item>
    ///   <item><c>Task MethodName(CancellationToken ct)</c></item>
    ///   <item><c>Task MethodName()</c></item>
    ///   <item><c>void MethodName(object? parameter)</c></item>
    ///   <item><c>void MethodName()</c></item>
    /// </list>
    /// <para>
    /// The framework will automatically adapt and wrap it into an asynchronous command based on the actual signature, supporting cancellation, concurrency control, and complete lifecycle events.
    /// </para>
    /// </remarks>
    /// <param name="name">
    /// The name of the command. If set to <c>"Auto"</c> (default), the command property name is automatically generated from the method name (e.g., <c>MyMethod</c> → <c>MyCommand</c>).
    /// </param>
    /// <param name="canValidate">
    /// Whether to enable command executability validation. If set to <see langword="true"/>, a corresponding Boolean property or method named <c>CanXxx</c> must be provided (e.g., <c>CanSave</c> corresponds to <c>SaveCommand</c>).
    /// </param>
    /// <param name="semaphore">
    /// The maximum number of concurrent executions for the command (semaphore capacity). Default is 1 (serial execution). Setting it to a value greater than 1 allows multiple instances to run in parallel.
    /// Must be ≥ 1.
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