namespace VeloxDev.AT.Drivers;

/// <summary>
/// The overshoot scenarios every demo's strip exposes.
/// </summary>
/// <remarks>
/// The set is fixed by the demos rather than by the suites: a platform that cannot run one of these is a finding, not
/// a configuration. That is why there is no "supported scenarios" property on a driver — the scenario table is the
/// contract.
/// </remarks>
internal enum ScenarioId
{
    /// <summary>A translate on X under <c>Back.Out</c>.</summary>
    Back,

    /// <summary>A translate on X under <c>Elastic.Out</c>.</summary>
    Elastic,

    /// <summary>A solid fill under <c>Back.Out</c>.</summary>
    Color,

    /// <summary>A width under <c>Elastic.Out</c>.</summary>
    Size,

    /// <summary>A non-solid fill, which takes the blended-brush path and cannot overshoot.</summary>
    Brush,
}
