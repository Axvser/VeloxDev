using System.Drawing;
using System.Drawing.Drawing2D;

namespace VeloxDev.WinForms.Xaml;

/// <summary>A two-dimensional affine transform expressed as a GDI+ <see cref="Matrix"/>.</summary>
/// <remarks>
/// The element order follows GDI+: a point maps as <c>x' = x·M11 + y·M21 + OffsetX</c> and
/// <c>y' = x·M12 + y·M22 + OffsetY</c>.
/// </remarks>
public abstract class Transform
{
    /// <summary>The identity transform.</summary>
    public static Transform Identity { get; } = new MatrixTransform();

    /// <summary>The composed matrix of this transform.</summary>
    /// <remarks>Each read returns a new matrix; the caller owns it and should dispose it.</remarks>
    public abstract Matrix Matrix { get; }
}

/// <summary>A translation by <see cref="X"/> and <see cref="Y"/>.</summary>
public sealed class TranslateTransform : Transform
{
    /// <summary>The horizontal distance.</summary>
    public double X { get; set; }

    /// <summary>The vertical distance.</summary>
    public double Y { get; set; }

    /// <inheritdoc />
    public override Matrix Matrix => new System.Drawing.Drawing2D.Matrix(1f, 0f, 0f, 1f, (float)X, (float)Y);
}

/// <summary>A scale about <see cref="CenterX"/>, <see cref="CenterY"/>.</summary>
public sealed class ScaleTransform : Transform
{
    /// <summary>The horizontal scale factor; one by default.</summary>
    public double ScaleX { get; set; } = 1d;

    /// <summary>The vertical scale factor; one by default.</summary>
    public double ScaleY { get; set; } = 1d;

    /// <summary>The horizontal centre of the scale.</summary>
    public double CenterX { get; set; }

    /// <summary>The vertical centre of the scale.</summary>
    public double CenterY { get; set; }

    /// <inheritdoc />
    public override Matrix Matrix => new System.Drawing.Drawing2D.Matrix(
        (float)ScaleX, 0f,
        0f, (float)ScaleY,
        (float)(CenterX - (CenterX * ScaleX)),
        (float)(CenterY - (CenterY * ScaleY)));
}

/// <summary>A clockwise rotation by <see cref="Angle"/> degrees about <see cref="CenterX"/>, <see cref="CenterY"/>.</summary>
public sealed class RotateTransform : Transform
{
    /// <summary>The rotation in degrees.</summary>
    public double Angle { get; set; }

    /// <summary>The horizontal centre of the rotation.</summary>
    public double CenterX { get; set; }

    /// <summary>The vertical centre of the rotation.</summary>
    public double CenterY { get; set; }

    /// <inheritdoc />
    public override Matrix Matrix
    {
        get
        {
            var radians = Angle * Math.PI / 180d;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);
            return new System.Drawing.Drawing2D.Matrix(
                (float)cos, (float)sin,
                (float)-sin, (float)cos,
                (float)(CenterX - ((CenterX * cos) - (CenterY * sin))),
                (float)(CenterY - ((CenterX * sin) + (CenterY * cos))));
        }
    }
}

/// <summary>A transform given directly by its six matrix elements.</summary>
public sealed class MatrixTransform : Transform
{
    /// <summary>The first row, first column element; one by default.</summary>
    public double M11 { get; set; } = 1d;

    /// <summary>The first row, second column element; zero by default.</summary>
    public double M12 { get; set; }

    /// <summary>The second row, first column element; zero by default.</summary>
    public double M21 { get; set; }

    /// <summary>The second row, second column element; one by default.</summary>
    public double M22 { get; set; } = 1d;

    /// <summary>The horizontal translation.</summary>
    public double OffsetX { get; set; }

    /// <summary>The vertical translation.</summary>
    public double OffsetY { get; set; }

    /// <inheritdoc />
    public override Matrix Matrix => new System.Drawing.Drawing2D.Matrix(
        (float)M11, (float)M12,
        (float)M21, (float)M22,
        (float)OffsetX, (float)OffsetY);
}

/// <summary>A sequence of transforms applied in declaration order.</summary>
public sealed class TransformGroup : Transform
{
    /// <summary>The child transforms, applied first to last.</summary>
    public IList<Transform> Children { get; } = new List<Transform>();

    /// <summary>The matrix produced by composing the children; the identity when the group is empty.</summary>
    public Matrix Value => Matrix;

    /// <inheritdoc />
    public override Matrix Matrix
    {
        get
        {
            var result = new Matrix();
            foreach (var child in Children)
            {
                if (child is null)
                {
                    continue;
                }

                using var matrix = child.Matrix;
                result.Multiply(matrix, MatrixOrder.Append);
            }

            return result;
        }
    }
}
