using System.Collections.Concurrent;
using System.Linq.Expressions;
using VeloxDev.Core.Interfaces.Theme;
using VeloxDev.Core.Interfaces.TransitionSystem;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.Core.ThemeSystem
{
    public abstract class ThemeManagerCore
    {
        public static ConcurrentDictionary<Type, IValueConstructor> Constructors { get; protected set; } = [];
        public static object? TryConstructValue(Type type, params object?[] paramArray)
        {
            if (TryGetConstructor(type, out var constructor))
            {
                return constructor?.Construct(paramArray);
            }
            else
            {
                return Activator.CreateInstance(type, paramArray);
            }
        }
        public static bool TryGetConstructor(Type type, out IValueConstructor? constructor)
        {
            if (Constructors.TryGetValue(type, out constructor))
            {
                return true;
            }
            constructor = null;
            return false;
        }
        public static bool RegisterConstructor(Type type, IValueConstructor constructor)
        {
            if (Constructors.TryGetValue(type, out var oldValue))
            {
                return Constructors.TryUpdate(type, constructor, oldValue);
            }
            else
            {
                return Constructors.TryAdd(type, constructor);
            }
        }
        public static bool RemoveConstructor(Type type, out IValueConstructor? constructor)
        {
            return Constructors.TryRemove(type, out constructor);
        }
    }
}
