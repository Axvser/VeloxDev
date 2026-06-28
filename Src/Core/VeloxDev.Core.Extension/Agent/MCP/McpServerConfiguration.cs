using VeloxDev.MVVM;

namespace VeloxDev.AI.MCP;

public partial class McpServerConfiguration
{
    [VeloxProperty] public partial string Name { get; set; }
    [VeloxProperty] public partial string Description { get; set; }

    /// <summary>
    /// How the server process is launched.
    /// <see cref="McpServerRunMode.Node"/> requires <see cref="ServerModulePath"/>;
    /// <see cref="McpServerRunMode.Npx"/> and <see cref="McpServerRunMode.Uvx"/>
    /// use <see cref="NpmPackage"/> as the package name directly.
    /// </summary>
    [VeloxProperty] public partial McpServerRunMode RunMode { get; set; }

    /// <summary>NPM / Python 包名，如 "@modelcontextprotocol/server-filesystem" 或 "mcp-server-git"。</summary>
    [VeloxProperty] public partial string NpmPackage { get; set; }

    /// <summary>版本标签。为 null 时使用 "latest"。仅 <see cref="McpServerRunMode.Node"/> 模式生效。</summary>
    [VeloxProperty] public partial string? NpmVersion { get; set; }

    /// <summary>
    /// <para>包内相对路径，指向服务器入口文件</para>
    /// <para>如 @modelcontextprotocol/server-filesystem 的 "dist/index.js"</para>
    /// <para>仅在 <see cref="McpServerRunMode.Node"/> 模式下使用。</para>
    /// </summary>
    [VeloxProperty] public partial string ServerModulePath { get; set; }

    /// <summary>传给服务器进程的额外参数</summary>
    [VeloxProperty] public partial string[] ServerArguments { get; set; }
}
