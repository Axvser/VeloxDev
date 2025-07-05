namespace VeloxDev.Core.Interfaces.Theme
{
    public interface IValueConstructor
    {
        public object? Construct(params object?[] paramArray);
    }
}
