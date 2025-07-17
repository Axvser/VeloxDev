namespace VeloxDev.Core.WorkflowSystem
{
    /// <summary>
    /// The options for the Task in the Workflow System.
    /// <para><see cref="Default"/> ➤ default config</para>
    /// <para><see cref="Concurrent"/> ➤ execute Tasks concurrently</para>
    /// <para><see cref="Fuse"/> ➤ use fuse for Exceptions</para>
    /// </summary>
    [Flags]
    public enum TaskOptions : int
    {
        Default = 1,
        Concurrent = 2,
        Fuse = 4
    }

    public sealed class Workflow
    {
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
        public sealed class ContextAttribute(TaskOptions taskOptions = TaskOptions.Default) : Attribute
        {
            public TaskOptions TaskOptions { get; } = taskOptions;
        }

        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
        public sealed class ContextTreeAttribute : Attribute;

        [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
        public sealed class ViewMappingAttribute<TContext>(Type viewType) : Attribute
        {
            public Type ContextType { get; } = typeof(TContext);
            public Type ViewType { get; } = viewType;
        }

        [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
        public sealed class ViewAttribute<TContext>(Type viewType) : Attribute
        {
            public Type ContextType { get; } = typeof(TContext);
            public Type ViewType { get; } = viewType;
        }
    }
}
