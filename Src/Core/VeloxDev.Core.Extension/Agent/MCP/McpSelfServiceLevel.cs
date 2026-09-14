namespace VeloxDev.AI.MCP;

/// <summary>
/// How far an Agent may go in adding MCP servers of its own, from most restrictive to least.
/// <para>
/// The ladder opens two independent things in order: <i>which</i> server kinds may be added, and then
/// whether the user still has to agree. A remote server only sends data out; a local one installs and
/// runs a package on the machine, which is why the two are opened separately.
/// </para>
/// <para>
/// A host that never calls <c>WithSelfService</c> stays at <see cref="Closed"/>, which is the state
/// every release before this one had.
/// </para>
/// </summary>
public enum McpSelfServiceLevel
{
    /// <summary>
    /// The Agent may only load, unload and inspect servers the host pre-registered. It cannot name a
    /// configuration the host did not supply, and the add tool is not registered at all.
    /// </summary>
    Closed = 0,

    /// <summary>
    /// The Agent may add <b>remote (Http)</b> servers, subject to user confirmation. Local stdio modes —
    /// which install and launch a package — remain limited to host-registered configurations.
    /// </summary>
    RemoteConfirmed = 1,

    /// <summary>
    /// The Agent may add remote <i>and</i> local servers, each subject to user confirmation.
    /// </summary>
    AllConfirmed = 2,

    /// <summary>
    /// The Agent may add any server without asking. Use only where the machine and the conversation are
    /// both trusted: this is the setting under which the model can cause a package to be installed and
    /// run.
    /// </summary>
    Unrestricted = 3,
}
