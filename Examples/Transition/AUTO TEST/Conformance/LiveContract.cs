namespace VeloxDev.AT.Conformance;

/// <summary>
/// The components of each produced type that the adapter promises to keep inside a range, whatever the eased time is.
/// </summary>
/// <remarks>
/// This is not a restatement of any sampler's arithmetic — it is the library's own contract for the types that carry a
/// meaning the framework will not accept outside of. A colour channel is a byte, an opacity is a fraction, a width
/// cannot be negative; the samplers that produce those are the ones that saturate instead of extrapolating, which is
/// exactly what makes an out-of-range value here a bug rather than an overshoot.
/// <para>
/// Everything else is deliberately absent. A <c>Thickness</c>, a <c>Point</c>, a transform offset and a plane
/// rotation all extrapolate by design: an ease that overshoots its target is supposed to carry them past the endpoint
/// and bring them back, so a value outside <c>[start, end]</c> is the feature working.
/// </para>
/// </remarks>
internal static class LiveContract
{
    /// <summary>A contiguous run of components that share one allowed range.</summary>
    private readonly record struct Range(int First, int Last, double Min, double Max);

    private static readonly Dictionary<string, Range[]> Ranges = new(StringComparer.Ordinal)
    {
        // A=0..3 是字节通道；画笔单独的 Opacity 是一个 0..1 的分数，不在通道里。
        ["Color"] = [new(0, 3, 0d, 255d)],
        ["SolidColorBrush"] = [new(0, 3, 0d, 255d), new(4, 4, 0d, 1d)],

        ["CornerRadius"] = [new(0, 3, 0d, double.PositiveInfinity)],

        ["Size"] = [new(0, 1, 0d, double.PositiveInfinity)],
        ["SizeF"] = [new(0, 1, 0d, double.PositiveInfinity)],
        ["PixelSize"] = [new(0, 1, 0d, double.PositiveInfinity)],

        // 矩形的前两个分量是原点，可以为负；后两个是尺寸。两个单精度矩形的分量序相同，各占一个键：
        // RectF 是 MAUI 那条 live 载荷的标签，RectangleF 是 Core 那条 System.Drawing 单精度矩形。
        ["Rect"] = [new(2, 3, 0d, double.PositiveInfinity)],
        ["RectF"] = [new(2, 3, 0d, double.PositiveInfinity)],
        ["RectangleF"] = [new(2, 3, 0d, double.PositiveInfinity)],
        ["PixelRect"] = [new(2, 3, 0d, double.PositiveInfinity)],

        // 第一个分量是数值，第二个是单位（Pixel/Star 的编号），所以只有第一个有下界。
        ["GridLength"] = [new(0, 0, 0d, double.PositiveInfinity)],
    };

    /// <summary>
    /// The range component <paramref name="index"/> of that type has to stay in, or <c>null</c> when the type
    /// extrapolates and any value is legal.
    /// </summary>
    internal static (double Min, double Max)? Bounds(string typeTag, int index)
    {
        if (!Ranges.TryGetValue(typeTag, out var ranges)) return null;

        foreach (var range in ranges)
        {
            if (index >= range.First && index <= range.Last) return (range.Min, range.Max);
        }

        return null;
    }

    /// <summary>
    /// Whether a component of this type is a quantity — something that can meaningfully be compared across samples.
    /// </summary>
    /// <remarks>
    /// Colour channels, offsets and sizes are. A <c>String</c> is not: the payload carries its character code points,
    /// which is a lossless encoding rather than a vector of values — the sequence is a different length at different
    /// points of the run, and "did this component's minimum equal its maximum" has no answer. Checks that read the
    /// envelope therefore skip this type rather than reporting an artifact of the encoding as a defect.
    /// </remarks>
    internal static bool ComponentsAreQuantities(string typeTag) => typeTag != "String";
}
