#if NET

namespace VeloxDev.AspectOriented
{
    /// <summary>
    /// Which accessor of a member an aspect is being installed on.
    /// </summary>
    public enum ProxyMembers
    {
        /// <summary>The property's getter.</summary>
        Getter,

        /// <summary>The property's setter.</summary>
        Setter,

        /// <summary>A method.</summary>
        Method
    }

    /// <summary>
    /// Installs aspects on the proxy a generated <c>Aop()</c> returns.
    /// </summary>
    /// <remarks>
    /// A property's getter and setter are separate members and take separate aspects, which is why
    /// <see cref="ProxyMembers"/> exists rather than one call covering the property.
    /// </remarks>
    public static class ProxyEx
    {
        /// <summary>
        /// Installs the three stages on one member of <paramref name="target"/>.
        /// </summary>
        /// <typeparam name="T">The generated AOP interface.</typeparam>
        /// <param name="target">The proxy — what <c>Aop()</c> returned, never the object behind it.</param>
        /// <param name="memberType">Which accessor of the member is being hooked.</param>
        /// <param name="memberName">The member's plain name, as written in the class.</param>
        /// <param name="start">Runs before the member, or <see langword="null"/>.</param>
        /// <param name="coverage">Replaces the member when it is not <see langword="null"/>.</param>
        /// <param name="end">Runs after the member, or <see langword="null"/>.</param>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="target"/> is the original object rather than a proxy. Aspects installed there would
        /// never run, so this is refused instead of ignored.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="memberName"/> is not an interceptable member of the proxied type.
        /// </exception>
        /// <remarks>
        /// Installing on a member twice <em>replaces</em> its aspects rather than stacking them: all three
        /// stages are always written together.
        /// </remarks>
        public static void SetProxy<T>(
            this T target,
            ProxyMembers memberType,
            string memberName,
            ProxyHandler? start,
            ProxyHandler? coverage,
            ProxyHandler? end)
            where T : IAspectOriented
        {
            // 挂在真身上时，代理永远不会经过它 —— 那是个不会报错的失效，所以这里拒绝而不是忽略。
            if (target is not IAopHookTarget hookTarget)
            {
                throw new InvalidOperationException(
                    $"'{target?.GetType().Name ?? "null"}' is not an AOP proxy. " +
                    "Install aspects on the instance Aop() returns, not on the object behind it.");
            }

            string memberKey = memberType switch
            {
                ProxyMembers.Getter => "get_" + memberName,
                ProxyMembers.Setter => "set_" + memberName,
                _ => memberName,
            };

            hookTarget.SetHooks(memberKey, new AspectHooks(start, coverage, end));
        }
    }
}

#endif
