using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

/// <summary>
/// The session's log sink: an <see cref="ILogWriter"/> receives every line, in the same order and with the same
/// text as <see cref="IRuntimeContext.Logs"/>, and the in-memory view can be capped without costing the writer a
/// line. Also pins the two things a writer must not change: the default behaviour, and the redirect semantics of
/// <c>Warn</c>/<c>Error</c>.
/// </summary>
[TestClass]
public class CompilerLogWriterTests
{
    /// <summary>Collects what a session hands it, keeping the order.</summary>
    private sealed class CollectingWriter : ILogWriter
    {
        public List<string> Lines { get; } = [];
        public void Write(string line) => Lines.Add(line);
    }

    /// <summary>A two-node chain: enough to produce engine lines plus whatever the node logs.</summary>
    private static (ProbeNode First, ProbeNode Second) Chain()
    {
        var first = new ProbeNode("first");
        var second = new ProbeNode("second");
        ProbeGraph.Wire(first, second);
        return (first, second);
    }

    private static async Task<RuntimeContext> RunAsync(
        CompiledGraph graph, ILogWriter? writer = null, int? maxRetained = null)
    {
        var session = new RuntimeContext { LogWriter = writer, MaxRetainedLogs = maxRetained };
        await new RuntimeEngine().RunAsync(graph, session, CancellationToken.None);
        return session;
    }

    /// <summary>With no writer configured, nothing about today's behaviour may change.</summary>
    [TestMethod]
    public async Task WithoutAWriter_LinesStayInLogsAsBefore()
    {
        var (first, _) = Chain();
        first.Handler = (ctx, _) => { ((IRuntimeContext)ctx).Log("hello"); return null; };

        var session = await RunAsync(ProbeGraph.Compile(first));

        Assert.IsTrue(session.Logs.Any(l => l.Contains("hello")), "the node's line must be in Logs");
        Assert.IsTrue(session.Logs.Any(l => l.Contains(nameof(ProbeNode))), "the engine's per-node line must be in Logs");
    }

    /// <summary>The contract that makes a file-backed log trustworthy: same lines, same order, same text.</summary>
    [TestMethod]
    public async Task AWriter_ReceivesExactlyWhatLogsReceived_InTheSameOrder()
    {
        var (first, _) = Chain();
        first.Handler = (ctx, _) => { ((IRuntimeContext)ctx).Log("one"); return null; };
        var writer = new CollectingWriter();

        var session = await RunAsync(ProbeGraph.Compile(first), writer);

        CollectionAssert.AreEqual(session.Logs.ToList(), writer.Lines,
            "the writer and Logs must agree line for line — including the sequence prefixes");
    }

    /// <summary>The point of pairing them: the file keeps everything while memory keeps the tail.</summary>
    [TestMethod]
    public async Task MaxRetainedLogs_KeepsTheNewestLines_AndTheWriterLosesNothing()
    {
        var (first, _) = Chain();
        first.Handler = (ctx, _) => { ((IRuntimeContext)ctx).Log("one"); return null; };
        var writer = new CollectingWriter();

        var session = await RunAsync(ProbeGraph.Compile(first), writer, maxRetained: 2);

        Assert.HasCount(2, session.Logs, "only the newest lines are retained");
        Assert.IsTrue(writer.Lines.Count > session.Logs.Count, "the writer is the complete record");
        CollectionAssert.AreEqual(writer.Lines.TakeLast(2).ToList(), session.Logs.ToList(),
            "what is retained must be the newest lines, not the oldest");
    }

    /// <summary>Zero is legal and means "keep none in memory" — the writer still gets everything.</summary>
    [TestMethod]
    public async Task MaxRetainedLogs_Zero_KeepsNothingInMemory()
    {
        var (first, _) = Chain();
        var writer = new CollectingWriter();

        var session = await RunAsync(ProbeGraph.Compile(first), writer, maxRetained: 0);

        Assert.IsEmpty(session.Logs);
        Assert.IsTrue(writer.Lines.Count > 0, "the writer still receives every line");
    }

    /// <summary>
    /// A writer is a sink, not a filter: <c>Warn</c> still marks the run, so a run whose node warns still ends the
    /// flow (no node here implements <c>IRedirectable</c>).
    /// </summary>
    [TestMethod]
    public async Task AWriter_DoesNotChangeWarnSemantics()
    {
        var (first, second) = Chain();
        first.Handler = (ctx, _) => { ((IRuntimeContext)ctx).Warn("careful"); return null; };
        var writer = new CollectingWriter();

        var session = await RunAsync(ProbeGraph.Compile(first), writer);

        Assert.IsTrue(writer.Lines.Any(l => l.Contains("[Warning] careful")), "the warning still reaches the writer");
        Assert.IsTrue(session.EndedWithError, "a node that warns without IRedirectable still ends the flow");
        Assert.AreEqual("Stopped", session.Status);
        Assert.IsEmpty(second.Calls, "the flow must not have continued to the next node");
    }

    /// <summary>A sink that always fails, standing in for a full disk or a revoked path.</summary>
    private sealed class ThrowingWriter : ILogWriter
    {
        public int Attempts { get; private set; }
        public void Write(string line)
        {
            Attempts++;
            throw new IOException("disk is full");
        }
    }

    /// <summary>
    /// Diagnostics never change what the run does. Without the guard in <c>AppendLog</c> the exception would escape
    /// inside a node's frame and the engine would read it as a redirect request — a logging failure would abort a
    /// run.
    /// </summary>
    [TestMethod]
    public async Task AWriterThatThrows_DoesNotBreakTheRun_ButIsReported()
    {
        var (first, second) = Chain();
        var writer = new ThrowingWriter();
        var failures = 0;

        var graph = ProbeGraph.Compile(first);
        var session = new RuntimeContext { LogWriter = writer };
        session.LogWriteFailed += (_, e) =>
        {
            failures++;
            Assert.AreEqual("disk is full", e.Error.Message);
            Assert.IsFalse(string.IsNullOrEmpty(e.Line), "the failed line is reported with the error");
        };

        await new RuntimeEngine().RunAsync(graph, session, CancellationToken.None);

        Assert.IsTrue(writer.Attempts > 0, "the writer really was used");
        Assert.IsTrue(failures > 0, "a failing writer must be visible rather than merely quiet");
        Assert.AreEqual("Completed", session.Status, "the run must finish as if nothing had gone wrong");
        Assert.HasCount(1, second.Calls, "and the flow must have continued to the next node");
        Assert.IsTrue(session.Logs.Count > 0, "the in-memory log is unaffected by the sink's failure");
    }

    /// <summary>
    /// Inside a fan-out the line goes to the session (one log, one order) but the redirect request stays on the
    /// branch — if it leaked, a sibling would be judged to have asked for a redirect it never asked for.
    /// </summary>
    [TestMethod]
    public async Task ABranchsWarn_MarksTheBranch_NotTheSession()
    {
        var source = new ProbeNode("source") { Handler = (_, _) => "SRC" };
        var a = new ProbeNode("a");
        var b = new ProbeNode("b");
        var join = new ProbeNode("join");
        ProbeGraph.Wire(source, a);
        ProbeGraph.Wire(source, b);
        ProbeGraph.Wire(a, join);
        ProbeGraph.Wire(b, join);
        a.Handler = (ctx, _) => { ((IRuntimeContext)ctx).Warn("branch a is unhappy"); return "A"; };
        b.Handler = (_, _) => "B";

        var session = await RunAsync(ProbeGraph.Compile(source));

        Assert.IsTrue(session.Logs.Any(l => l.Contains("[Warning] branch a is unhappy")),
            $"the warning must still reach the session's log; got: {string.Join(" | ", session.Logs)}");
        Assert.IsFalse(session.RedirectRequested,
            "the request belongs to the branch that made it — the session must not be marked");
        Assert.IsTrue(session.EndedWithError, "the run's outcome, by contrast, is global");
    }

    /// <summary>The shipped writer appends to a <see cref="TextWriter"/>, one line each, UTF-8 without a BOM.</summary>
    [TestMethod]
    public void TextWriterLogWriter_WritesOneLineEach()
    {
        var sink = new StringWriter();
        var writer = new TextWriterLogWriter(sink);

        writer.Write("01. first");
        writer.Write("02. second");

        Assert.AreEqual($"01. first{sink.NewLine}02. second{sink.NewLine}", sink.ToString());
    }

    /// <summary>And appending to a real file keeps earlier content, with no byte-order mark.</summary>
    [TestMethod]
    public void TextWriterLogWriter_For_AppendsWithoutABom()
    {
        var path = Path.Combine(Path.GetTempPath(), $"veloxdev-logwriter-{Guid.NewGuid():N}.log");
        try
        {
            File.WriteAllText(path, "existing\n");
            using (var writer = TextWriterLogWriter.For(path))
                writer.Write("appended");

            var bytes = File.ReadAllBytes(path);
            Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "the file must not gain a UTF-8 byte-order mark");
            var text = new UTF8Encoding(false).GetString(bytes);
            StringAssert.StartsWith(text, "existing", "appending must not truncate");
            StringAssert.Contains(text, "appended");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);   // 姊妹模块的 SkillScopeTests 就是漏在这里没清理
        }
    }
}
