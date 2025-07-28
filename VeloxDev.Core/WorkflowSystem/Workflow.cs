namespace VeloxDev.Core.WorkflowSystem
{
    public sealed class Workflow
    {
        public sealed class Context
        {
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class TreeAttribute(Type? slotType = default, Type? linkType = default) : Attribute
            {
                public Type? SlotType { get; } = slotType;
                public Type? LinkType { get; } = linkType;
            }

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class NodeAttribute : Attribute;

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class SlotAttribute : Attribute;

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class LinkAttribute : Attribute;
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
