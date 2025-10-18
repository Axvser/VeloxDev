namespace VeloxDev.Core.WorkflowSystem
{
    public sealed class WorkflowBuilder
    {
        public sealed class ViewModel
        {
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class TreeAttribute(Type? helperType = default, Type? virtualLinkType = default, Type? linkGroupType = default) : Attribute
            {
                public Type? HelperType { get; } = helperType;
                public Type? VirtualLinkType { get; } = virtualLinkType;
                public Type? LinkGroupType { get; } = linkGroupType;
            }

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class NodeAttribute(Type? helperType = default, int semaphore = 1) : Attribute
            {
                public Type? HelperType { get; } = helperType;
                public int Semaphore { get; } = semaphore;
            }

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class SlotAttribute(Type? helperType = default) : Attribute
            {
                public Type? HelperType { get; } = helperType;
            }

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class LinkAttribute(Type? helperType = default) : Attribute
            {
                public Type? HelperType { get; } = helperType;
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
