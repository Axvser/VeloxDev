namespace VeloxDev.Core.TimeLine
{
    /// <summary>
    /// Enables the instance to run MonoBehaviour-like lifecycle methods in the TimeLine system.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class MonoBehaviourAttribute : Attribute
    {

    }
}
