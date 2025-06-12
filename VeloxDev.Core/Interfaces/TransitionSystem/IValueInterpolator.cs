namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps);
    }
}
