#if NET

using System.Reflection;

namespace VeloxDev.WPF.AspectOriented
{
    public static class ProxyExtension
    {
        public static T CreateProxy<T>(this T target) where T : IProxy
        {
            var type = typeof(T);
            dynamic proxy = DispatchProxy.Create<T, ProxyInstance>() ?? throw new InvalidOperationException();
            proxy._target = target;
            proxy._targetType = type;
            ProxyInstance.ProxyIDs.Add(proxy, proxy._localid);
            return proxy;
        }
        public static T SetPropertyGetter<T>(this T source, string propertyName, ProxyHandler? start, ProxyHandler? coverage, ProxyHandler? end) where T : IProxy
        {
            if (!ProxyInstance.ProxyIDs.TryGetValue(source, out var id))
            {
                return source;
            }
            if (ProxyInstance.ProxyInstances.TryGetValue(id, out var instance))
            {
                var Name = $"get_{propertyName}";
                if (instance.GetterActions.ContainsKey(Name))
                {
                    instance.GetterActions[Name] = Tuple.Create(start, coverage, end);
                }
                else
                {
                    instance.GetterActions.Add(Name, Tuple.Create(start, coverage, end));
                }
            }
            return source;
        }
        public static T SetPropertySetter<T>(this T source, string propertyName, ProxyHandler? start, ProxyHandler? coverage, ProxyHandler? end) where T : IProxy
        {
            if (!ProxyInstance.ProxyIDs.TryGetValue(source, out var id))
            {
                return source;
            }
            if (ProxyInstance.ProxyInstances.TryGetValue(id, out var instance))
            {
                var Name = $"set_{propertyName}";
                if (instance.SetterActions.ContainsKey(Name))
                {
                    instance.SetterActions[Name] = Tuple.Create(start, coverage, end);
                }
                else
                {
                    instance.SetterActions.Add(Name, Tuple.Create(start, coverage, end));
                }
            }
            return source;
        }
        public static T SetMethod<T>(this T source, string methodName, ProxyHandler? start, ProxyHandler? coverage, ProxyHandler? end) where T : IProxy
        {
            if (!ProxyInstance.ProxyIDs.TryGetValue(source, out var id))
            {
                return source;
            }
            if (ProxyInstance.ProxyInstances.TryGetValue(id, out var instance))
            {
                if (instance.MethodActions.ContainsKey(methodName))
                {
                    instance.MethodActions[methodName] = Tuple.Create(start, coverage, end);
                }
                else
                {
                    instance.MethodActions.Add(methodName, Tuple.Create(start, coverage, end));
                }
            }
            return source;
        }
    }
}

#endif