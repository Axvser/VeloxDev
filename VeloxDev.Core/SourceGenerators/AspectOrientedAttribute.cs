#if NET

namespace VeloxDev.Core.SourceGenerators
{
    /// <summary>
    /// 🧰 > Enable the target to support aspect-oriented programming based on dynamic proxy
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class AspectOrientedAttribute() : Attribute
    {

    }
}

#endif