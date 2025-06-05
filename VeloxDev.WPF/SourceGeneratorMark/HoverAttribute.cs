namespace VeloxDev.WPF.SourceGeneratorMark
{
    /// <summary>
    /// ✨ View >>> Adds a hover-animation behavior for the specified property in the View layer
    /// </summary>
    /// <param name="propertyNames"> The names of the properties that can hover.</param>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class HoverAttribute(string[] propertyNames) : Attribute
    {
        public string[] PropertyNames { get; private set; } = propertyNames;
    }
}