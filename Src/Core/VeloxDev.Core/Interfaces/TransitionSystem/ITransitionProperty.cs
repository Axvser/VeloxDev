using System.Reflection;

namespace VeloxDev.Core.TransitionSystem
{
    public interface ITransitionProperty
    {
        public string Path { get; }
        public Type PropertyType { get; }
        public PropertyInfo PropertyInfo { get; }
        public bool CanRead { get; }
        public bool CanWrite { get; }
        public object? GetValue(object target);
        public bool SetValue(object target, object? value);
    }
}
