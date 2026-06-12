namespace Demo.ViewModels;

public sealed class CSharpScriptEnrichmentHost
{
    public string Prefix { get; set; } = "CS";

    public int DelayMilliseconds { get; set; } = 200;

    public List<string> Tags { get; set; } = [];

    public async Task<CSharpScriptContext> EnrichAsync(
        CSharpScriptContext input)
    {
        await Task.Delay(DelayMilliseconds).ConfigureAwait(false);

        input.Data["external.id"] = $"{Prefix}-{input.Seed:0000}";
        input.Data["tags"] = string.Join(",", Tags);
        input.Status = "Enriched";
        input.Record(
            "enrich",
            $"id={input.Data["external.id"]}; tags={input.Data["tags"]}");
        return input;
    }
}
