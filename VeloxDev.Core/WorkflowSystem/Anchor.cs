using System.ComponentModel;

namespace VeloxDev.Core.WorkflowSystem
{
    public sealed class Anchor(double left = 0d, double top = 0d, int layer = 0) : INotifyPropertyChanging, INotifyPropertyChanged
    {
        public static readonly Anchor Default = new(0, 0, 0);

        public event PropertyChangingEventHandler? PropertyChanging;
        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanging(string propertyName) => PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));
        public void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private double _left = left;
        private double _top = top;
        private int _layer = layer;

        public double Left
        {
            get => _left;
            set
            {
                if (Equals(_left, value)) return;
                OnPropertyChanging(nameof(Left));
                _left = value;
                OnPropertyChanged(nameof(Left));
            }
        }
        public double Top
        {
            get => _top;
            set
            {
                if (Equals(_top, value)) return;
                OnPropertyChanging(nameof(Top));
                _top = value;
                OnPropertyChanged(nameof(Top));
            }
        }
        public int Layer
        {
            get => _layer;
            set
            {
                if (Equals(_layer, value)) return;
                OnPropertyChanging(nameof(Layer));
                _layer = value;
                OnPropertyChanged(nameof(Layer));
            }
        }

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
