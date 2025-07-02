namespace VeloxDev.Core.Generators
{
    public static class WorkflowBuilder
    {
        [AttributeUsage(AttributeTargets.Class)]
        public class ViewAttribute(Type contextType) : Attribute
        {
            public Type ContextType { get; } = contextType;
        }

        [AttributeUsage(AttributeTargets.Class)]
        public class ConnectionRendererAttribute : Attribute;

        [AttributeUsage(AttributeTargets.Class)]
        public class ContextAttribute : Attribute;
    }
}
