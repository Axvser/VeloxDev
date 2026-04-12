namespace VeloxDev.Core.TransitionSystem
{
    public interface IInterpolable
    {
        public List<object?> Interpolate(object? start, object? end, int steps);
    }
}
