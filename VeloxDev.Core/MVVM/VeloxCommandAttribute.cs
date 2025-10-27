namespace VeloxDev.Core.MVVM
{
    /// <summary>
    /// Task ➤ Command
    /// <para><strong>Format : </strong> <c>Task MethodName(object? parameter, CancellationToken ct)</c></para>
    /// </summary>
    /// <param name="name">The name of the command, if not specified, it will be automatically generated</param>
    /// <param name="canValidate">True indicates that the executability verification of this command is enabled</param>
    /// <param name="semaphore">Concurrent Capacity</param>
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