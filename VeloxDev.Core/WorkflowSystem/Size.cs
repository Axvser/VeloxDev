using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.WorkflowSystem
{
    public sealed partial class Size(double width = double.NaN, double height = double.NaN)
    {
        [VeloxProperty]
        private double _width = width;
        [VeloxProperty]
        private double _height = height;

        public override bool Equals(object? obj)
        {
            if (obj is Size size)
            {
                return Width == size.Width && Height == size.Height;
            }
            return false;
        }
        public override int GetHashCode() => HashCode.Combine(Width, Height);
        public override string ToString() => $"Size({Width},{Height})";
        public static bool operator ==(Size a, Size b) => a.Equals(b);
        public static bool operator !=(Size a, Size b) => !a.Equals(b);
        public static Size operator +(Size a, Size b) => new(a.Width + b.Width, a.Height + b.Height);
        public static Size operator -(Size a, Size b) => new(a.Width - b.Width, a.Height - b.Height);
    }
}
