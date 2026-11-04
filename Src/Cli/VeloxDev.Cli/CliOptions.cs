namespace VeloxDev.Cli;

/// <summary>What the process was asked to do, and with which archive.</summary>
internal sealed class CliOptions
{
    public const string Usage = """
        VeloxDev.Cli — a headless workflow runner and a stdio MCP server.

        Usage:
          veloxdev mcp --archive <graph.json> [options]
          veloxdev run --archive <graph.json> --start <nodeIndex> [options]

        Common options:
          --archive <path>       Workflow document produced by the library's serializer. Required.
          --manifest <path>      Archive manifest. Defaults to <archive>.manifest.json when that file exists.
          --assembly <path>      A dependency assembly to load before the document is read. Repeatable.
          --strict-plugin-versions
                                 Refuse to load a plugin whose VeloxDev.Core / Core.Extension reference has a
                                 different major version than the host. Default is to load it and warn.
          --single-archive       Serve exactly one document for the life of the process: LoadArchive is
                                 refused, so runtime IDs remain stable. Start a new process per archive.
          --no-assembly-copy     Load dependency DLLs from their source directory instead of a per-process
                                 copy. The default copy keeps the project's build output unlocked.
          --no-run-csv           Do not write execution results to tasks/<task>/runs/<timestamp>.csv.
                                 The CSV export is on by default.
          --root-type <name>     Concrete tree type the document was serialized from, when it was not
                                 TreeDefaultViewModel (e.g. Demo.ViewModels.TreeViewModel). The manifest may
                                 carry the same value as "rootType".
          --allow-execution      Expose the tools that run node business code (mcp mode only).
          --in-process-execution Run node code inside the MCP server instead of a child process. Faster and
                                 enables the handle/pause/status tools, but a hanging or crashing node takes
                                 the server down with it.
          --redirect-on-error <index>
                                 Host-level fallback for a node that reports an error but does not implement
                                 IRedirectable: re-run from this upstream node. 0-based ListNodes index.
          --checkpoint-file <path>
                                 Keep run checkpoints in this file instead of memory, so a continuation survives
                                 the process. In-process execution only.
          --log-file <path>      Append every run's log lines to this file. The run result then names it as
                                 logFile instead of relying on the in-memory tail.
          --workspace <dir>      Restrict every file path this process reads or writes (archive, manifest,
                                 assemblies, checkpoint, log, SaveArchive) to this existing directory.
          --max-concurrent-runs <n>
                                 How many isolated worker processes may run at once (default 2; 0 = unlimited).
                                 Excess calls wait for a slot.
          --worker-kill-grace-ms <n>
                                 Extra time after a run's timeout before the server kills the worker (default
                                 5000). Tests and tightly controlled hosts can lower it.
          --help, -h             Show this text.

        run options:
          --start <index>        Index of the compile entry node, as listed by ListNodes. Required.
          --node-result          Use the terminal role: compute the node at --start (and its ancestor cone)
                                 instead of running the whole chain from it.
          --output-tree <path>   Write the post-run graph to this path. The result names it as stateFile; the
                                 MCP client can adopt it with LoadArchive after an isolated run.
          --seed <text>          Optional seed payload written to the session's Data.
          --timeout-ms <n>       Optional timeout. Omit to wait for the run to end.

        The archive manifest is a small JSON file:
          {
            "assemblies": [ "lib/MyNodes.dll" ],
            "allowExecution": false
          }
        Assembly paths are resolved relative to the manifest and may not escape its directory.
        Loading an assembly runs its code, so both the manifest and --assembly are privileged input.
        """;

    public string Mode { get; private set; } = "mcp";
    public string? Archive { get; private set; }
    public string? Manifest { get; private set; }
    public string? RootType { get; private set; }
    public List<string> Assemblies { get; } = [];
    public bool StrictPluginVersions { get; private set; }
    public bool SingleArchive { get; private set; }
    public bool NoAssemblyCopy { get; private set; }
    public bool NoRunCsv { get; private set; }
    public bool AllowExecution { get; private set; }
    public bool InProcessExecution { get; private set; }
    public bool NodeResult { get; private set; }
    public string? OutputTree { get; private set; }
    public int? StartNode { get; private set; }
    public string? Seed { get; private set; }
    public int? TimeoutMs { get; private set; }
    public int? RedirectOnError { get; private set; }
    public string? CheckpointFile { get; private set; }
    public string? LogFile { get; private set; }
    public string? Workspace { get; private set; }
    public int MaxConcurrentRuns { get; private set; } = 2;
    public int WorkerKillGraceMs { get; private set; } = 5_000;
    public bool ShowHelp { get; private set; }

    private CliOptions() { }

    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0) return new CliOptions { ShowHelp = true };

        if (args[0] is "--help" or "-h" or "help")
            return new CliOptions { ShowHelp = true };

        var mode = args[0];
        if (mode is not ("mcp" or "run"))
            throw new CliUsageException($"Unknown command '{mode}'. Expected 'mcp' or 'run'.");

        var options = new CliOptions
        {
            Mode = mode,
            Archive = null,
            Manifest = null,
        };

        for (var i = 1; i < args.Length; i++)
        {
            var raw = args[i];
            var (name, inline) = Split(raw);

            string Value()
            {
                if (inline is not null) return inline;
                if (i + 1 >= args.Length) throw new CliUsageException($"Option '{name}' requires a value.");
                return args[++i];
            }

            switch (name)
            {
                case "--archive":
                    options.Archive = Value();
                    break;
                case "--manifest":
                    options.Manifest = Value();
                    break;
                case "--root-type":
                    options.RootType = Value();
                    break;
                case "--assembly":
                    options.Assemblies.Add(Value());
                    break;
                case "--strict-plugin-versions":
                    options.StrictPluginVersions = true;
                    break;
                case "--single-archive":
                    options.SingleArchive = true;
                    break;
                case "--no-assembly-copy":
                    options.NoAssemblyCopy = true;
                    break;
                case "--no-run-csv":
                    options.NoRunCsv = true;
                    break;
                case "--assemblies":
                    foreach (var part in Value().Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        options.Assemblies.Add(part);
                    break;
                case "--allow-execution":
                    options.AllowExecution = true;
                    break;
                case "--in-process-execution":
                    options.InProcessExecution = true;
                    break;
                case "--node-result":
                    options.NodeResult = true;
                    break;
                case "--output-tree":
                    options.OutputTree = Value();
                    break;
                case "--start":
                    options.StartNode = ParseInt(Value(), name);
                    break;
                case "--seed":
                    options.Seed = Value();
                    break;
                case "--timeout-ms":
                    options.TimeoutMs = ParseInt(Value(), name);
                    break;
                case "--redirect-on-error":
                    options.RedirectOnError = ParseInt(Value(), name);
                    break;
                case "--checkpoint-file":
                    options.CheckpointFile = Value();
                    break;
                case "--log-file":
                    options.LogFile = Value();
                    break;
                case "--workspace":
                    options.Workspace = Value();
                    break;
                case "--max-concurrent-runs":
                    options.MaxConcurrentRuns = ParseInt(Value(), name);
                    if (options.MaxConcurrentRuns < 0)
                        throw new CliUsageException("--max-concurrent-runs cannot be negative (0 = unlimited).");
                    break;
                case "--worker-kill-grace-ms":
                    options.WorkerKillGraceMs = ParseInt(Value(), name);
                    if (options.WorkerKillGraceMs < 0)
                        throw new CliUsageException("--worker-kill-grace-ms cannot be negative.");
                    break;
                case "--help":
                case "-h":
                    return new CliOptions { ShowHelp = true };
                default:
                    throw new CliUsageException($"Unknown option '{name}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(options.Archive))
            throw new CliUsageException("--archive is required.");

        if (mode == "run" && options.StartNode is null)
            throw new CliUsageException("--start is required for 'run'.");

        return options;
    }

    private static (string Name, string? Value) Split(string raw)
    {
        var index = raw.IndexOf('=');
        return index < 0 ? (raw, null) : (raw[..index], raw[(index + 1)..]);
    }

    private static int ParseInt(string text, string name)
        => int.TryParse(text, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new CliUsageException($"Option '{name}' expects an integer, got '{text}'.");
}

/// <summary>The caller asked for something the command line cannot express.</summary>
internal sealed class CliUsageException(string message) : Exception(message);
