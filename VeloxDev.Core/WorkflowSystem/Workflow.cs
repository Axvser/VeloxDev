namespace VeloxDev.Core.WorkflowSystem
{
    public sealed class Workflow
    {
        /// <summary>
        /// In a workflow system, a node view must hold a view model
        /// </summary>
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
        public sealed class ContextAttribute : Attribute;

        /// <summary>
        /// In a workflow system, several node view models must form a tree-structured view model
        /// </summary>
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
        public sealed class ContextTreeAttribute : Attribute;

        /// <summary>
        /// In a workflow system, use a connector view model to connect two node view models
        /// </summary>
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
        public sealed class ContextcConnectorAttribute : Attribute;
    }
}
