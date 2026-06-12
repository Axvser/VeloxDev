namespace Demo.ViewModels;

public sealed class CSharpScriptContext
{
    public string RunId { get; set; } = string.Empty;

    public string Scenario { get; set; } = string.Empty;

    public int Seed { get; set; }

    public int ItemCount { get; set; }

    public double Score { get; set; }

    public string Status { get; set; } = "Created";

    public Dictionary<string, string> Data { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<string> Stages { get; } = [];

    public void Record(string stage, string detail)
    {
        Stages.Add(stage);
        Data[$"stage.{Stages.Count:00}"] = detail;
    }

    public override string ToString()
        => $"{RunId} | {Status} | score={Score:0.0} | " +
           string.Join(" -> ", Stages);
}
