using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.WorkflowSystem
{
    public sealed partial class Size(double width = double.NaN, double height = double.NaN)
    {
        [VeloxProperty]
        private double _width = width;
        [VeloxProperty]
        private double _height = height;
    }
}
