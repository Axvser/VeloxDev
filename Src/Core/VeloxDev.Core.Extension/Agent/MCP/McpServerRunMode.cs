namespace VeloxDev.AI.MCP;

/// <summary>
/// Defines how an MCP server process is launched.
/// </summary>
public enum McpServerRunMode
{
    /// <summary>
    /// npm install + node {modulePath} {args}.
    /// Requires <see cref="McpServerConfiguration.NpmPackage"/> and <see cref="McpServerConfiguration.ServerModulePath"/>.
    /// </summary>
    Node,

    /// <summary>
    /// npx -y {package} {args}.
    /// Uses <see cref="McpServerConfiguration.NpmPackage"/> as the package name.
    /// </summary>
    Npx,

    /// <summary>
    /// uvx {package} {args}.
    /// Uses <see cref="McpServerConfiguration.NpmPackage"/> as the package name.
    /// </summary>
    Uvx,
}
