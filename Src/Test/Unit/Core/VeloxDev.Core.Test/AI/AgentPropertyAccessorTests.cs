using VeloxDev.AI;

namespace VeloxDev.Core.Test.AI;

[TestClass]
public class AgentPropertyAccessorTests
{
    /// <summary>
    /// The tree carries only types an author opted into, so the class annotation is what puts this one in it.
    /// </summary>
    [AgentContext(AgentLanguages.English, "A sample target")]
    internal sealed class SampleTarget
    {
        public string? Name { get; set; } = "Initial";
        public int Count { get; set; } = 5;
        public double ReadOnly => 3.14;
    }

    /// <summary>Nothing opts this type in, so the tree has no entry for it — the reverse control.</summary>
    internal sealed class UnannotatedTarget
    {
        public string? Name { get; set; }
    }

    [AgentContext(AgentLanguages.English, "A base target")]
    internal class BaseTarget
    {
        public string? Inherited { get; set; } = "from base";
    }

    /// <summary>A derived type carries its own entry, and reaches the base's members through the tree's base link.</summary>
    [AgentContext(AgentLanguages.English, "A derived target")]
    internal sealed class DerivedTarget : BaseTarget
    {
        public int Own { get; set; } = 1;
    }

    /// <summary>
    /// One property, annotated in English only — the shape a Chinese- or Japanese-language agent meets
    /// whenever a member has not been translated yet.
    /// </summary>
    internal sealed class AnnotatedTarget
    {
        public int Untouched { get; set; }

        [AgentContext(AgentLanguages.English, "What this object is called")]
        public string? Name { get; set; }
    }

    [TestMethod]
    public void DiscoverProperties_UntranslatedLanguage_FallsBackToEnglish()
    {
        var target = new AnnotatedTarget();
        var props = AgentPropertyAccessor.DiscoverProperties(target, AgentLanguages.Japanese);

        var name = props.First(p => p.Name == "Name");
        CollectionAssert.Contains((System.Collections.ICollection)name.AgentDescriptions, "What this object is called");
    }

    [TestMethod]
    public void DiscoverProperties_UnannotatedProperty_StaysUndescribed()
    {
        // The fallback is per target, not a blanket: a member nobody annotated has nothing to fall back to.
        var target = new AnnotatedTarget();
        var props = AgentPropertyAccessor.DiscoverProperties(target, AgentLanguages.Japanese);

        var untouched = props.First(p => p.Name == "Untouched");
        Assert.AreEqual(0, untouched.AgentDescriptions.Count);
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
        var props = AgentPropertyAccessor.DiscoverProperties(target, filter: name => name != "Count");
        Assert.IsFalse(props.Any(p => p.Name == "Count"));
    }

    [TestMethod]
    public void DiscoverProperties_TypeOutsideTheTree_ReturnsEmpty()
    {
        // 反向对照：目录是唯一事实源。没被标注过的类型不进目录，于是既列不出属性，也读不出值。
        var target = new UnannotatedTarget { Name = "Hidden" };

        Assert.AreEqual(0, AgentPropertyAccessor.DiscoverProperties(target).Count);
        Assert.IsNull(AgentPropertyAccessor.GetPropertyValue(target, "Name"));
        Assert.IsFalse(AgentPropertyAccessor.SetPropertyValue(target, "Name", "Written").Success);
    }

    [TestMethod]
    public void DiscoverProperties_WalksTheBaseChain()
    {
        // 目录只录类型自己声明的成员，继承来的靠 BaseType 链接过去 —— 派生类型的属性面不能因此变窄。
        var target = new DerivedTarget { Inherited = "reached" };
        var props = AgentPropertyAccessor.DiscoverProperties(target);

        Assert.IsTrue(props.Any(p => p.Name == "Own"));
        Assert.IsTrue(props.Any(p => p.Name == "Inherited"), "a base type's property must still be discoverable");

        // 读也要能走通：基类成员由基类那个访问器执行，派生类型自己的访问器里没有它的 case。
        Assert.AreEqual("reached", AgentPropertyAccessor.GetPropertyValue(target, "Inherited"));

        var set = AgentPropertyAccessor.SetPropertyValue(target, "Inherited", "written");
        Assert.IsTrue(set.Success, set.Error);
        Assert.AreEqual("written", target.Inherited);
    }

    [TestMethod]
    public void PropertyDescriptor_CarriesTheDeclaredTypeAsATextName()
    {
        // 目录里存的是声明类型的全名，描述符不再发放 Type —— 那正是裁剪器跟不上的东西。
        var props = AgentPropertyAccessor.DiscoverProperties(new SampleTarget());
        Assert.AreEqual("System.Int32", props.First(p => p.Name == "Count").PropertyType);
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
