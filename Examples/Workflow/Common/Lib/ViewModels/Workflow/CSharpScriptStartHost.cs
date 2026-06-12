namespace Demo.ViewModels;

public sealed class CSharpScriptStartHost
{
    public string Scenario { get; set; } = "Demo";

    public int Seed { get; set; } = 1;

    public int ItemCount { get; set; } = 3;

    public List<string> Sources { get; set; } = [];

    public CSharpScriptContext Start()
    {
        var context = new CSharpScriptContext
        {
            RunId = $"CS-{DateTime.UtcNow:HHmmssfff}",
            Scenario = Scenario,
            Seed = Seed,
            ItemCount = ItemCount
        };

        context.Data["sources"] = string.Join(",", Sources);
        context.Record("start", $"{Scenario}; seed={Seed}; items={ItemCount}");
        return context;
    }
}
