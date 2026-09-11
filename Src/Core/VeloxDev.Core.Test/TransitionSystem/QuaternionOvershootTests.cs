using System.Numerics;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;
using VeloxDev.TransitionSystem.NativeSamplers;

namespace VeloxDev.Core.Test.TransitionSystem;

/// <summary>
/// Why the quaternion sampler can take an overshooting eased time without normalising, and why it still needs the
/// exact-endpoint guard. Both facts were measured rather than assumed — <c>Quaternion.Slerp</c> does not clamp, and
/// its behaviour outside [0,1] is not the obvious one.
/// </summary>
[TestClass]
public class QuaternionOvershootTests
{
    private sealed class Target
    {
        public Quaternion Value { get; set; }
    }

    private static ITransitionProperty Property
        => TransitionProperty.FromProperty(typeof(Target).GetProperty(nameof(Target.Value))!);

    [TestMethod]
    public void Slerp_KeepsUnitLength_OutsideTheUnitInterval()
    {
        // Slerp is the great-circle parameterisation, (sin((1-t)w) q1 + sin(tw) q2) / sin w, whose length is 1 for
        // every t — it extrapolates along the arc rather than leaving the sphere. So an overshoot on a quaternion
        // introduces no scale, and the sampler needs no normalisation step.
        var q1 = Quaternion.Identity;
        var q2 = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.0f);

        foreach (var t in new[] { 1f, 1.3731f, -0.3731f })
        {
            Assert.AreEqual(1f, Quaternion.Slerp(q1, q2, t).Length(), 1e-6f, $"t={t}");
        }
    }

    [TestMethod]
    public void Slerp_DoesNotClamp_AndIsNotExactAtItsOwnEndpoint()
    {
        // Two facts at once, both load-bearing:
        //  - t beyond 1 produces a different rotation than t == 1, so there is no hidden clamp suppressing overshoot;
        //  - Slerp(q1, q2, 1) is NOT bit-exact in the general branch (two float roundings), which is exactly why the
        //    sampler short-circuits the exact endpoints instead of computing them.
        var q1 = Quaternion.Identity;
        var q2 = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.0f);

        Assert.AreNotEqual(Quaternion.Slerp(q1, q2, 1f), Quaternion.Slerp(q1, q2, 1.3731f));
        Assert.AreNotEqual(q2, Quaternion.Slerp(q1, q2, 1f));
    }

    [TestMethod]
    public void Sampler_AtTheExactEndpoint_WritesTheCallersOwnEndValue()
    {
        // The guard is what supplies the bit-exactness Slerp cannot, so the endpoint lands on the caller's value.
        var target = new Target();
        var sampler = new QuaternionSampler();
        object? working = null;

        var start = Quaternion.Identity;
        var end = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.0f);

        sampler.InsertFrame(target, Property, ref working, start, end, null, 1d);
        Assert.AreEqual(end, target.Value);
    }

    [TestMethod]
    public void Sampler_Overshoot_ExtrapolatesAlongTheArc()
    {
        // Past the endpoint the rotation keeps going rather than pinning — the point of the whole change.
        var target = new Target();
        var sampler = new QuaternionSampler();
        object? working = null;

        var start = Quaternion.Identity;
        var end = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.0f);

        sampler.InsertFrame(target, Property, ref working, start, end, null, 1.5d);

        var beyond = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.5f);
        Assert.AreEqual(beyond.X, target.Value.X, 1e-5f);
        Assert.AreEqual(beyond.Y, target.Value.Y, 1e-5f);
        Assert.AreEqual(beyond.Z, target.Value.Z, 1e-5f);
        Assert.AreEqual(beyond.W, target.Value.W, 1e-5f);
    }
}
