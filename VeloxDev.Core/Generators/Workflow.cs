namespace VeloxDev.Core.Generators
{
    public static class Workflow
    {
        [AttributeUsage(AttributeTargets.Class)]
        public class ViewAttribute: Attribute;

        [AttributeUsage(AttributeTargets.Class)]
        public class ConnectionRendererAttribute : Attribute;

        [AttributeUsage(AttributeTargets.Property)]
        public class ContextAttribute : Attribute;
    }
}
