namespace VeloxDev.Core.AOT
{
    /// <summary>
    /// Keep The Reflection Context In AOT
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    public sealed class AOTReflectionAttribute(
        string Namespace = "Auto",
        bool Constructors = true,
        bool Methods = true,
        bool Properties = true,
        bool Fields = true) : Attribute
    {
        public string Namespace { get; } = Namespace;
        public bool IncludeConstructors { get; } = Constructors;
        public bool IncludeMethods { get; } = Methods;
        public bool IncludeProperties { get; } = Properties;
        public bool IncludeFields { get; } = Fields;
    }
}
