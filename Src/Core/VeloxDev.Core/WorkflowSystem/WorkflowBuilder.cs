using VeloxDev.Core.Interfaces.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem
{
    public sealed class WorkflowBuilder
    {
        public sealed class ViewModel
        {
            /// <summary>
            /// [ Generator ] Template Code For Workflow Tree Component
            /// </summary>
            /// <typeparam name="T"> The Type Of Helper </typeparam>
            /// <param name="virtualLinkType"> The Type Of VirtualLink </param>
            /// <param name="virtualSlotType"> The Type Of Slot In VirtualLink </param>
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class TreeAttribute<T>(Type? virtualLinkType = default, Type? virtualSlotType = default) : Attribute
                where T : IWorkflowTreeViewModelHelper, new()
            {
                public Type? VirtualLinkType { get; } = virtualLinkType;
                public Type? VirtualSlotType { get; } = virtualSlotType;
            }

            /// <summary>
            /// [ Generator ] Template Code For Workflow Node Component
            /// </summary>
            /// <typeparam name="T"> The Type Of Helper </typeparam>
            /// <param name="workSemaphore"> The concurrent capacity of Work Task </param>
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class NodeAttribute<T>(int workSemaphore = 1) : Attribute
                where T : IWorkflowNodeViewModelHelper, new()
            {
                public int WorkSemaphore { get; } = workSemaphore;
            }

            /// <summary>
            /// [ Generator ] Template Code For Workflow Slot Component
            /// </summary>
            /// <typeparam name="T"> The Type Of Helper </typeparam>
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class SlotAttribute<T> : Attribute
                where T : IWorkflowSlotViewModelHelper, new();

            /// <summary>
            /// [ Generator ] Template Code For Workflow Link Component
            /// </summary>
            /// <typeparam name="T"> The Type Of Helper </typeparam>
            /// <param name="slotType"> The Type Of Initial Slot </param>
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class LinkAttribute<T>(Type? slotType = default) : Attribute
                where T : IWorkflowLinkViewModelHelper, new()
            {
                public Type? SlotType { get; } = slotType;
            }
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
