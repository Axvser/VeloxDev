using VeloxDev.MVVM;

namespace VeloxDev.AI.MCP;

public partial class McpServerConfiguration
{
    [VeloxProperty] public partial string Name { get; set; }
    [VeloxProperty] public partial string Description { get; set; }

    /// <summary>
    /// How the server is reached.
    /// <see cref="McpServerRunMode.Npm"/> requires npm-installed package;
    /// <see cref="McpServerRunMode.Npx"/>, <see cref="McpServerRunMode.Uvx"/>, and <see cref="McpServerRunMode.Pip"/>
    /// use <see cref="Package"/> as the package name directly;
    /// <see cref="McpServerRunMode.Dotnet"/> executes via <c>dotnet {Package}</c>;
    /// <see cref="McpServerRunMode.Exe"/> executes <c>{Package}</c> directly (tech-agnostic);
    /// <see cref="McpServerRunMode.Http"/> connects to a remote server over HTTP using <see cref="Endpoint"/>.
    /// </summary>
    [VeloxProperty] public partial McpServerRunMode RunMode { get; set; }

    /// <summary>
    /// Package name / directory name.
    /// Npm/Npx/Uvx/Pip: an NPM or PyPI package name;
    /// Dotnet: a DLL path under mcpRoot, e.g. "sharp-email-mcp/SharpEmailMcp.dll";
    /// Exe: an executable path under mcpRoot, e.g. "tools/my-tool.exe".
    /// </summary>
    [VeloxProperty] public partial string Package { get; set; }

    /// <summary>
    /// Version tag. When null, "latest" is used.
    /// Applies to <see cref="McpServerRunMode.Npm"/> and <see cref="McpServerRunMode.Pip"/> modes.
    /// </summary>
    [VeloxProperty] public partial string? Version { get; set; }

    /// <summary>Extra arguments passed to the server process (e.g. an allowed-directory set for a filesystem server)</summary>
    [VeloxProperty] public partial string[] Arguments { get; set; }

    /// <summary>
    /// Remote MCP endpoint URL (only for <see cref="McpServerRunMode.Http"/> mode), e.g. "https://mcp.example.com/mcp".
    /// Connects via Streamable HTTP (older servers fall back to SSE automatically).
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Arbitrary server options — the serialized result of an anonymous object. The host can pass an
    /// anonymous object directly:
    /// <code>
    /// Options = new
    /// {
    ///     headers = new { Authorization = "Bearer x", "X-Custom" = "v" },   // HTTP extra headers
    ///     env = new { FILESYSTEM_ROOT = "C:/data", API_KEY = "k" },          // stdio per-server environment variables
    ///     connectionTimeout = 30,                                            // seconds (or a TimeSpan string); overrides McpScope.WithConnectionTimeout
    ///     transportMode = "StreamableHttp",                                  // Http: AutoDetect/StreamableHttp/Sse
    ///     ownsSession = true,                                                // Http: whether to hold the MCP session (stateful)
    ///     workingDirectory = "C:/data",                                      // stdio working directory
    ///     oauth = new { clientId = "id", clientSecret = "s",                 // Http: OAuth 2.0 (PKCE)
    ///                  redirectUri = "http://localhost:1179/cb", scopes = new[] { "read" } },
    /// };
    /// </code>
    /// Unknown keys are rejected by <see cref="McpScope"/> (an error rather than a silent ignore), so typos surface immediately.
    /// </summary>
    public object? Options { get; set; }
}
