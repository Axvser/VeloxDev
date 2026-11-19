using System.Linq;
using VeloxDev.Core.WorkflowSystem.CompilerEx;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

/// <summary>
/// The flat outline of a compiled graph: the shape a list view binds when it wants the whole structure visible at
/// once, instead of a nested <c>ItemsControl</c> over <c>Entries</c>.
/// </summary>
[TestClass]
public class CompiledOutlineTests
{
    [TestMethod]
    public void AChain_IsOneRow()
    {
        var first = new ProbeNode("first");
        var second = new ProbeNode("second");
        ProbeGraph.Wire(first, second);

        var rows = CompiledOutline.Of(ProbeGraph.Compile(first));

        Assert.HasCount(1, rows);
        Assert.AreEqual(0, rows[0].Depth);
        Assert.AreEqual("Execute", rows[0].Kind);
        Assert.HasCount(2, rows[0].Nodes, "a chain line names every node in it");
    }

    [TestMethod]
    public void AFanOut_IsAParallelRowWithItsBranchesBeneathIt()
    {
        var source = new ProbeNode("source");
        var a = new ProbeNode("a");
        var b = new ProbeNode("b");
        var join = new ProbeNode("join");
        ProbeGraph.Wire(source, a);
        ProbeGraph.Wire(source, b);
        ProbeGraph.Wire(a, join);
        ProbeGraph.Wire(b, join);

        var rows = CompiledOutline.Of(ProbeGraph.Compile(source));

        Assert.AreEqual("Execute", rows[0].Kind);
        Assert.AreEqual("Parallel", rows[1].Kind);
        Assert.AreEqual(0, rows[1].Depth);
        Assert.AreEqual("2 branches", rows[1].Label);
        Assert.AreEqual(1, rows[2].Depth, "a fan-out's branches sit one level deeper");
        Assert.AreEqual(1, rows[3].Depth);
        Assert.AreEqual("Execute", rows[2].Kind);
        Assert.AreEqual(0, rows[4].Depth, "the join returns to the top level, after the group");
    }

    [TestMethod]
    public void ABranch_NamesItsRouterAndListsItsOptions()
    {
        var router = new RouterNode("router") { Selection = "High" };
        router.RouteTable["High"] = [new ProbeNode("high")];
        router.RouteTable["Low"] = [new ProbeNode("low")];

        var rows = CompiledOutline.Of(ProbeGraph.Compile(router));

        Assert.AreEqual("Branch", rows[0].Kind);
        Assert.AreEqual(0, rows[0].Depth);
        Assert.HasCount(1, rows[0].Nodes, "a branch line names its router");
        StringAssert.Contains(rows[0].Label, "RouterNode");
        StringAssert.Contains(rows[0].Label, "dynamic");
        StringAssert.Contains(rows[0].Label, "2 options");

        var options = rows.Where(r => r.Kind is "Option" or "Terminal").ToList();
        Assert.HasCount(2, options, "one line per route key");
        Assert.IsTrue(options.All(o => o.Depth == 1), "options sit one level deeper than their branch");
        Assert.IsTrue(options.Any(o => o.Label.Contains("'High'")), "each option's label names its key");
    }

    /// <summary>An empty graph is an empty outline, not a throw.</summary>
    [TestMethod]
    public void AnEmptyGraph_HasNoRows()
    {
        Assert.IsEmpty(CompiledOutline.Of(new CompiledGraph()));
    }
}
