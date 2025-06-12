namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface IInterpolable
    {
        public List<object?> Interpolate(object? start, object? end, int steps);
    }
}
