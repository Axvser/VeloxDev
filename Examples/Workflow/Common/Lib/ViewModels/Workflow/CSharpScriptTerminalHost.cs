namespace Demo.ViewModels;

public sealed class CSharpScriptTerminalHost
{
    public string Destination { get; set; } = "demo://completed";

    public int DelayMilliseconds { get; set; } = 120;

    public async Task CompleteAsync(CSharpScriptContext input)
    {
        await Task.Delay(DelayMilliseconds).ConfigureAwait(false);

        input.Data["destination"] = Destination;
        input.Status = "Completed";
        input.Record(
            "complete",
            $"{Destination}; values={input.Data.Count}");
    }
}
