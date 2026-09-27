using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using Demo.ViewModels;
using Demo.Workflow;

namespace VeloxDev.Core.Extension.Test.Examples;

/// <summary>
/// Runs the demo's Python scripts for real — the graph is stubbed everywhere else, and this is what keeps the
/// scripts themselves honest.
/// <para>
/// They are demo content, but they are also the reference for the conventions the helper implements: the host adds
/// <c>_attempt</c> and <c>_drive</c> to a payload, and reads <c>warn</c> / <c>error</c> / <c>redirect</c> back out of
/// a result. A rename on either side would quietly turn the showcase into a graph that does nothing, so it is
/// asserted here rather than trusted.
/// </para>
/// <para>
/// <b>Skips itself when the machine has no Python</b>, which is why it is the only test in the suite that spawns a
/// process.
/// </para>
/// </summary>
[TestClass]
public class DemoScriptTests
{
    private static string? _python;
    private static string? Python
    {
        get
        {
            if (_python is not null) return _python.Length == 0 ? null : _python;
            try
            {
                using var probe = Process.Start(new ProcessStartInfo("python", "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                })!;
                probe.WaitForExit(10_000);
                _python = probe.ExitCode == 0 ? "python" : string.Empty;
            }
            catch (Exception)
            {
                _python = string.Empty;
            }
            return _python.Length == 0 ? null : _python;
        }
    }

    /// <summary>Every Python node's script, by title — read off the demo's own graph rather than copied here.</summary>
    private static Dictionary<string, string> Scripts()
        => WorkflowDemoSession.Create().Tree.Nodes.OfType<PythonScriptNodeViewModel>()
            .ToDictionary(n => n.Title, n => n.Script);

    /// <summary>Runs one script the way <c>PythonHelper</c> does: <c>python script.py input.json output.json</c>.</summary>
    private static (bool Ok, Dictionary<string, object?> Output) Run(string script, object? payload, string directory)
    {
        var scriptPath = Path.Combine(directory, "script.py");
        var inputPath = Path.Combine(directory, "input.json");
        var outputPath = Path.Combine(directory, "output.json");
        File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
        File.WriteAllText(inputPath, JToken.FromObject(payload ?? new object()).ToString(), new UTF8Encoding(false));
        if (File.Exists(outputPath)) File.Delete(outputPath);

        using var process = Process.Start(new ProcessStartInfo(Python!, $"\"{scriptPath}\" \"{inputPath}\" \"{outputPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        process.WaitForExit(60_000);
        if (process.ExitCode != 0) return (false, []);

        var text = File.Exists(outputPath) ? File.ReadAllText(outputPath) : "{}";
        // Parsed the way the host parses it (PythonHelper.ParseResult), so the test sees the same CLR values a node
        // would: a number arrives as long, a JSON object as a dictionary.
        return (true, JToken.Parse(text).ToObject<Dictionary<string, object?>>() ?? []);
    }

    [TestMethod]
    public async Task TheGraphsScripts_HonourTheConventionsTheHelperImplements()
    {
        if (Python is null) Assert.Inconclusive("no Python on this machine: the scripts are not exercised");

        var scripts = Scripts();
        var directory = Path.Combine(Path.GetTempPath(), $"veloxdev-demo-scripts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            // ── Pass 1: the quick set, which the audit is meant to refuse ────────────────────────────────
            var quick = Run(scripts["Generate Dataset"], new Dictionary<string, object?> { ["_attempt"] = 1 }, directory);
            await Task.Yield();
            Assert.IsTrue(quick.Ok, "the generator runs");
            Assert.AreEqual(12L, Convert.ToInt64(quick.Output["count"]), "_attempt = 1 asks for the quick set");

            var dist = Run(scripts["Frequency Dist"], quick.Output, directory);
            Assert.IsTrue(dist.Ok && dist.Output.ContainsKey("warn"),
                "a histogram bin with nothing in it is worth a warning, and a warning carries the run on");

            var scan = Run(scripts["Anomaly Scan"], quick.Output, directory);
            Assert.IsTrue(scan.Ok && scan.Output.ContainsKey("warn"), "and so is a flat set with no anomaly in it");

            var stats = Run(scripts["Numeric Stats"], quick.Output, directory);
            var merge = Run(scripts["Merge Report"], new Dictionary<string, object?>
            {
                ["stats"] = stats.Output,
                ["dist"] = dist.Output,
                ["anomalies"] = scan.Output,
            }, directory);
            Assert.IsTrue(merge.Ok);
            Assert.IsTrue(Convert.ToInt64(merge.Output["sample_count"]) < 20, "the quick set is below the audit's bar");

            // ── The delivery: fails once, and carries the report on so the audit can read it ─────────────
            var first = Run(scripts["Publish"], With(merge.Output, ("_drive", 1), ("_attempt", 1)), directory);
            Assert.IsFalse(first.Ok, "the first delivery fails on purpose — that is the retry policy's cue");

            var retried = Run(scripts["Publish"], With(merge.Output, ("_drive", 2), ("_attempt", 1)), directory);
            Assert.IsTrue(retried.Ok, "the retry gets through");
            Assert.IsTrue(retried.Output.ContainsKey("sample_count"),
                "and the report travels on: the audit behind this node reads it");

            var rejected = Run(scripts["Audit"], retried.Output, directory);
            Assert.IsTrue(rejected.Ok);
            Assert.IsFalse(Convert.ToBoolean(rejected.Output["verified"]));
            Assert.IsTrue(rejected.Output.ContainsKey("error"), "an error is the complaint");
            Assert.AreEqual("Ticker", rejected.Output["redirect"], "and a name is where the run should fall back to");

            // The audit left null behind it, and the report script warns instead of crashing on that.
            var nothingToArchive = Run(scripts["Report Zero"], null, directory);
            Assert.IsTrue(nothingToArchive.Ok, "a null payload must not crash the tail of the pass");
            Assert.IsTrue(nothingToArchive.Output.ContainsKey("warn"));

            // ── Pass 2: what the redirect asked for ──────────────────────────────────────────────────────
            var full = Run(scripts["Generate Dataset"], new Dictionary<string, object?> { ["_attempt"] = 2 }, directory);
            Assert.IsTrue(full.Ok && Convert.ToInt64(full.Output["count"]) == 40L, "_attempt = 2 asks for the full set");

            var fullStats = Run(scripts["Numeric Stats"], full.Output, directory);
            var fullDist = Run(scripts["Frequency Dist"], full.Output, directory);
            var fullScan = Run(scripts["Anomaly Scan"], full.Output, directory);
            var fullMerge = Run(scripts["Merge Report"], new Dictionary<string, object?>
            {
                ["stats"] = fullStats.Output,
                ["dist"] = fullDist.Output,
                ["anomalies"] = fullScan.Output,
            }, directory);
            var accepted = Run(scripts["Audit"],
                Run(scripts["Publish"], With(fullMerge.Output, ("_drive", 3), ("_attempt", 2)), directory).Output, directory);

            Assert.IsTrue(accepted.Ok);
            Assert.IsTrue(Convert.ToBoolean(accepted.Output["verified"]), "the full set passes the audit");
            Assert.IsFalse(accepted.Output.ContainsKey("error"), "a pass reports no error");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static Dictionary<string, object?> With(
        Dictionary<string, object?> payload, params (string Key, object? Value)[] extra)
    {
        var copy = new Dictionary<string, object?>(payload);
        foreach (var (key, value) in extra) copy[key] = value;
        return copy;
    }
}
