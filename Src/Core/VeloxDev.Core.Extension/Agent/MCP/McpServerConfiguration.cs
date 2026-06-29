using VeloxDev.MVVM;

namespace VeloxDev.AI.MCP;

public partial class McpServerConfiguration
{
    [VeloxProperty] public partial string Name { get; set; }
    [VeloxProperty] public partial string Description { get; set; }

    /// <summary>
    /// How the server process is launched.
    /// <see cref="McpServerRunMode.Npm"/> requires npm-installed package;
    /// <see cref="McpServerRunMode.Npx"/>, <see cref="McpServerRunMode.Uvx"/>, and <see cref="McpServerRunMode.Pip"/>
    /// use <see cref="Package"/> as the package name directly;
    /// <see cref="McpServerRunMode.Dotnet"/> executes via <c>dotnet {Package}</c>;
    /// <see cref="McpServerRunMode.Exe"/> executes <c>{Package}</c> directly (tech-agnostic).
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

    /// <summary>传给服务器进程的额外参数</summary>
    [VeloxProperty] public partial string[] Arguments { get; set; }
}
