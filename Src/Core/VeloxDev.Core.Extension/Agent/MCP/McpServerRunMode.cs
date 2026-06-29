namespace VeloxDev.AI.MCP;

/// <summary>
/// Defines how an MCP server process is launched.
/// Installation directories are automatically isolated to <c>.evn/mcp/{runtime}/</c>
/// based on the selected runtime ecosystem.
/// </summary>
public enum McpServerRunMode
{
    /// <summary>
    /// <b>Node.js</b> — npm install + node {modulePath} {args}.
    /// Package installed to <c>.evn/mcp/node/{package}/</c>.
    /// Requires <see cref="McpServerConfiguration.NpmPackage"/> and <see cref="McpServerConfiguration.ServerModulePath"/>.
    /// </summary>
    Node,

    /// <summary>
    /// <b>Node.js</b> — npx -y {package} {args} (temporary download, no install).
    /// Uses <see cref="McpServerConfiguration.NpmPackage"/> as the package name.
    /// </summary>
    Npx,

    /// <summary>
    /// <b>Python (uv)</b> — uvx {package} {args}.
    /// uv provides its own isolated environment; no extra venv needed.
    /// Uses <see cref="McpServerConfiguration.NpmPackage"/> as the package name.
    /// </summary>
    Uvx,

    /// <summary>
    /// <b>.NET</b> — dotnet {dll} {args}.
    /// User pre-publishes to <c>.evn/mcp/dotnet/{package}/{ServerModulePath}</c>.
    /// <see cref="McpServerConfiguration.NpmPackage"/> is the subdirectory name,
    /// <see cref="McpServerConfiguration.ServerModulePath"/> is the .dll file name.
    /// </summary>
    Dotnet,

    /// <summary>
    /// <b>Python (pip + venv)</b> — creates an isolated venv → pip install → python -m {module} {args}.
    /// The venv lives at <c>.evn/mcp/py/venvs/{package}/</c>, never touches the global Python environment.
    /// <see cref="McpServerConfiguration.NpmPackage"/> is the PyPI package name,
    /// <see cref="McpServerConfiguration.ServerModulePath"/> is the entry module (defaults to the package name).
    /// </summary>
    Pip,
}
