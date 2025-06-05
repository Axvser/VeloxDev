namespace VeloxDev.WPF.SourceGeneratorMark
{
    /// <summary>
    /// ✨ View >>> Enable the control to have a complete life cycle
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class MonoBehaviourAttribute(double MilliSeconds) : Attribute
    {
        public double TimeSpan { get; private set; } = Math.Abs(MilliSeconds);
    }
}
