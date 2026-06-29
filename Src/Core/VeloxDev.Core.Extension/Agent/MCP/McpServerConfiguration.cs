using VeloxDev.MVVM;

namespace VeloxDev.AI.MCP;

public partial class McpServerConfiguration
{
    [VeloxProperty] public partial string Name { get; set; }
    [VeloxProperty] public partial string Description { get; set; }

    /// <summary>
    /// How the server process is launched.
    /// <see cref="McpServerRunMode.Node"/> requires <see cref="ServerModulePath"/>;
    /// <see cref="McpServerRunMode.Npx"/>, <see cref="McpServerRunMode.Uvx"/>, and <see cref="McpServerRunMode.Pip"/>
    /// use <see cref="NpmPackage"/> as the package name directly;
    /// <see cref="McpServerRunMode.Dotnet"/> uses <see cref="NpmPackage"/> as subdirectory and <see cref="ServerModulePath"/> as the DLL.
    /// </summary>
    [VeloxProperty] public partial McpServerRunMode RunMode { get; set; }

    /// <summary>
    /// 包名/目录名。
    /// Node/Npx/Uvx/Pip: NPM 或 PyPI 包名；
    /// Dotnet: mcpRoot 下的子目录名。
    /// </summary>
    [VeloxProperty] public partial string NpmPackage { get; set; }

    /// <summary>
    /// 版本标签。为 null 时使用 "latest"。
    /// 对 <see cref="McpServerRunMode.Node"/> 和 <see cref="McpServerRunMode.Pip"/> 模式生效。
    /// </summary>
    [VeloxProperty] public partial string? NpmVersion { get; set; }

    /// <summary>
    /// <para>包内相对路径，指向服务器入口文件</para>
    /// <para>Node: 如 "dist/index.js"</para>
    /// <para>Dotnet: 如 "SharpEmailMcp.dll"</para>
    /// <para>Pip: 如 "mcp_server_email"（入口模块名，默认同包名）</para>
    /// </summary>
    [VeloxProperty] public partial string ServerModulePath { get; set; }

    /// <summary>传给服务器进程的额外参数</summary>
    [VeloxProperty] public partial string[] ServerArguments { get; set; }
}
