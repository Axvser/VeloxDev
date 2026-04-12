namespace VeloxDev.Core.TransitionSystem
{
    public interface IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps);
    }
}
