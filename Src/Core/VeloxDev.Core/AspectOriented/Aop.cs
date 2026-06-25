#if NET

using System.Runtime.CompilerServices;

namespace VeloxDev.AspectOriented
{
    /// <summary>
    /// Infrastructure for AOP proxy lifecycle management.
    /// Provides proxy-to-target reverse lookup.
    /// </summary>
    public static class Aop
    {
        private static readonly ConditionalWeakTable<object, object> _proxyToTarget = [];

        /// <summary>
        /// Register a proxy-to-target mapping.
        /// Called internally by generated extension methods.
        /// </summary>
        public static void Map(object proxy, object target)
            => _proxyToTarget.Add(proxy, target);

        /// <summary>
        /// Reverse lookup: get the original instance from its AOP proxy.
        /// </summary>
        public static TTarget? GetTarget<TTarget>(IAspectOriented proxy) where TTarget : class
            => _proxyToTarget.TryGetValue(proxy, out var t) ? (TTarget)t : null;
    }
}

#endif
