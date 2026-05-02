namespace VeloxDev.TransitionSystem
{
    public interface IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps, object? options = null);
    }
}
