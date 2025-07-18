#if NET5_0_OR_GREATER

namespace VeloxDev.Core.AspectOriented
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public class AspectOrientedAttribute : Attribute
    {

    }
}

#endif