#if NET

using VeloxDev.Core.WeakTypes;

namespace VeloxDev.Core.AspectOriented
{
    internal class ProxyManager()
    {
        internal WeakDelegate<ProxyHandler> Intercept { get; set; } = new();
        internal WeakDelegate<ProxyHandler> Cover { get; set; } = new();
        internal WeakDelegate<ProxyHandler> CallBack { get; set; } = new();
    }
}

#endif