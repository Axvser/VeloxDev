namespace VeloxDev.WorkflowSystem;

/// <summary>Attributes that tell the source generator which helper a workflow component uses.</summary>
public sealed class WorkflowBuilder
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
        /// <summary>The link type used for the tree's provisional link.</summary>
        public Type? VirtualLinkType { get; } = virtualLinkType;
        /// <summary>The slot type used inside that provisional link.</summary>
        public Type? VirtualSlotType { get; } = virtualSlotType;
    }

    /// <summary>
    /// [ Generator ] Template Code For Workflow Node Component
    /// </summary>
    /// <typeparam name="T"> The Type Of Helper </typeparam>
    /// <param name="workSemaphore"> The concurrent capacity of the receive task </param>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class NodeAttribute<T>(int workSemaphore = 1) : Attribute
        where T : IWorkflowNodeViewModelHelper, new()
    {
        /// <summary>The concurrent capacity of the node's receive task.</summary>
        public int Semaphore { get; } = workSemaphore;
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
        /// <summary>The starter slot type a new link gets.</summary>
        public Type? SlotType { get; } = slotType;
    }
}
