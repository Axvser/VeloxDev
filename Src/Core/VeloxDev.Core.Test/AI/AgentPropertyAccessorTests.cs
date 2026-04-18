using VeloxDev.AI;

namespace VeloxDev.Core.Test.AI;

[TestClass]
public class AgentPropertyAccessorTests
{
    private sealed class SampleTarget
    {
        public string? Name { get; set; } = "Initial";
        public int Count { get; set; } = 5;
        public double ReadOnly => 3.14;
    }

    [TestMethod]
    public void DiscoverProperties_ReturnsAll()
    {
        var target = new SampleTarget();
        var props = AgentPropertyAccessor.DiscoverProperties(target);
        Assert.IsTrue(props.Count >= 3);
    }

    [TestMethod]
    public void DiscoverProperties_WithValues_IncludesCurrent()
    {
        var target = new SampleTarget { Name = "Test" };
        var props = AgentPropertyAccessor.DiscoverProperties(target, includeValues: true);
        var nameProp = props.First(p => p.Name == "Name");
        Assert.AreEqual("Test", nameProp.CurrentValue);
    }

    [TestMethod]
    public void DiscoverProperties_WithFilter_ExcludesFiltered()
    {
        var target = new SampleTarget();
        var props = AgentPropertyAccessor.DiscoverProperties(target, filter: p => p.Name != "Count");
        Assert.IsFalse(props.Any(p => p.Name == "Count"));
    }

    [TestMethod]
    public void DiscoverProperties_NullTarget_ReturnsEmpty()
    {
        var props = AgentPropertyAccessor.DiscoverProperties(null!);
        Assert.AreEqual(0, props.Count);
    }

    [TestMethod]
    public void GetPropertyValue_ExistingProp_ReturnsValue()
    {
        var target = new SampleTarget { Count = 42 };
        var val = AgentPropertyAccessor.GetPropertyValue(target, "Count");
        Assert.AreEqual(42, val);
    }

    [TestMethod]
    public void GetPropertyValue_NonExistent_ReturnsNull()
    {
        var target = new SampleTarget();
        Assert.IsNull(AgentPropertyAccessor.GetPropertyValue(target, "Ghost"));
    }

    [TestMethod]
    public void GetPropertyValue_NullTarget_ReturnsNull()
    {
        Assert.IsNull(AgentPropertyAccessor.GetPropertyValue(null!, "Name"));
    }

    [TestMethod]
    public void SetPropertyValue_WritableProp_Succeeds()
    {
        var target = new SampleTarget();
        var result = AgentPropertyAccessor.SetPropertyValue(target, "Name", "Updated");
        Assert.IsTrue(result.Success);
        Assert.AreEqual("Updated", target.Name);
    }

    [TestMethod]
    public void SetPropertyValue_ReadOnlyProp_Fails()
    {
        var target = new SampleTarget();
        var result = AgentPropertyAccessor.SetPropertyValue(target, "ReadOnly", 0.0);
        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public void SetPropertyValue_NonExistent_Fails()
    {
        var target = new SampleTarget();
        var result = AgentPropertyAccessor.SetPropertyValue(target, "Ghost", null);
        Assert.IsFalse(result.Success);
    }

    [TestMethod]
    public void SetPropertyValue_NullTarget_Fails()
    {
        var result = AgentPropertyAccessor.SetPropertyValue(null!, "Name", null);
        Assert.IsFalse(result.Success);
    }

    [TestMethod]
    public void SetProperties_MultiplePatch_ReturnsResults()
    {
        var target = new SampleTarget();
        var dict = new Dictionary<string, object?> { ["Name"] = "Patched", ["Count"] = 99 };
        var results = AgentPropertyAccessor.SetProperties(target, dict);
        Assert.AreEqual(2, results.Count);
        Assert.IsTrue(results.All(r => r.Success));
        Assert.AreEqual("Patched", target.Name);
        Assert.AreEqual(99, target.Count);
    }

    [TestMethod]
    public void SetProperties_WithRejected_RejectsThose()
    {
        var target = new SampleTarget();
        var rejected = new HashSet<string> { "Count" };
        var dict = new Dictionary<string, object?> { ["Name"] = "Ok", ["Count"] = 0 };
        var results = AgentPropertyAccessor.SetProperties(target, dict, rejected);

        var nameResult = results.First(r => r.PropertyName == "Name");
        var countResult = results.First(r => r.PropertyName == "Count");
        Assert.IsTrue(nameResult.Success);
        Assert.IsFalse(countResult.Success);
        Assert.AreEqual(5, target.Count); // unchanged
    }

    [TestMethod]
    public void CopyScalarProperties_CopiesValues()
    {
        var source = new SampleTarget { Name = "Source", Count = 77 };
        var dest = new SampleTarget();

        AgentPropertyAccessor.CopyScalarProperties(source, dest);
        Assert.AreEqual("Source", dest.Name);
        Assert.AreEqual(77, dest.Count);
    }
}
