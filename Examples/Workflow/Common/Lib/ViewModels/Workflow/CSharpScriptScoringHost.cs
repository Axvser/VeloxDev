namespace Demo.ViewModels;

public sealed class CSharpScriptScoringHost
{
    public double Multiplier { get; set; } = 1;

    public double Bonus { get; set; }

    public double PassScore { get; set; } = 50;

    public CSharpScriptContext Score(CSharpScriptContext input)
    {
        input.Score =
            ((input.Seed + input.ItemCount) * Multiplier) + Bonus;
        input.Status = input.Score >= PassScore
            ? "Approved"
            : "Review";
        input.Record(
            "score",
            $"{input.Score:0.0}/{PassScore:0.0} => {input.Status}");
        return input;
    }
}
