using Newtonsoft.Json.Linq;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// What the patcher refuses, and why it says so.
/// </summary>
/// <remarks>
/// The refusals are the load-bearing part: every one of them routes a change away from a direct property write.
/// They now come from the context tree's flags rather than from reflected metadata, so these pin that the
/// classification still lands on the same properties.
/// </remarks>
[TestClass]
public class ComponentPatcherTests
{
    private static JObject Patch(object target, string json)
        => JObject.Parse(ComponentPatcher.ApplyPatch(target, json));

    private static JObject DetailFor(JObject result, string property)
        => result["details"]!.Children<JObject>().First(d => d["property"]?.Value<string>() == property);

    [TestMethod]
    public void AnUnmountedComponentIsRefusedBeforeAnythingIsWritten()
    {
        var result = Patch(new NodeDefaultViewModel(), """{"State":"X"}""");

        Assert.AreEqual("error", result["status"]?.Value<string>());
        StringAssert.Contains(result["message"]?.Value<string>() ?? string.Empty, "not mounted");
    }

    [TestMethod]
    public void AnUnknownPropertyIsSkippedRatherThanGuessedAt()
    {
        var result = Patch(new TreeDefaultViewModel(), """{"NoSuchThing":1}""");

        var detail = DetailFor(result, "NoSuchThing");
        Assert.AreEqual("skipped", detail["status"]?.Value<string>());
        Assert.AreEqual("not found", detail["reason"]?.Value<string>());
    }

    [TestMethod]
    public void FrameworkManagedPropertiesAreRejected()
    {
        var result = Patch(new TreeDefaultViewModel(), """{"Nodes":[]}""");

        var detail = DetailFor(result, "Nodes");
        Assert.AreEqual("rejected", detail["status"]?.Value<string>());
        StringAssert.Contains(detail["reason"]?.Value<string>() ?? string.Empty, "framework-managed");
    }

    [TestMethod]
    public void ACommandBackedPropertyIsRejectedInFavourOfItsCommand()
    {
        // Anchor 有 SetAnchorCommand —— patcher 必须把改动让给命令管线，而不是直接写属性。
        // 挂上去才能过 ResolveTree 那一关，而这一条本身也是被测行为之一。
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(node);

        var detail = DetailFor(Patch(node, """{"Anchor":{"Horizontal":1.0}}"""), "Anchor");
        Assert.AreEqual("rejected", detail["status"]?.Value<string>());
        StringAssert.Contains(detail["reason"]?.Value<string>() ?? string.Empty, "SetAnchorCommand");
    }

    [TestMethod]
    public void APropertyWithoutABackingCommandIsWrittenThroughTheAccessor()
    {
        var target = new TreeDefaultViewModel();

        // Layout 是 [VeloxProperty] 提升出来的属性，没有 SetLayoutCommand —— 直接写这条路是通的。
        // 值按访问器的 typeof 字面量给出的目标类型读回（经 AgentJsonValue，枚举那一类也认）。
        var result = Patch(target, """{"Layout":{"Scale":{"Horizontal":2.0,"Vertical":3.0}}}""");

        var detail = DetailFor(result, "Layout");
        Assert.AreEqual("ok", detail["status"]?.Value<string>(), detail["reason"]?.Value<string>());
        Assert.AreEqual(2.0, target.Layout.Scale.Horizontal);
        Assert.AreEqual(3.0, target.Layout.Scale.Vertical);
    }
}
