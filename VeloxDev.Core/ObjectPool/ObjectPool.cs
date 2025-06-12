using System.Collections.Concurrent;
using VeloxDev.Core.Interfaces.ObjectPool;

namespace VeloxDev.Core.ObjectPool
{
    /// <summary>
    /// 🧰 > Provide a simple object pool structure and methods can be rewritten as needed
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ObjectPool<T>() : IObjectPool<T> where T : class
    {
        private static readonly Type _type = typeof(T);

        private readonly ConcurrentQueue<T> _values = [];
        private readonly HashSet<T> _availables = [];
        private readonly object _lock = new();

        public virtual T Acquire(params object?[] values)
        {
            lock (_lock)
            {
                if (_values.TryDequeue(out var value))
                {
                    _availables.Remove(value);
                    return value;
                }
                else
                {
                    return Activator.CreateInstance(_type, values) as T ?? throw new ArgumentException($"An instance of ⌈ {_type.FullName} ⌋ cannot be created.");
                }
            }
        }
        public virtual bool Recycle(T instance)
        {
            lock (_lock)
            {
                if (_availables.Contains(instance)) return false;
                _values.Enqueue(instance);
                _availables.Add(instance);
                return true;
            }
        }
        public virtual void Initialize(int count, params object?[] values)
        {
            lock (_lock)
            {
                for (int i = 0; i < count; i++)
                {
                    var newValue = Activator.CreateInstance(_type, values) as T ?? throw new ArgumentException($"An instance of ⌈ {_type.FullName} ⌋ cannot be created.");
                    _availables.Add(newValue);
                    _values.Enqueue(newValue);
                }
            }
        }
    }
}
