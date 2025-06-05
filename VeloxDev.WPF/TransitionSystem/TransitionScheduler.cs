using System.Collections.Concurrent;
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using VeloxDev.WPF.StructuralDesign.Animator;
using VeloxDev.WPF.TransitionSystem.Basic;

namespace VeloxDev.WPF.TransitionSystem
{
    public sealed class TransitionScheduler
    {
        internal TransitionScheduler(object instance, params State[] states)
        {
            TransitionApplied = new WeakReference<object>(instance);
            Type = instance.GetType();
            InitializeTypes(Type);
            States = [.. states];
        }

        public static ConditionalWeakTable<object, TransitionScheduler> MachinePool { get; internal set; } = new();
        public static ConcurrentDictionary<Type, Tuple<ConcurrentDictionary<string, PropertyInfo>, ConcurrentDictionary<string, PropertyInfo>, ConcurrentDictionary<string, PropertyInfo>, ConcurrentDictionary<string, PropertyInfo>, ConcurrentDictionary<string, PropertyInfo>, ConcurrentDictionary<string, PropertyInfo>, ConcurrentDictionary<string, PropertyInfo>>> SplitedPropertyInfos { get; internal set; } = new();
        public static TransitionScheduler CreateUniqueUnit(object targetObj, params State[] states)
        {
            if (MachinePool.TryGetValue(targetObj, out var scheduler))
            {
                scheduler.States = [.. states];
                return scheduler;
            }
            else
            {
                var newScheduler = new TransitionScheduler(targetObj, states);
                MachinePool.Add(targetObj, newScheduler);
                return newScheduler;
            }
        }
        public static TransitionScheduler CreateIndependentUnit(object targetObj, params State[] states)
        {
            return new TransitionScheduler(targetObj, states);
        }
        public static void InitializeTypes(params Type[] types)
        {
            foreach (var type in types)
            {
                if (!SplitedPropertyInfos.ContainsKey(type))
                {
                    var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(x => x.CanWrite && x.CanRead &&
                    (x.PropertyType == typeof(double)
                    || x.PropertyType == typeof(Brush)
                    || x.PropertyType == typeof(Transform)
                    || x.PropertyType == typeof(Point)
                    || x.PropertyType == typeof(CornerRadius)
                    || x.PropertyType == typeof(Thickness)
                    || typeof(IInterpolable).IsAssignableFrom(x.PropertyType)
                    ));
                    SplitedPropertyInfos.TryAdd(type, Tuple.Create(new ConcurrentDictionary<string, PropertyInfo>(properties.Where(x => x.PropertyType == typeof(double)).ToDictionary(x => x.Name, x => x)),
                                                          new ConcurrentDictionary<string, PropertyInfo>(properties.Where(x => x.PropertyType == typeof(Brush)).ToDictionary(x => x.Name, x => x)),
                                                          new ConcurrentDictionary<string, PropertyInfo>(properties.Where(x => x.PropertyType == typeof(Transform)).ToDictionary(x => x.Name, x => x)),
                                                          new ConcurrentDictionary<string, PropertyInfo>(properties.Where(x => x.PropertyType == typeof(Point)).ToDictionary(x => x.Name, x => x)),
                                                          new ConcurrentDictionary<string, PropertyInfo>(properties.Where(x => x.PropertyType == typeof(CornerRadius)).ToDictionary(x => x.Name, x => x)),
                                                          new ConcurrentDictionary<string, PropertyInfo>(properties.Where(x => x.PropertyType == typeof(Thickness)).ToDictionary(x => x.Name, x => x)),
                                                          new ConcurrentDictionary<string, PropertyInfo>(properties.Where(x => typeof(IInterpolable).IsAssignableFrom(x.PropertyType)).ToDictionary(x => x.Name, x => x))));
                }
            }
        }
        public static bool TryGetScheduler(object target, out TransitionScheduler? result)
        {
            if (MachinePool.TryGetValue(target, out var scheduler))
            {
                result = scheduler;
                return true;
            }
            result = null;
            return false;
        }
        public TransitionScheduler Copy()
        {
            if (TransitionApplied.TryGetTarget(out var value))
            {
                var result = new TransitionScheduler(value);
                States = [.. result.States];
                return result;
            }
            throw new ArgumentNullException("You attempted to replicate an instance of TransitionScheduler, but the object managed by this TransitionScheduler have already been disposed");
        }

        internal WeakReference<object> TransitionApplied { get; set; }
        internal Type Type { get; set; }
        internal StateCollection States { get; set; } = [];

        internal CancellationTokenSource? tokensource;

        public async void Transition(string stateName, Action<TransitionParams>? paramSetter, List<List<Tuple<PropertyInfo, List<object?>>>>? preload = null)
        {
            Dispose();
            TransitionParams param = new();
            CancellationTokenSource cts = new();
            paramSetter?.Invoke(param);
            tokensource = cts;
            TransitionInterpreter interpreter = new(this, param, cts, States[stateName], preload);
            await interpreter.Start();
        }
        public async void Transition(string stateName, TransitionParams? param, List<List<Tuple<PropertyInfo, List<object?>>>>? preload = null)
        {
            Dispose();
            param ??= TransitionParams.Empty;
            CancellationTokenSource cts = new();
            tokensource = cts;
            TransitionInterpreter interpreter = new(this, param, cts, States[stateName], preload);
            await interpreter.Start();
        }
        public void Dispose()
        {
            var oldsource = Interlocked.Exchange(ref tokensource, null);
            if (oldsource != null)
            {
                oldsource.Cancel();
                oldsource.Dispose();
            }
        }
    }
}
