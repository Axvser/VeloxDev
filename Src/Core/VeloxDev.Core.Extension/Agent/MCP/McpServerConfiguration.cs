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
    /// 包名/目录名。
    /// Npm/Npx/Uvx/Pip: NPM 或 PyPI 包名；
    /// Dotnet: mcpRoot 下 DLL 路径，如 "sharp-email-mcp/SharpEmailMcp.dll"；
    /// Exe: mcpRoot 下可执行文件路径，如 "tools/my-tool.exe"。
    /// </summary>
    [VeloxProperty] public partial string Package { get; set; }

    /// <summary>
    /// 版本标签。为 null 时使用 "latest"。
    /// 对 <see cref="McpServerRunMode.Npm"/> 和 <see cref="McpServerRunMode.Pip"/> 模式生效。
    /// </summary>
    [VeloxProperty] public partial string? Version { get; set; }

    /// <summary>传给服务器进程的额外参数（如文件系统服务器的允许目录集）</summary>
    [VeloxProperty] public partial string[] Arguments { get; set; }

    /// <summary>
    /// 远程 MCP 端点 URL（仅 <see cref="McpServerRunMode.Http"/> 模式），如 "https://mcp.example.com/mcp"。
    /// 通过 Streamable HTTP（旧服务器自动回退 SSE）连接。
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// 任意服务器选项——匿名对象序列化结果。宿主直接传匿名对象即可：
    /// <code>
    /// Options = new
    /// {
    ///     headers = new { Authorization = "Bearer x", "X-Custom" = "v" },   // HTTP 附加头
    ///     env = new { FILESYSTEM_ROOT = "C:/data", API_KEY = "k" },          // stdio 逐服务器环境变量
    ///     connectionTimeout = 30,                                            // 秒（或 TimeSpan 字符串），覆盖 McpScope.WithConnectionTimeout
    ///     transportMode = "StreamableHttp",                                  // Http: AutoDetect/StreamableHttp/Sse
    ///     ownsSession = true,                                                // Http: 是否持有 MCP 会话（有状态）
    ///     workingDirectory = "C:/data",                                      // stdio 工作目录
    ///     oauth = new { clientId = "id", clientSecret = "s",                 // Http: OAuth 2.0（PKCE）
    ///                  redirectUri = "http://localhost:1179/cb", scopes = new[] { "read" } },
    /// };
    /// </code>
    /// 未知 key 会被 <see cref="McpScope"/> 拒绝（报错而非静默忽略），保证拼写错误立刻暴露。
    /// </summary>
    public object? Options { get; set; }
}
