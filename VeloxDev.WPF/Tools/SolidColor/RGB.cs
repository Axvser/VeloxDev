using System.Windows.Media;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.WPF.Tools.SolidColor
{
    public class RGB : ICloneable, IInterpolable
    {
        public RGB() { }
        public RGB(int r, int g, int b)
        {
            R = r;
            G = g;
            B = b;
        }
        public RGB(int r, int g, int b, int a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        private int r = 0;
        private int g = 0;
        private int b = 0;
        private int a = 255;

        public static RGB Empty { get; private set; } = new(0, 0, 0, 0);

        public int R
        {
            get => r;
            set => r = Clamp(value, 0, 255);
        }
        public int G
        {
            get => g;
            set => g = Clamp(value, 0, 255);
        }
        public int B
        {
            get => b;
            set => b = Clamp(value, 0, 255);
        }
        public int A
        {
            get => a;
            set => a = Clamp(value, 0, 255);
        }

        public double Opacity
        {
            get => (double)A / 255;
            set
            {
                A = (int)(255 * Clamp(value, 0, 1));
            }
        }

        public Color Color => Color.FromArgb((byte)A, (byte)R, (byte)G, (byte)B);
        public SolidColorBrush SolidColorBrush => new(Color);
        public Brush Brush => SolidColorBrush;

        public static RGB FromString(string color)
        {
            var original = (Color)ColorConverter.ConvertFromString(color);
            return new RGB(original.R, original.G, original.B, original.A);
        }
        public static RGB FromColor(Color color)
        {
            return new RGB(color.R, color.G, color.B, color.A);
        }
        public static RGB FromBrush(Brush brush)
        {
            var color = (Color)ColorConverter.ConvertFromString(brush.ToString());
            return new RGB(color.R, color.G, color.B, color.A);
        }

        public override string ToString()
        {
            return Color.ToString();
        }
        public override bool Equals(object? obj)
        {
            if (obj == null) return false;
            if (obj is RGB rgb) return rgb.R == R && rgb.G == G && rgb.B == B && rgb.A == A;
            if (obj is Color color) return color.R == R && color.G == G && color.B == B && color.A == A;
            if (obj is Brush brush) return Equals(FromBrush(brush));
            if (obj is string text) return Equals(FromString(text));
            return false;
        }

        public List<object?> Interpolate(object? current, object? target, int steps)
        {
            if (current is not RGB start || target is not RGB end)
            {
                throw new ArgumentException("Both current and target must be of type RGB");
            }

            if (steps == 1)
            {
                return [end];
            }

            var result = new List<object?>();

            for (int i = 0; i < steps; i++)
            {
                double t = (double)i / steps;
                int r = (int)(start.R + (end.R - start.R) * t);
                int g = (int)(start.G + (end.G - start.G) * t);
                int b = (int)(start.B + (end.B - start.B) * t);
                int a = (int)(start.A + (end.A - start.A) * t);
                result.Add(new RGB(r, g, b, a));
            }

            result[0] = start;
            result[steps - 1] = end;

            return result;
        }

        public object Clone()
        {
            return new RGB(R, G, B, A);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(R, G, B, A);
        }

        private static double Clamp(double value, double minus, double max)
        {
            if (max < minus) throw new InvalidOperationException();
            if (value < minus) return minus;
            if (value > max) return max;
            return value;
        }
        private static int Clamp(int value, int minus, int max)
        {
            if (max < minus) throw new InvalidOperationException();
            if (value < minus) return minus;
            if (value > max) return max;
            return value;
        }
    }
}
