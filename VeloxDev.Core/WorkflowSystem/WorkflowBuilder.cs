using VeloxDev.Core.Interfaces.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem
{
    public sealed class WorkflowBuilder
    {
        public sealed class ViewModel
        {
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class TreeAttribute<T> : Attribute
                where T : IWorkflowTreeViewModelHelper;

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class NodeAttribute<T> : Attribute
                where T : IWorkflowNodeViewModelHelper;

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class SlotAttribute<T> : Attribute
                where T : IWorkflowSlotViewModelHelper;

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class LinkAttribute<T> : Attribute
                where T : IWorkflowLinkViewModelHelper;
        }

        public sealed class View
        {
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class TreeAttribute : Attribute;

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class NodeAttribute : Attribute;

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class SlotAttribute : Attribute;

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class LinkAttribute : Attribute;
        }
    }
}
