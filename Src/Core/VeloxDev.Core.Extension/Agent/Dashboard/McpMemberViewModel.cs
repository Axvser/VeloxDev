using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VeloxDev.AI.MCP;
using VeloxDev.MVVM;

namespace VeloxDev.AI.Dashboard;

/// <summary>
/// One tool of one MCP server, shown when its server row is expanded. Switching it off is narrower than
/// switching the server off: the server keeps offering its other tools.
/// </summary>
public sealed partial class McpToolMemberViewModel : AgentMemberViewModel
{
    private readonly McpScope _scope;
    private readonly string _serverName;

    internal McpToolMemberViewModel(
        McpScope scope, string serverName, string toolName, string description, bool serverEnabled)
    {
        _scope = scope;
        _serverName = serverName;
        Name = toolName;
        Description = description;
        IsEnabled = scope.IsToolEnabled(serverName, toolName);
        DescribeServerState(serverEnabled);
    }

    /// <summary>
    /// Re-describes this tool after its server was switched. The tool keeps its own switch; what changes is
    /// whether the server hands anything over at all.
    /// </summary>
    internal void DescribeServerState(bool serverEnabled)
    {
        StateText = serverEnabled ? "就绪" : "服务器已关闭";
        Note = serverEnabled ? string.Empty : "所在服务器被宿主关闭";
    }

    /// <inheritdoc />
    protected override void ApplyToScope(bool enabled) => _scope.SetToolEnabled(_serverName, Name, enabled);
}

/// <summary>
/// An MCP server row, expandable to its tools.
/// <para>
/// The two operations this row offers are deliberately distinct and both present, because they cost
/// different things: the checkbox sets <see cref="AgentMemberViewModel.IsEnabled"/>, which drops the
/// server's tools from the model's tool set while the connection stays up (instant, reversible), whereas
/// <see cref="UnloadAsync"/> tears the process/transport down and <see cref="ReloadAsync"/> has to pay for
/// it again.
/// </para>
/// </summary>
public sealed partial class McpServerMemberViewModel : AgentMemberViewModel
{
    private readonly McpScope _scope;

    [VeloxProperty] private bool isExpanded = true;
    [VeloxProperty] private string runModeText = string.Empty;
    [VeloxProperty] private int loadedToolCount = 0;
    [VeloxProperty] private string error = string.Empty;
    [VeloxProperty] private bool isConnected = false;
    [VeloxProperty] private bool isWorking = false;
    [VeloxProperty] private bool isFailed = false;
    [VeloxProperty] private ObservableCollection<McpToolMemberViewModel> tools = [];

    internal McpServerMemberViewModel(McpScope scope, McpServerStatusViewModel server)
    {
        _scope = scope;
        Name = server.Name;
        Description = server.Description;
        IsEnabled = server.IsEnabled;
        Sync(server);
    }

    /// <summary>There is an error message to show.</summary>
    public bool HasError => Error.Length > 0;

    /// <summary>
    /// The server's own state is Failed. Deliberately distinct from <see cref="HasError"/>: a message can
    /// be attached to a server that is still connected, and the 存活 / 错误 roll-up has to count states,
    /// or one server would land in both buckets.
    /// </summary>
    public bool IsFailedState => IsFailed;

    /// <summary>
    /// The disclosure caret, kept as a stored property rather than a computed one: a get-only property
    /// depends on the binding picking up a <c>PropertyChanged</c> that the sibling flag raises, and a
    /// stored value carries its own initial state and notification. The row is opened by a plain button
    /// rather than an <c>Expander</c> header because the switches have to sit on the server row itself,
    /// and an interactive child inside an expander header competes with the header's own toggle.
    /// </summary>
    [VeloxProperty] private string expandGlyph = ExpandedGlyph;

    private const string ExpandedGlyph = "▾";     // ▾
    private const string CollapsedGlyph = "▸";    // ▸

    partial void OnIsExpandedChanged(bool oldValue, bool newValue)
        => ExpandGlyph = newValue ? ExpandedGlyph : CollapsedGlyph;

    /// <summary>Shows or hides the sub-tool list.</summary>
    public void ToggleExpand() => IsExpanded = !IsExpanded;

    /// <summary>The connection is up, so unloading is meaningful.</summary>
    public bool CanUnload => IsConnected;

    /// <summary>Nothing is connected, so there is something to load.</summary>
    public bool CanReload => !IsConnected && !IsWorking;

    partial void OnErrorChanged(string oldValue, string newValue)
        => OnPropertyChanged(nameof(HasError));

    partial void OnIsConnectedChanged(bool oldValue, bool newValue)
    {
        OnPropertyChanged(nameof(CanUnload));
        OnPropertyChanged(nameof(CanReload));
    }

    partial void OnIsWorkingChanged(bool oldValue, bool newValue)
        => OnPropertyChanged(nameof(CanReload));

    /// <inheritdoc />
    protected override void ApplyToScope(bool enabled) => _scope.SetServerEnabled(Name, enabled);

    /// <summary>
    /// Adopts the scope's row. Called on the dashboard's thread whenever the server's status changed.
    /// </summary>
    internal void Sync(McpServerStatusViewModel server)
    {
        RunModeText = server.RunMode.ToString();
        LoadedToolCount = server.ToolCount;
        Error = server.Error ?? string.Empty;
        IsConnected = server.IsConnected;
        IsWorking = server.IsInstalling || server.IsConnecting;
        IsFailed = server.IsError;
        StateText = server.StateText;
        SetFromScope(server.IsEnabled);
    }

    /// <summary>
    /// Rebuilds the sub-tool list from what the server actually exposed. Reads
    /// <see cref="McpScope.GetServerTools"/> — the server's real tools, <b>not</b> the switched-off-filtered
    /// view — so a switched-off server still shows which tools the host could turn back on.
    /// </summary>
    internal void RebuildTools()
    {
        var live = _scope.GetServerTools(Name);

        // Keep existing rows so a switch the host already flipped is not thrown away by a status refresh.
        var kept = Tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        Tools.Clear();
        foreach (var tool in live)
        {
            Tools.Add(kept.TryGetValue(tool.Name, out var existing)
                ? existing
                : new McpToolMemberViewModel(_scope, Name, tool.Name, tool.Description ?? string.Empty, IsEnabled));
        }
        LoadedToolCount = live.Count;
    }

    /// <summary>Re-describes every sub-tool after this server's switch moved.</summary>
    internal void DescribeTools()
    {
        foreach (var tool in Tools) tool.DescribeServerState(IsEnabled);
    }

    /// <summary>Tears the connection down. The switch state is untouched — the server comes back as it was.</summary>
    public async Task UnloadAsync()
    {
        await _scope.UnloadServerAsync(Name).ConfigureAwait(false);
        Tools.Clear();
        LoadedToolCount = 0;
    }

    /// <summary>Connects the server again using the configuration the host registered under this name.</summary>
    public async Task ReloadAsync()
    {
        var config = _scope.RegisteredServers.FirstOrDefault(
            c => string.Equals(c.Name, Name, StringComparison.OrdinalIgnoreCase));
        if (config is null) return;
        // AddAsync, not LoadAsync: LoadAsync resets the whole status list, which would drop every other
        // server's row out from under the panel.
        await _scope.AddAsync(config).ConfigureAwait(false);
    }
}
