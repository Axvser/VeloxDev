using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VeloxDev.Cli;

/// <summary>
/// Exports one execution result to a CSV file under <c>tasks/&lt;task&gt;/runs/</c>.
/// <para>
/// The engine reports log lines as strings and failures as records; that is the shape this writer consumes.
/// Writing is best-effort: a run must never fail because its log file could not be written.
/// </para>
/// </summary>
internal static class RunLogCsv
{
    /// <summary>Writes the result's logs and failures as <c>runs/&lt;timestamp&gt;.csv</c>.</summary>
    /// <param name="taskDirectory">The per-process task directory.</param>
    /// <param name="tool">The execution tool that produced the result; recorded in the file name.</param>
    /// <param name="json">The tool result JSON.</param>
    /// <returns>The written path, or <c>null</c> when the result is not a JSON object or the write fails.</returns>
    public static string? TryWrite(string taskDirectory, string tool, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var hasLogs = root.TryGetProperty("logs", out var logs) &&
                          logs.ValueKind == JsonValueKind.Array && logs.GetArrayLength() > 0;
            var hasFailures = root.TryGetProperty("failures", out var failures) &&
                              failures.ValueKind == JsonValueKind.Array && failures.GetArrayLength() > 0;

            var runs = Path.Combine(taskDirectory, "runs");
            Directory.CreateDirectory(runs);

            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            var path = Path.Combine(runs, $"{stamp}_{Sanitize(tool)}.csv");
            if (File.Exists(path))
                path = Path.Combine(runs, $"{stamp}_{Sanitize(tool)}_{Guid.NewGuid().ToString("N")[..4]}.csv");

            var builder = new StringBuilder();
            builder.AppendLine("seq,record,level,phase,order,attempt,node,message");

            var sequence = 0;
            void Row(string record, string level = "", string phase = "",
                string order = "", string attempt = "", string node = "", string message = "")
                => builder.AppendLine(
                    $"{sequence++},{Escape(record)},{Escape(level)},{Escape(phase)}," +
                    $"{Escape(order)},{Escape(attempt)},{Escape(node)},{Escape(message)}");

            Row(
                "run",
                level: "info",
                message: $"status={Field(root, "status")};runStatus={Field(root, "runStatus")};" +
                         $"outcome={Field(root, "outcome")};attempts={Field(root, "attempts")}");

            if (hasLogs)
            {
                foreach (var log in logs.EnumerateArray())
                    Row("log", message: log.ValueKind == JsonValueKind.String ? log.GetString() ?? "" : log.GetRawText());
            }

            if (hasFailures)
            {
                foreach (var failure in failures.EnumerateArray())
                {
                    if (failure.ValueKind != JsonValueKind.Object) continue;
                    Row(
                        "failure",
                        level: Field(failure, "level"),
                        phase: Field(failure, "phase"),
                        order: Field(failure, "order"),
                        attempt: Field(failure, "attempt"),
                        node: Field(failure, "node"),
                        message: Field(failure, "message"));
                }
            }

            // UTF-8 with BOM so spreadsheet tools open the Chinese text without guessing the encoding.
            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Adds <c>csvFile</c> to the result object; returns the original text when it is not an object.</summary>
    public static string Attach(string json, string csvPath)
    {
        try
        {
            if (JsonNode.Parse(json) is JsonObject obj)
            {
                obj["csvFile"] = csvPath;
                return obj.ToJsonString();
            }
        }
        catch (JsonException)
        {
            // Keep the original text.
        }

        return json;
    }

    private static string Field(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Null or JsonValueKind.Undefined => "",
                JsonValueKind.Object or JsonValueKind.Array => value.GetRawText(),
                _ => value.GetRawText(),
            }
            : "";

    private static string Escape(string value)
        => $"\"{value.Replace("\"", "\"\"")}\"";

    private static string Sanitize(string tool)
        => string.Concat(tool.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
}
