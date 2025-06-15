#if NET

using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace VeloxDev.Core.AspectOriented
{
    public delegate object? ProxyHandler(object?[]? parameters, object? previous);

    public class ProxyInstance() : DispatchProxy, IDisposable
    {
        public static ConditionalWeakTable<object, ProxyInstance> ProxyInstances { get; internal set; } = [];

        internal WeakReference<object>? _target = null;
        internal Type? _targetType = null;

        internal ConcurrentDictionary<string, ProxyManager> GetterActions { get; set; } = [];
        internal ConcurrentDictionary<string, ProxyManager> SetterActions { get; set; } = [];
        internal ConcurrentDictionary<string, ProxyManager> MethodActions { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var Name = targetMethod?.Name ?? string.Empty;
            if (Name == string.Empty || _targetType is null) return null;
            if (_target is not null && _target.TryGetTarget(out var target))
            {
                var actions = Name switch
                {
                    string n when n.StartsWith("get_") => GetterActions.TryGetValue(n, out var a) ? a : null,
                    string n when n.StartsWith("set_") => SetterActions.TryGetValue(n, out var a) ? a : null,
                    _ => MethodActions.TryGetValue(Name, out var a) ? a : null
                };

                var intercept = actions?.Intercept?.GetInvocationList();
                var coverage = actions?.Cover?.GetInvocationList();
                var callback = actions?.CallBack?.GetInvocationList();

                var R0 = intercept?.Invoke(args, null);
                var R1 = coverage == null
                    ? _targetType.GetMethod(Name)?.Invoke(target, args)
                    : coverage.Invoke(args, R0);
                callback?.Invoke(args, R1);

                return R1;
            }
            else
            {
                return null;
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}

#endif