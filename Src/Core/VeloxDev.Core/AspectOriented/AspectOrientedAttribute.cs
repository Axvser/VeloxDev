#if NET5_0_OR_GREATER

namespace VeloxDev.Core.AspectOriented
{
    /// <summary>
    /// Apply dynamic proxies to the target to support aspect-oriented programming
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public class AspectOrientedAttribute : Attribute
    {

    }
}

#endif