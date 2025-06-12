using System.Windows.Media;

namespace VeloxDev.WPF.Tools.SolidColor
{
    /// <summary>
    /// 🖌️ > Use reference types to represent colors
    /// <para>Core</para>
    /// <para>- <see cref="R"/> → Red</para>
    /// <para>- <see cref="G"/> → Green</para>
    /// <para>- <see cref="B"/> → Blue</para>
    /// <para>- <see cref="A"/> → [ Int number ] representation of the Alpha</para>
    /// <para>- <see cref="Opacity"/> → [ Floating-point number ] representation of the Alpha</para>
    /// <para>- <see cref="Brush"/> → Converted to Brush</para>
    /// <para>- <see cref="SolidColorBrush"/> → Converted to SolidColorBrush</para>
    /// <para>- <see cref="Color"/> → Converted to Color</para>
    /// <para>Helper</para>
    /// <para>- <see cref="FromBrush(System.Windows.Media.Brush)"/></para>
    /// <para>- <see cref="FromColor(System.Windows.Media.Color)"/></para>
    /// <para>- <see cref="FromString(string)"/></para>
    /// <para>- <see cref="Interpolate"/></para>
    /// </summary>
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

#if NET
        public int R
        {
            get => r;
            set => r = Math.Clamp(value, 0, 255);
        }
        public int G
        {
            get => g;
            set => g = Math.Clamp(value, 0, 255);
        }
        public int B
        {
            get => b;
            set => b = Math.Clamp(value, 0, 255);
        }
        public int A
        {
            get => a;
            set => a = Math.Clamp(value, 0, 255);
        }
#elif NETFRAMEWORK
        public int R
        {
            get => r;
            set => r = value.Clamp(0, 255);
        }
        public int G
        {
            get => g;
            set => g = value.Clamp(0, 255);
        }
        public int B
        {
            get => b;
            set => b = value.Clamp(0, 255);
        }
        public int A
        {
            get => a;
            set => a = value.Clamp(0, 255);
        }
#endif

        public double Opacity
        {
            get => (double)A / 255;
            set
            {
#if NET
                A = (int)(255 * Math.Clamp(value, 0, 1));
#elif NETFRAMEWORK
                A = (int)(255 * value.Clamp(0, 1));
#endif
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

            var result = new List<object?>();

            if (steps == 0)
            {
                result.Add(end);
                return result;
            }

            for (int i = 0; i < steps; i++)
            {
                double t = (double)i / steps;
                int r = (int)(start.R + (end.R - start.R) * t);
                int g = (int)(start.G + (end.G - start.G) * t);
                int b = (int)(start.B + (end.B - start.B) * t);
                int a = (int)(start.A + (end.A - start.A) * t);
                result.Add(new RGB(r, g, b, a));
            }

            if (result.Count > 0) result[result.Count - 1] = target;

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
    }
}
