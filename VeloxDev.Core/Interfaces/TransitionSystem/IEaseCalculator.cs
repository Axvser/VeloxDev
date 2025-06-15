namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface IEaseCalculator : IEaseCalculatorCore
    {
        public double Ease(double t);
    }

    public interface IEaseCalculatorCore
    {

    }
}
