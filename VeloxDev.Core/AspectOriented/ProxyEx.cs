#if NET

using System.Collections.Concurrent;
using System.Reflection;
using VeloxDev.Core.Interfaces.AspectOriented;

namespace VeloxDev.Core.AspectOriented
{
    public enum ProxyMembers
    {
        Getter,
        Setter,
        Method
    }

    public static class ProxyEx
    {
        public static T CreateProxy<T>(this T target) where T : IProxy
        {
            var type = typeof(T);
            dynamic proxy = DispatchProxy.Create<T, ProxyInstance>() ?? throw new InvalidOperationException();
            proxy._target = new WeakReference(target);
            proxy._targetType = type;
            ProxyInstance.ProxyInstances.Add(target, proxy);
            return proxy;
        }

        public static T AddProxy<T>(
            this T source,
            ProxyMembers memberType,
            string memberName,
            ProxyHandler? start = null,
            ProxyHandler? coverage = null,
            ProxyHandler? end = null) where T : IProxy
        {
            if (!ProxyInstance.ProxyInstances.TryGetValue(source, out var proxy))
                return source;

            var (actionDict, fullName) = GetProxyInfo(proxy, memberType, memberName);
            var manager = GetOrCreateProxyManager(actionDict, fullName);

            AddHandlersToManager(manager, start, coverage, end);

            return source;
        }

        public static T RemoveProxy<T>(
            this T source,
            ProxyMembers memberType,
            string memberName,
            ProxyHandler?[]? starts = null,
            ProxyHandler?[]? coverages = null,
            ProxyHandler?[]? ends = null) where T : IProxy
        {
            if (!ProxyInstance.ProxyInstances.TryGetValue(source, out var proxy))
                return source;

            var (actionDict, fullName) = GetProxyInfo(proxy, memberType, memberName);

            if (actionDict.TryGetValue(fullName, out var manager))
            {
                RemoveHandlersFromManager(manager, starts, coverages, ends);
            }

            return source;
        }

        private static void RemoveHandlersFromManager(
            ProxyManager manager,
            ProxyHandler?[]? starts,
            ProxyHandler?[]? coverages,
            ProxyHandler?[]? ends)
        {
            if (starts is not null)
            {
                foreach (var start in starts)
                {
                    manager.Intercept.RemoveHandler(start);
                }
            }
            if (coverages is not null)
            {
                foreach (var coverage in coverages)
                {
                    manager.Cover.RemoveHandler(coverage);
                }
            }
            if (ends is not null)
            {
                foreach (var end in ends)
                {
                    manager.CallBack.RemoveHandler(end);
                }
            }
        }

        private static (ConcurrentDictionary<string, ProxyManager> dict, string name) GetProxyInfo(
            ProxyInstance proxy,
            ProxyMembers memberType,
            string memberName) => memberType switch
            {
                ProxyMembers.Getter => (proxy.GetterActions, $"get_{memberName}"),
                ProxyMembers.Setter => (proxy.SetterActions, $"set_{memberName}"),
                ProxyMembers.Method => (proxy.MethodActions, memberName),
                _ => throw new ArgumentOutOfRangeException(nameof(memberType))
            };

        private static ProxyManager GetOrCreateProxyManager(
            ConcurrentDictionary<string, ProxyManager> actionDict,
            string fullName)
        {
            if (!actionDict.TryGetValue(fullName, out var manager))
            {
                manager = new ProxyManager();
                actionDict.TryAdd(fullName, manager);
            }
            return manager;
        }

        private static void AddHandlersToManager(
            ProxyManager manager,
            ProxyHandler? start,
            ProxyHandler? coverage,
            ProxyHandler? end)
        {
            if (start is not null) manager.Intercept.AddHandler(start);
            if (coverage is not null) manager.Cover.AddHandler(coverage);
            if (end is not null) manager.CallBack.AddHandler(end);
        }
    }
}

#endif