namespace VeloxDev.TransitionSystem
{
    /// <summary>An easing curve that maps normalized time to progress.</summary>
    public interface IEaseCalculator
    {
        /// <summary>Maps normalized time <paramref name="t"/> in [0,1] to the eased progress.</summary>
        public double Ease(double t);
    }
}
