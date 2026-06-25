#if NET

using System.Runtime.CompilerServices;

namespace VeloxDev.AspectOriented
{
    /// <summary>
    /// Generic weak-reference cache for AOP proxy instances.
    /// Each pair (TClass, TInterface) gets its own <see cref="ConditionalWeakTable{TClass, TInterface}"/>
    /// via CLR generic specialization — no per-class generated code needed.
    /// </summary>
    public static class AopCache
    {
        private static class Entry<TClass, TInterface>
            where TClass : class
            where TInterface : class, IAspectOriented
        {
            public static readonly ConditionalWeakTable<TClass, TInterface> Instances = [];
        }

        /// <summary>
        /// Resolve (get or create) the AOP proxy for the given instance.
        /// </summary>
        public static TInterface Resolve<TClass, TInterface>(
            TClass instance,
            Func<TClass, TInterface> factory)
            where TInterface : class, IAspectOriented
            where TClass : class
        {
            return Entry<TClass, TInterface>.Instances
                .GetValue(instance, k => factory(k));
        }
    }
}

#endif
