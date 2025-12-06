#if NET
using System.Reflection;

namespace VeloxDev.Core.AspectOriented
{
    public delegate object? ProxyHandler(object?[]? parameters, object? previous);

    public class ProxyInstance : DispatchProxy
    {
        internal static int _id = 0;
        public static Dictionary<int, ProxyInstance> ProxyInstances { get; internal set; } = [];
        public static Dictionary<object, int> ProxyIDs { get; internal set; } = [];

        public ProxyInstance() { _localid = _id; _id++; ProxyInstances.Add(_localid, this); }

        internal object? _target = null;
        internal Type? _targetType = null;
        internal int _localid = 0;

        internal Dictionary<string, Tuple<ProxyHandler?, ProxyHandler?, ProxyHandler?>> GetterActions { get; set; } = [];
        internal Dictionary<string, Tuple<ProxyHandler?, ProxyHandler?, ProxyHandler?>> SetterActions { get; set; } = [];
        internal Dictionary<string, Tuple<ProxyHandler?, ProxyHandler?, ProxyHandler?>> MethodActions { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var Name = targetMethod?.Name ?? string.Empty;

            if (Name == string.Empty) return null;

            if (Name.StartsWith("get_"))
            {
                GetterActions.TryGetValue(Name, out var actions);
                var R0 = actions?.Item1?.Invoke(args, null);
                var R1 = actions?.Item2 == null ? _targetType?.GetMethod(Name)?.Invoke(_target, args) : actions.Item2.Invoke(args, R0);
                actions?.Item3?.Invoke(args, R1);
                return R1;
            }
            else if (Name.StartsWith("set_"))
            {
                SetterActions.TryGetValue(Name, out var actions);
                var R0 = actions?.Item1?.Invoke(args, null);
                var R1 = actions?.Item2 == null ? _targetType?.GetMethod(Name)?.Invoke(_target, args) : actions.Item2.Invoke(args, R0);
                actions?.Item3?.Invoke(args, R1);
                return R1;
            }
            else
            {
                MethodActions.TryGetValue(Name, out var actions);
                var R0 = actions?.Item1?.Invoke(args, null);
                var R1 = actions?.Item2 == null ? _targetType?.GetMethod(Name)?.Invoke(_target, args) : actions.Item2.Invoke(args, R0);
                actions?.Item3?.Invoke(args, R1);
                return R1;
            }
        }
    }
}

#endif