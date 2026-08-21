namespace VeloxDev.AI.MCP;

/// <summary>
/// Connection lifecycle status of a single MCP server.
/// The host UI binds via <see cref="McpStatusViewModel"/> to show alive/installing/connecting/error.
/// </summary>
public enum McpServerStatus
{
    /// <summary>Loading has not started yet.</summary>
    NotStarted,

    /// <summary>Installing/preparing the runtime (local modes such as npm/pip). Remote Http mode skips this state.</summary>
    Installing,

    /// <summary>Connecting (launching the local process or handshaking with the remote server).</summary>
    Connecting,

    /// <summary>Connected; tools are available.</summary>
    Connected,

    /// <summary>Load/connection failed (see <see cref="McpServerStatusViewModel.Error"/>).</summary>
    Error,
}
