using System.Globalization;

namespace VeloxDev.Adapters.Benchmarks.Headed;

/// <summary>Parsed options for one headed view-pipeline benchmark run.</summary>
/// <remarks>
/// The options describe the scenario only; which adapters can actually run is decided by the host
/// application that calls <see cref="HeadedBenchmarkCore.ExecuteAsync"/>.
/// </remarks>
public sealed class HeadedOptions
{
    /// <summary>Gets or sets the adapter name written to the report and used in the output file name.</summary>
    public string Adapter { get; set; } = "avalonia";

    /// <summary>Gets or sets the pool policy: <c>adaptive</c> or <c>fixed</c>.</summary>
    public string Policy { get; set; } = "adaptive";

    /// <summary>Gets or sets the number of nodes in the synthetic workflow.</summary>
    public int Nodes { get; set; } = 2000;

    /// <summary>Gets or sets the number of links in the synthetic workflow.</summary>
    public int Links { get; set; } = 3000;

    /// <summary>Gets or sets the number of measured iterations.</summary>
    public int Iterations { get; set; } = 5;

    /// <summary>Gets or sets the number of discarded warmup iterations.</summary>
    public int WarmupIterations { get; set; } = 1;

    /// <summary>Gets or sets the number of viewport moves in the diagonal scroll sweep.</summary>
    public int ScrollSteps { get; set; } = 20;

    /// <summary>Gets or sets the viewport width in device-independent pixels.</summary>
    public double ViewportWidth { get; set; } = 1200d;

    /// <summary>Gets or sets the viewport height in device-independent pixels.</summary>
    public double ViewportHeight { get; set; } = 800d;

    /// <summary>Gets or sets the synthetic node width.</summary>
    public double NodeWidth { get; set; } = 160d;

    /// <summary>Gets or sets the synthetic node height.</summary>
    public double NodeHeight { get; set; } = 90d;

    /// <summary>Gets or sets the spatial index cell size used by the visibility helper.</summary>
    public double CellSize { get; set; } = 256d;

    /// <summary>Gets or sets the batch size passed to the pool policy.</summary>
    public int BatchSize { get; set; } = 64;

    /// <summary>Gets or sets the per-batch frame budget passed to the pool policy.</summary>
    public double FrameBudgetMilliseconds { get; set; } = 8d;

    /// <summary>Gets or sets the synthetic workflow's random seed.</summary>
    public int Seed { get; set; } = 12345;

    /// <summary>Gets or sets the output directory; <see langword="null"/> derives one from the entry project.</summary>
    public string? OutputPath { get; set; }

    /// <summary>Gets the command-line usage text shown for <c>--help</c> and parse failures.</summary>
    public static string Usage =>
        """
        headed --adapter avalonia|wpf|winforms|jalium [--policy adaptive|fixed]
               [--nodes 2000] [--links 3000] [--iterations 5] [--warmup 1]
               [--steps 20] [--viewport 1200x800] [--node 160x90]
               [--cell 256] [--batch 64] [--budget 8] [--seed 12345] [--out path]
        """;

    /// <summary>Parses one headed benchmark command line.</summary>
    /// <param name="args">The arguments after the <c>headed</c> verb.</param>
    /// <returns>The parsed options.</returns>
    /// <exception cref="ArgumentException">An option is unknown, missing its value, or out of range.</exception>
    public static HeadedOptions Parse(string[] args)
    {
        var options = new HeadedOptions();
        for (var index = 0; index < args.Length; index++)
        {
            var key = args[index];
            if (key is "--help" or "-h")
            {
                throw new ArgumentException(Usage);
            }

            var value = index + 1 < args.Length ? args[index + 1] : throw new ArgumentException($"Missing value for {key}.\n{Usage}");
            index++;

            switch (key)
            {
                case "--adapter":
                    options.Adapter = value.ToLowerInvariant();
                    break;
                case "--policy":
                    options.Policy = value.ToLowerInvariant();
                    break;
                case "--nodes":
                    options.Nodes = ParseInt(key, value);
                    break;
                case "--links":
                    options.Links = ParseInt(key, value);
                    break;
                case "--iterations":
                    options.Iterations = ParseInt(key, value);
                    break;
                case "--warmup":
                    options.WarmupIterations = ParseInt(key, value);
                    break;
                case "--steps":
                    options.ScrollSteps = ParseInt(key, value);
                    break;
                case "--viewport":
                    (options.ViewportWidth, options.ViewportHeight) = ParsePair(key, value);
                    break;
                case "--node":
                    (options.NodeWidth, options.NodeHeight) = ParsePair(key, value);
                    break;
                case "--cell":
                    options.CellSize = ParseDouble(key, value);
                    break;
                case "--batch":
                    options.BatchSize = ParseInt(key, value);
                    break;
                case "--budget":
                    options.FrameBudgetMilliseconds = ParseDouble(key, value);
                    break;
                case "--seed":
                    options.Seed = ParseInt(key, value);
                    break;
                case "--out":
                    options.OutputPath = value;
                    break;
                default:
                    throw new ArgumentException($"Unknown option {key}.\n{Usage}");
            }
        }

        if (options.Adapter.Length == 0)
        {
            throw new ArgumentException($"--adapter needs a name.\n{Usage}");
        }

        if (options.Policy is not ("adaptive" or "fixed"))
        {
            throw new ArgumentException("--policy must be adaptive or fixed.");
        }

        if (options.Nodes < 1 || options.Links < 0 || options.Iterations < 1 || options.ScrollSteps < 0)
        {
            throw new ArgumentException("Nodes >= 1, Links >= 0, Iterations >= 1 and Steps >= 0 are required.");
        }

        return options;
    }

    private static int ParseInt(string key, string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new ArgumentException($"{key} expects an integer, got '{value}'.");

    private static double ParseDouble(string key, string value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new ArgumentException($"{key} expects a number, got '{value}'.");

    private static (double, double) ParsePair(string key, string value)
    {
        var parts = value.Split('x', 'X');
        if (parts.Length != 2)
        {
            throw new ArgumentException($"{key} expects WIDTHxHEIGHT, got '{value}'.");
        }

        return (ParseDouble(key, parts[0]), ParseDouble(key, parts[1]));
    }
}
