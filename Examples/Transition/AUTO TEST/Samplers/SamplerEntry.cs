using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.SamplerTest;

/// <summary>What a sampler does with an eased time that leaves the unit interval.</summary>
internal enum SamplerRule
{
    /// <summary>The value continues past its endpoint: <c>start + (end - start) * t</c> for any t.</summary>
    Extrapolate,

    /// <summary>The value moves with the eased time but stops at a limit its type cannot represent.</summary>
    Saturate,

    /// <summary>The value switches at the endpoint instead of moving, because it cannot be interpolated at all.</summary>
    Discrete,
}

/// <summary>
/// One sampler, plus the closed form it has to satisfy at any eased time.
/// </summary>
/// <remarks>
/// The entry carries both halves on purpose: <see cref="Write"/> runs one frame on a real target and reads the
/// value back, and <see cref="Expected"/> states what that value must be. A test therefore compares a sampler
/// against a statement written independently of it, rather than against whatever it happens to produce.
/// </remarks>
internal sealed class SamplerEntry
{
    /// <summary>The sampler's type, which is also what the coverage check matches against reflection.</summary>
    internal required Type SamplerType { get; init; }

    /// <summary>Which adapter the sampler belongs to, for the failure messages.</summary>
    internal required string Adapter { get; init; }

    /// <summary>Which of the three rules this sampler follows.</summary>
    internal required SamplerRule Rule { get; init; }

    /// <summary>
    /// Builds an instance to check. Defaults to the parameterless constructor, which is how every sampler a host
    /// registers by name is created; one whose endpoints are baked into a per-animation constructor supplies its own.
    /// </summary>
    internal Func<ISampler>? Create { get; init; }

    /// <summary>Produces the instance every <see cref="Write"/> call for this entry runs on.</summary>
    internal ISampler Instantiate()
        => Create is { } factory ? factory() : (ISampler)Activator.CreateInstance(SamplerType)!;

    /// <summary>Runs one frame at <paramref name="t"/> and returns the value the sampler wrote.</summary>
    internal required Func<ISampler, double, object?> Write { get; init; }

    /// <summary>The value the sampler must write at <paramref name="t"/>, derived from the rule rather than recorded.</summary>
    internal required Func<double, object?> Expected { get; init; }

    /// <summary>
    /// How the produced value is compared with the expected one. Defaults to exact equality, which is what every
    /// entry wants when its closed form is the same arithmetic the sampler runs; an entry whose form is written
    /// differently supplies its own.
    /// </summary>
    internal Func<object?, object?, bool> Equivalent { get; init; } = static (expected, actual) => object.Equals(expected, actual);

    /// <summary>The times every entry is checked at. All are exactly representable in binary, so the comparison is equality rather than a tolerance.</summary>
    internal static readonly double[] Times = [0d, 0.5d, 1d, 1.5d, -0.5d];

    public override string ToString() => $"{Adapter}/{SamplerType.Name}";
}
