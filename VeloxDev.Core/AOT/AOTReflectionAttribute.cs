namespace VeloxDev.Core.AOT
{
    /// <summary>
    /// Marking this type should retain the reflection information in the AOT environment
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
    public sealed class AOTReflectionAttribute(
        bool Constructors = false,
        bool Methods = false,
        bool Properties = false,
        bool Fields = false) : Attribute
    {
        public bool IncludeConstructors { get; } = Constructors;
        public bool IncludeMethods { get; } = Methods;
        public bool IncludeProperties { get; } = Properties;
        public bool IncludeFields { get; } = Fields;
    }
}
