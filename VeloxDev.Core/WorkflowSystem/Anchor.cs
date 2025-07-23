using System.ComponentModel;
using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.WorkflowSystem
{
    public sealed partial class Anchor(double left = 0d, double top = 0d, int layer = 0)
    {
        [VeloxProperty]
        private double _left = left;
        [VeloxProperty]
        private double _top = top;
        [VeloxProperty]
        private int _layer = layer;

        public override bool Equals(object? obj)
        {
            if (obj is Anchor other)
            {
                return Left == other.Left && Top == other.Top && Layer == other.Layer;
            }
            return false;
        }
        public override int GetHashCode() => HashCode.Combine(Left, Top, Layer);
        public override string ToString() => $"Anchor({Left},{Top},{Layer})";
        public static bool operator ==(Anchor left, Anchor right) => left.Equals(right);
        public static bool operator !=(Anchor left, Anchor right) => !left.Equals(right);
        public static Anchor operator +(Anchor left, Anchor right)
        {
            return new Anchor(left.Left + right.Left, left.Top + right.Top, left.Layer + right.Layer);
        }
        public static Anchor operator -(Anchor left, Anchor right)
        {
            return new Anchor(left.Left - right.Left, left.Top - right.Top, left.Layer - right._layer);
        }
    }
}
