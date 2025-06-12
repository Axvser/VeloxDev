namespace VeloxDev.Core.Interfaces.ObjectPool
{
    public interface IObjectPool<T> where T : class
    {
        public T Acquire(params object?[] values);
        public bool Recycle(T target);
    }
}
