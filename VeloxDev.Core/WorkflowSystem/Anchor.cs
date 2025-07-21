namespace VeloxDev.Core.WorkflowSystem
{
    /// <summary>
    /// Describe the position and hierarchy in the view system. Note that the origin is located in the upper left corner
    /// </summary>
    /// <param name="left">Horizontal offset</param>
    /// <param name="top">Vertical offset</param>
    /// <param name="layer">Rendering ZIndex</param>
    public readonly struct Anchor(double left, double top, int layer)
    {
        public static readonly Anchor Default = new(0, 0, 0);
        public double Left { get; } = left;
        public double Top { get; } = top;
        public int Layer { get; } = layer;
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
            return new Anchor(left.Left + right.Left, left.Top + right.Top, left.Layer);
        }
        public static Anchor operator -(Anchor left, Anchor right)
        {
            return new Anchor(left.Left - right.Left, left.Top - right.Top, left.Layer);
        }
    }
}
