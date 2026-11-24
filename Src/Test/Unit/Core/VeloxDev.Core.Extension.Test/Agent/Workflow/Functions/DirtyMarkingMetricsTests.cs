using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// Dirty marking is unconditional: a read-only tool call marks the tree exactly like a mutation does. The helper's
/// raw-request counter is what makes that visible without depending on the tick's coalescing.
/// </summary>
[TestClass]
public class DirtyMarkingMetricsTests
{
    [TestMethod]
    public void AQueryToolCountsAsADirtyRequest()
    {
        var tree = new TreeDefaultViewModel();
        var helper = new TreeHelper(200d);
        tree.SetHelper(helper);

        try
        {
            var scope = new WorkflowAgentScope(tree);

            WorkflowToolInvoker.Invoke(scope, "ListNodes");

            Assert.AreEqual(
                1,
                helper.DirtyRequestCount,
                "a read-only tool call marks the tree dirty, and the helper counts the raw request");
        }
        finally
        {
            helper.Uninstall(tree);
        }
    }
}
