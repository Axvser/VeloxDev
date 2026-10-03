namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Where a node card puts its ports, in design (scale-1) coordinates.
/// </summary>
/// <remarks>
/// <para>
/// The card is drawn at its design size inside a viewbox scaled to the collapsed box, so port positions are design
/// coordinates that the surface and the link views scale by <c>node.Size / DesignSize</c>.
/// </para>
/// <para>
/// These are the host's design, not platform machinery — which is why they are values here rather than constants.
/// The defaults match the shared item template's card.
/// </para>
/// </remarks>
public sealed class WorkflowPortLayout
{
    /// <summary>Card width at scale 1.</summary>
    public double DesignWidth { get; set; } = 260;

    /// <summary>Card height at scale 1.</summary>
    public double DesignHeight { get; set; } = 180;

    /// <summary>Height of the title band at the top of the card.</summary>
    public double TitleBarH { get; set; } = 36;

    /// <summary>Height of one output row.</summary>
    public double RowH { get; set; } = 26;

    /// <summary>Distance from the card's left edge to the centre of the input port.</summary>
    public double InputPortX { get; set; } = 10;

    /// <summary>Distance from the card's right edge to the centre of an output port.</summary>
    public double OutputInset { get; set; } = 15;

    /// <summary>Radius of the input port's glyph.</summary>
    public double InputPortRadius { get; set; } = 9;

    /// <summary>Radius of an output port's glyph.</summary>
    public double OutputPortRadius { get; set; } = 7;
}
