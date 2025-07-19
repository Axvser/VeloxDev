namespace VeloxDev.Core.WorkflowSystem
{
    public sealed class Workflow
    {
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
        public sealed class ContextAttribute : Attribute;

        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
        public sealed class ContextTreeAttribute : Attribute;
    }
}
