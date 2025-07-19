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
        /// In a workflow system, a view model must hold a mapping with a view, and this mapping must be unique within a tree structure
        /// </summary>
        /// <param name="leftType">type of the view model or view in the mapping relationship</param>
        /// <param name="rightType">type of the view model or view in the mapping relationship</param>
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
        public sealed class ViewMappingAttribute(Type leftType, Type rightType) : Attribute
        {
            public Type LeftType { get; } = leftType;
            public Type RightType { get; } = rightType;
        }
    }
}
