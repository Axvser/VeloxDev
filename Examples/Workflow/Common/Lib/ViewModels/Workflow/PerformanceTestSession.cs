using Demo.ViewModels;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VeloxDev.WorkflowSystem;

namespace Demo.Workflow;

/// <summary>
/// Generates a large workflow graph (1000 nodes with random connections) for
/// testing virtualization performance.  Nodes are scattered across a
/// 100000 x 100000 canvas so that only a small fraction are ever visible
/// at typical zoom levels.
/// </summary>
public sealed class PerformanceTestSession
{
    private PerformanceTestSession(TreeViewModel tree)
    {
        Tree = tree;
    }

    public TreeViewModel Tree { get; }

    /// <summary>
    /// Creates a 1000-node randomly-connected workflow graph.
    /// </summary>
    /// <param name="seed">Random seed for deterministic reproduction.</param>
    /// <param name="nodeCount">Number of nodes to generate (default 1000).</param>
    /// <param name="canvasSize">Canvas dimension in logical units. Default 8000 is
    /// large enough that only ~20-30 nodes are visible in a normal viewport, but
    /// keeps nodes at a readable size without aggressive auto-scaling.</param>
    /// <param name="maxLinksPerNode">Maximum outgoing links from each node (default 3).</param>
    public static PerformanceTestSession Create(
        int seed = 42,
        int nodeCount = 1000,
        double canvasSize = 8_000,
        int maxLinksPerNode = 3)
    {
        var logPath = Path.Combine(Path.GetTempPath(), "VeloxDev_PerfTest_Diag.log");
        using var log = new StreamWriter(logPath, append: false) { AutoFlush = true };

        log.WriteLine($"=== PerformanceTestSession.Create started at {DateTime.Now:O} ===");
        log.WriteLine($"seed={seed}, nodeCount={nodeCount}, canvasSize={canvasSize}, maxLinksPerNode={maxLinksPerNode}");
        log.WriteLine();

        var rng = new Random(seed);
        var tree = new TreeViewModel();
        tree.Layout.OriginSize = new Size(canvasSize, canvasSize);

        var helper = tree.GetHelper();
        var nodeSize = new Size(200, 160);
        const double margin = 100;
        var range = canvasSize - margin * 2 - nodeSize.Width;

        // ---- Step 1: create all nodes ----
        log.WriteLine("=== Step 1: Creating nodes ===");
        var nodes = new NodeViewModel[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            double left = margin + rng.NextDouble() * range;
            double top = margin + rng.NextDouble() * range;

            var node = new NodeViewModel
            {
                Title = $"Node {i + 1}",
                DelayMilliseconds = rng.Next(100, 3000),
                Size = nodeSize,
                Anchor = new Anchor(left, top, 0),
                CompilePriority = rng.Next(5),
            };

            helper.CreateNode(node);

            // Log Size + Anchor for first 3 nodes to confirm they're set
            if (i < 3)
                log.WriteLine($"N[{i}] Size=({node.Size.Width},{node.Size.Height}) Anchor=({node.Anchor.Horizontal},{node.Anchor.Vertical})");

            // Detect abnormal Slots collections during creation
            if (node.Slots.Count != 2 || node.Slots.Any(s => s is null))
            {
                var slotInfos = string.Join(", ", node.Slots.Select((s, si) =>
                    $"s[{si}]: HC={s?.GetHashCode()}{(s is null ? " NULL!" : $" P={(s.Parent is null ? "null" : (s.Parent == node ? "ok" : "OTHER!"))}")}"));
                log.WriteLine($"N[{i}] Slots({node.Slots.Count}): {slotInfos} <<<");
            }

            nodes[i] = node;
        }

        // ---- Step 1.5: sanitize Slots collections (remove nulls and duplicate references) ----
        int fixedNodes = 0;
        for (int i = 0; i < nodeCount; i++)
        {
            var slotList = nodes[i].Slots;
            bool changed = false;

            // Remove null entries
            for (int si = slotList.Count - 1; si >= 0; si--)
            {
                if (slotList[si] is null)
                {
                    slotList.RemoveAt(si);
                    changed = true;
                }
            }

            // Remove duplicate references (keep first occurrence)
            var seen = new HashSet<IWorkflowSlotViewModel>(new RefEq<IWorkflowSlotViewModel>());
            for (int si = 0; si < slotList.Count; )
            {
                if (!seen.Add(slotList[si]))
                {
                    slotList.RemoveAt(si); // duplicate, remove
                    changed = true;
                }
                else
                {
                    si++;
                }
            }

            if (changed)
            {
                fixedNodes++;
                log.WriteLine($"Sanitized N[{i}] Slots → {slotList.Count} unique entries");
            }
        }
        log.WriteLine($"Step 1.5 done. sanitized {fixedNodes} nodes.");
        log.WriteLine();

        // ---- Step 2: create random connections ----
        log.WriteLine("=== Step 2: Creating connections ===");
        int totalLinks = 0;
        int errorCount = 0;
        for (int i = 0; i < nodeCount; i++)
        {
            var candidates = new List<int>(nodeCount - i - 1);
            for (int j = i + 1; j < nodeCount; j++)
                candidates.Add(j);

            if (candidates.Count == 0) continue;

            int linkCount = rng.Next(0, Math.Min(maxLinksPerNode + 1, candidates.Count));
            for (int k = 0; k < linkCount; k++)
            {
                int pick = rng.Next(k, candidates.Count);
                (candidates[k], candidates[pick]) = (candidates[pick], candidates[k]);
            }

            for (int k = 0; k < linkCount; k++)
            {
                int targetIdx = candidates[k];

                try
                {
                    Connect(tree, nodes[i].OutputSlot!, nodes[targetIdx].InputSlot!);
                    totalLinks++;
                }
                catch (Exception ex)
                {
                    errorCount++;
                    log.WriteLine($"  >>> CONNECT ERROR [{totalLinks}] N{i}->N{targetIdx}: {ex.GetType().Name}: {ex.Message}");
                    log.WriteLine($"  >>> sender.Parent={(nodes[i].OutputSlot?.Parent is null ? "null" : (nodes[i].OutputSlot?.Parent == nodes[i] ? "ok" : "MISMATCH"))} recv.Parent={(nodes[targetIdx].InputSlot?.Parent is null ? "null" : (nodes[targetIdx].InputSlot?.Parent == nodes[targetIdx] ? "ok" : "MISMATCH"))}");

                    if (errorCount >= 5)
                    {
                        log.WriteLine("Too many errors, aborting.");
                        throw;
                    }
                }
            }

            if ((i + 1) % 100 == 0)
                log.WriteLine($"  progress: node[{i + 1}]/{nodeCount}, links created so far: {totalLinks}, errors: {errorCount}");
        }
        log.WriteLine($"Step 2 done. totalLinks={totalLinks}, errors={errorCount}");
        log.WriteLine();

        // ---- Step 3: place first 50 nodes in a visible grid near origin ----
        log.WriteLine("=== Step 3: Clustering near origin ===");
        int clusterCount = Math.Min(50, nodeCount);
        double spacingX = nodeSize.Width + 40;
        double spacingY = nodeSize.Height + 40;
        int cols = 10;
        for (int i = 0; i < clusterCount; i++)
        {
            int col = i % cols;
            int row = i / cols;
            nodes[i].Anchor = new Anchor(
                60 + col * spacingX,
                60 + row * spacingY,
                0);
        }
        log.WriteLine($"Step 3 done. placed {clusterCount} nodes in a grid near origin.");
        // Verify first node state
        var n0 = nodes[0];
        log.WriteLine($"Verify N[0]: Size=({n0.Size.Width},{n0.Size.Height}) Anchor=({n0.Anchor.Horizontal},{n0.Anchor.Vertical}) Slots.Count={n0.Slots.Count}");
        log.WriteLine($"=== PerformanceTestSession.Create completed successfully ===");

        return new PerformanceTestSession(tree);
    }

    private static void Connect(IWorkflowTreeViewModel tree, IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
    {
        tree.GetHelper().SendConnection(sender);
        tree.GetHelper().ReceiveConnection(receiver);
    }
}

/// <summary>
/// Reference-equality comparer for types that may override Object.Equals.
/// </summary>
internal sealed class RefEq<T> : IEqualityComparer<T>
    where T : class
{
    public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
    public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
}
