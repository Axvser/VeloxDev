namespace VeloxDev.Cli.Test;

[TestClass]
public class RunLogCsvTests
{
    [TestMethod]
    public void WritesLogsAndFailuresAsEscapedCsv()
    {
        var root = Path.Combine(Path.GetTempPath(), $"veloxdev-csv-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            const string json = """
                {
                  "status": "ok",
                  "runStatus": "Completed",
                  "outcome": "Completed",
                  "attempts": 1,
                  "logs": ["first", "line, with comma", "quote \" here"],
                  "failures": [
                    { "level": "warn", "phase": "Receive", "order": 2, "attempt": 1, "node": "n1", "message": "bad" }
                  ]
                }
                """;

            var path = RunLogCsv.TryWrite(root, "RunCompiledWorkflow", json);

            Assert.IsNotNull(path);
            Assert.IsTrue(path!.StartsWith(Path.Combine(root, "runs"), StringComparison.OrdinalIgnoreCase), path);
            var csv = File.ReadAllText(path);
            StringAssert.StartsWith(csv, "seq,record,level,phase,order,attempt,node,message");
            StringAssert.Contains(csv, "\"line, with comma\"");
            StringAssert.Contains(csv, "\"quote \"\" here\"");
            StringAssert.Contains(csv, "\"failure\"");
            StringAssert.Contains(csv, "\"Receive\"");
            StringAssert.Contains(csv, "\"run\"");

            var attached = RunLogCsv.Attach(json, path);
            StringAssert.Contains(attached, "\"csvFile\"");
            StringAssert.Contains(attached, path.Replace("\\", "\\\\"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    [TestMethod]
    public void WritesARunRowEvenWithoutLogsOrFailures()
    {
        var root = Path.Combine(Path.GetTempPath(), $"veloxdev-csv-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = RunLogCsv.TryWrite(
                root, "RunCompiledWorkflow", """{"status":"ok","runStatus":"Cancelled","outcome":"Cancelled","logs":[],"failures":[]}""");

            Assert.IsNotNull(path);
            var csv = File.ReadAllText(path!);
            StringAssert.StartsWith(csv, "seq,record,level,phase,order,attempt,node,message");
            StringAssert.Contains(csv, "\"run\"");
            StringAssert.Contains(csv, "runStatus=Cancelled");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }
}
