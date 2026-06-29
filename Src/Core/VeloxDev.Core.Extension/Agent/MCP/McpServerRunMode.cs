namespace VeloxDev.AI.MCP;

/// <summary>
/// Defines how an MCP server process is launched.
/// Installation directories are automatically isolated to <c>.evn/mcp/{runtime}/</c>
/// based on the selected runtime ecosystem.
/// </summary>
public enum McpServerRunMode
{
    /// <summary>
    /// <b>npm</b> — npm install + node {modulePath} {args}.
    /// Package installed to <c>.evn/mcp/node/{package}/</c>.
    /// Requires <see cref="McpServerConfiguration.Package"/>.
    /// </summary>
    Npm,

    /// <summary>
    /// <b>npx</b> — npx -y {package} {args} (temporary download, no install).
    /// Uses <see cref="McpServerConfiguration.Package"/> as the package name.
    /// </summary>
    Npx,

    /// <summary>
    /// <b>Python (uv)</b> — uvx {package} {args}.
    /// uv provides its own isolated environment; no extra venv needed.
    /// Uses <see cref="McpServerConfiguration.Package"/> as the package name.
    /// </summary>
    Uvx,

    /// <summary>
    /// <b>.NET</b> — dotnet {dll} {args}.
    /// User pre-publishes to <c>.evn/mcp/dotnet/{package}</c>.
    /// <see cref="McpServerConfiguration.Package"/> is the DLL path relative to <c>dotnet/</c>,
    /// e.g. <c>"sharp-email-mcp/SharpEmailMcp.dll"</c>.
    /// </summary>
    Dotnet,

    /// <summary>
    /// <b>Python (pip + venv)</b> — creates an isolated venv → pip install → python -m {module} {args}.
    /// The venv lives at <c>.evn/mcp/py/venvs/{package}/</c>.
    /// <see cref="McpServerConfiguration.Package"/> is the PyPI package name.
    /// </summary>
    Pip,

    /// <summary>
    /// <b>Any executable</b> — directly executes the file at <c>.evn/mcp/exe/{Package}</c>.
    /// Tech-agnostic: works for native binaries (.exe, ELF), shell scripts (.sh, .bat),
    /// scripts with shebang, and self-contained executables.
    /// The file must have execute permission (Unix) or be a recognized executable (Windows).
    /// </summary>
    Exe,
}
