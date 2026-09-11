namespace VeloxDev.AT.Conformance;

/// <summary>
/// One sampler, plus what it must produce at any eased time.
/// </summary>
/// <remarks>
/// The expectation is a component vector rather than a formatted string, so the comparison is numeric and a
/// last-bit difference from two separately compiled copies of the same arithmetic cannot fail the suite. It is still
/// an exact statement of the rule: the same <c>a + (b - a) * t</c>, written here rather than taken from the sampler.
/// </remarks>
/// <param name="Sampler">The sampler's type name — the key the demo's payload is matched by.</param>
/// <param name="TypeTag">
/// The type name the produced value must have. Checked so a sampler that starts producing something else is reported
/// as that, rather than as a pile of component mismatches.
/// </param>
/// <param name="Expected">The components the produced value must have, in the order the demo serialises them.</param>
internal sealed record ConformanceEntry(string Sampler, string TypeTag, Func<double, double[]> Expected);
