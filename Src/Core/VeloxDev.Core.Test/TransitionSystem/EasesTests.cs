using VeloxDev.TransitionSystem;

namespace VeloxDev.Core.Test.TransitionSystem;

[TestClass]
public class EasesTests
{
    [TestMethod]
    public void Default_AtZero_ReturnsZero()
    {
        Assert.AreEqual(0d, Eases.Default.Ease(0d), 1e-10);
    }

    [TestMethod]
    public void Default_AtOne_ReturnsOne()
    {
        Assert.AreEqual(1d, Eases.Default.Ease(1d), 1e-10);
    }

    [TestMethod]
    public void Default_AtHalf_ReturnsHalf()
    {
        Assert.AreEqual(0.5, Eases.Default.Ease(0.5), 1e-10);
    }

    [TestMethod]
    [DataRow(typeof(EaseInSine))]
    [DataRow(typeof(EaseOutSine))]
    [DataRow(typeof(EaseInOutSine))]
    [DataRow(typeof(EaseInQuad))]
    [DataRow(typeof(EaseOutQuad))]
    [DataRow(typeof(EaseInOutQuad))]
    [DataRow(typeof(EaseInCubic))]
    [DataRow(typeof(EaseOutCubic))]
    [DataRow(typeof(EaseInOutCubic))]
    [DataRow(typeof(EaseInQuart))]
    [DataRow(typeof(EaseOutQuart))]
    [DataRow(typeof(EaseInOutQuart))]
    [DataRow(typeof(EaseInQuint))]
    [DataRow(typeof(EaseOutQuint))]
    [DataRow(typeof(EaseInOutQuint))]
    [DataRow(typeof(EaseInExpo))]
    [DataRow(typeof(EaseOutExpo))]
    [DataRow(typeof(EaseInOutExpo))]
    [DataRow(typeof(EaseInCirc))]
    [DataRow(typeof(EaseOutCirc))]
    [DataRow(typeof(EaseInOutCirc))]
    public void AllStandardEases_AtBoundaries_ReturnExpected(Type easeType)
    {
        var ease = (IEaseCalculator)Activator.CreateInstance(easeType)!;
        Assert.AreEqual(0d, ease.Ease(0d), 1e-6, $"{easeType.Name} at t=0");
        Assert.AreEqual(1d, ease.Ease(1d), 1e-6, $"{easeType.Name} at t=1");
    }

    [TestMethod]
    public void QuadIn_IsMonotonicallyIncreasing()
    {
        var ease = Eases.Quad.In;
        double prev = 0;
        for (int i = 1; i <= 100; i++)
        {
            double t = i / 100.0;
            double val = ease.Ease(t);
            Assert.IsTrue(val >= prev, $"Not monotonic at t={t}");
            prev = val;
        }
    }

    [TestMethod]
    public void Sine_FactoryProperties_ReturnNonNull()
    {
        Assert.IsNotNull(Eases.Sine.In);
        Assert.IsNotNull(Eases.Sine.Out);
        Assert.IsNotNull(Eases.Sine.InOut);
    }

    [TestMethod]
    public void Back_FactoryProperties_ReturnNonNull()
    {
        Assert.IsNotNull(Eases.Back.In);
        Assert.IsNotNull(Eases.Back.Out);
        Assert.IsNotNull(Eases.Back.InOut);
    }

    [TestMethod]
    public void Elastic_FactoryProperties_ReturnNonNull()
    {
        Assert.IsNotNull(Eases.Elastic.In);
        Assert.IsNotNull(Eases.Elastic.Out);
        Assert.IsNotNull(Eases.Elastic.InOut);
    }

    [TestMethod]
    public void Bounce_FactoryProperties_ReturnNonNull()
    {
        Assert.IsNotNull(Eases.Bounce.In);
        Assert.IsNotNull(Eases.Bounce.Out);
        Assert.IsNotNull(Eases.Bounce.InOut);
    }

    [TestMethod]
    public void InOutQuad_Symmetry_AtHalf()
    {
        var ease = Eases.Quad.InOut;
        Assert.AreEqual(0.5, ease.Ease(0.5), 1e-10);
    }
}
