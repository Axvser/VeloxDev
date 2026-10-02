#if NET

using System.Diagnostics.CodeAnalysis;
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
        // CWT 对 TValue 的 DAM 标注是为 GetOrCreateValue() 服务的，而我们只走带工厂的 GetValue()，永远不碰那条路。
        // 但类型实参是**未标注的泛型形参**时裁剪分析器照样报 IL2091，所以把标注传递下去。
        // 实参永远是生成的具体接口类型，而具体类型不查构造器 —— 调用点不会因此多出警告。
        private static class Entry<
            TClass,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TInterface>
            where TClass : class
            where TInterface : class, IAspectOriented
        {
            public static readonly ConditionalWeakTable<TClass, TInterface> Instances = [];
        }

        /// <summary>
        /// Resolve (get or create) the AOP proxy for the given instance.
        /// </summary>
        /// <typeparam name="TClass">The type behind the proxy.</typeparam>
        /// <typeparam name="TInterface">The generated AOP interface that mirrors <typeparamref name="TClass"/>.</typeparam>
        /// <param name="instance">The object the proxy stands for, and the cache key.</param>
        /// <param name="factory">Creates the proxy the first time <paramref name="instance"/> is resolved.</param>
        /// <returns>The cached proxy, or the one <paramref name="factory"/> just returned.</returns>
        /// <remarks>
        /// <typeparamref name="TInterface"/> carries <see cref="DynamicallyAccessedMembersAttribute"/> only to keep
        /// <see cref="ConditionalWeakTable{TKey, TValue}"/>'s annotation satisfied for the trimmer: that requirement
        /// serves <c>GetOrCreateValue</c>, which this method never calls. Concrete type arguments — every caller
        /// passes a generated interface — satisfy it without a public parameterless constructor.
        /// </remarks>
        public static TInterface Resolve<
            TClass,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TInterface>(
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
