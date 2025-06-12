namespace VeloxDev.Core.SourceGenerators
{
    /// <summary>
    /// Provide a set of millisecond values for common FPS
    /// </summary>
    public enum StandardFPS : int
    {
        FPS_30 = 33,
        FPS_45 = 22,
        FPS_60 = 17,
        FPS_120 = 8,
        FPS_144 = 7,
        FPS_165 = 6,
    }

    /// <summary>
    /// ✨ View >>> Enable the control to have a complete life cycle
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class MonoBehaviourAttribute(int MilliSeconds) : Attribute
    {
        public int TimeSpan { get; private set; } = Math.Abs(MilliSeconds);
    }
}
