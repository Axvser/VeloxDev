namespace VeloxDev.WPF.SourceGeneratorMark
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class FocusModuleAttribute(bool CanFocused = true, bool CanDefaultStyle = false) : Attribute
    {
        public bool CanFocused { get; private set; } = CanFocused;
        public bool CanDefaultStyle { get; private set; } = CanDefaultStyle;
    }
}
