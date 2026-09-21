using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using VeloxDev.MVVM;

namespace VeloxDev.AI.MCP;

/// <summary>
/// Bindable status view-model for a single MCP server. Follows the framework's <c>[VeloxProperty]</c>
/// MVVM conventions; property changes are broadcast by the generator via
/// <see cref="INotifyPropertyChanged"/> and can be bound directly to the UI.
/// </summary>
public partial class McpServerStatusViewModel
{
    [VeloxProperty] private string name = string.Empty;
    [VeloxProperty] private string description = string.Empty;
    [VeloxProperty] private McpServerRunMode runMode = McpServerRunMode.Npm;
    [VeloxProperty] private McpServerStatus state = McpServerStatus.NotStarted;
    [VeloxProperty] private int toolCount = 0;
    [VeloxProperty] private string? error = null;
    [VeloxProperty] private string? endpoint = null;

    /// <summary>
    /// Whether this server's tools reach the Agent.
    /// <para>
    /// Deliberately independent of <see cref="State"/>, and the same distinction
    /// <see cref="Skills.SkillStatusViewModel.IsEnabled"/> draws: a disabled server stays
    /// <see cref="McpServerStatus.Connected"/> and keeps its tools loaded, but none of them is offered to
    /// the model. That is a different operation from <see cref="McpScope.UnloadServer"/>, which tears the
    /// connection down — disabling is instant and reversible, unloading is not.
    /// </para>
    /// </summary>
    [VeloxProperty] private bool isEnabled = true;

    /// <summary>Connected (tools available).</summary>
    public bool IsConnected => State == McpServerStatus.Connected;

    /// <summary>Connected and switched on — the only state in which this server's tools are offered.</summary>
    public bool IsActive => IsConnected && IsEnabled;

    /// <summary>Installing the runtime (local npm/pip).</summary>
    public bool IsInstalling => State == McpServerStatus.Installing;

    /// <summary>Connecting (launching process / handshaking with remote).</summary>
    public bool IsConnecting => State == McpServerStatus.Connecting;

    /// <summary>Load failed (see <see cref="Error"/>).</summary>
    public bool IsError => State == McpServerStatus.Error;

    /// <summary>Chinese display text for the status (directly usable by UI / Agent).</summary>
    public string StateText => State switch
    {
        McpServerStatus.Connected => "已连接",
        McpServerStatus.Installing => "安装中",
        McpServerStatus.Connecting => "连接中",
        McpServerStatus.Error => "错误",
        _ => "未启动",
    };

    partial void OnStateChanged(McpServerStatus oldValue, McpServerStatus newValue)
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsInstalling));
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(StateText));
    }

    partial void OnIsEnabledChanged(bool oldValue, bool newValue)
        => OnPropertyChanged(nameof(IsActive));
}

/// <summary>
/// An immutable copy of one server's state, safe to read from any thread.
/// <para>
/// Carries everything a <see cref="McpServerStatusViewModel"/> needs to be rebuilt, not just the
/// aggregates: a scope built from a snapshot (a spawned child's view of its parent's servers) has no
/// tracked servers to point at, so its rows have to be reconstructed out of these fields.
/// </para>
/// </summary>
public sealed class McpServerSummary
{
    /// <summary>Server name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Operator-facing description of what the server is for.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>How the server is launched (npm / pip / remote / …).</summary>
    public McpServerRunMode RunMode { get; set; } = McpServerRunMode.Npm;

    /// <summary>Raw lifecycle state, for readers that branch rather than display.</summary>
    public McpServerStatus State { get; set; } = McpServerStatus.NotStarted;

    /// <summary>Localized state text (see <see cref="McpServerStatusViewModel.StateText"/>).</summary>
    public string StateText { get; set; } = string.Empty;

    /// <summary>Number of tools the server currently exposes.</summary>
    public int ToolCount { get; set; }

    /// <summary>Load failure text, when <see cref="State"/> is <see cref="McpServerStatus.Error"/>.</summary>
    public string? Error { get; set; }

    /// <summary>Remote endpoint, when the server is not launched locally.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Whether the server's tools currently reach the Agent (see <see cref="McpServerStatusViewModel.IsEnabled"/>).</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Connected and switched on — the only state in which this server's tools are offered.</summary>
    public bool IsActive => State == McpServerStatus.Connected && IsEnabled;
}

/// <summary>
/// Globally bindable MCP server status view-model: the host UI binds <see cref="Servers"/> to show
/// each server's alive/installing/connecting/error status, and reads the aggregate counts
/// (<see cref="ConnectedCount"/>/<see cref="ErrorCount"/>). Held by <see cref="McpScope.Status"/>
/// and driven during <see cref="McpScope.LoadAsync"/>.
/// </summary>
public partial class McpStatusViewModel
{
    // Servers / IsLoading are generated by the generator from the [VeloxProperty] fields below
    // (do not manually declare same-named properties).
    [VeloxProperty] private ObservableCollection<McpServerStatusViewModel> servers = [];
    [VeloxProperty] private bool isLoading = false;

    private volatile McpServerSummary[] _snapshot = [];

    /// <summary>
    /// Immutable copy of <see cref="Servers"/>, republished on the thread the collection is bound to
    /// whenever a tracked server changes.
    /// <para>
    /// Read this — not <see cref="Servers"/> — from anywhere that is not the bound thread. An agent
    /// invocation renders its prompt on a thread of the framework's choosing, and enumerating an
    /// <see cref="ObservableCollection{T}"/> there races the host's UI.
    /// </para>
    /// </summary>
    public IReadOnlyList<McpServerSummary> Snapshot => _snapshot;

    /// <summary>Number of connected (alive) servers.</summary>
    public int ConnectedCount => Count(McpServerStatus.Connected);

    /// <summary>Number of failed servers.</summary>
    public int ErrorCount => Count(McpServerStatus.Error);

    /// <summary>Number of servers installing or connecting.</summary>
    public int WorkingCount => Servers.Count(s => s.State is McpServerStatus.Installing or McpServerStatus.Connecting);

    /// <summary>Whether all servers are ready (no installing/connecting/error).</summary>
    public bool IsAllReady => Servers.Count > 0 && Servers.Count == ConnectedCount;

    /// <summary>Whether at least one server has failed.</summary>
    public bool HasError => ErrorCount > 0;

    private int Count(McpServerStatus state) => Servers.Count(s => s.State == state);

    /// <summary>
    /// Registers a server and subscribes to its status changes so the aggregate counts refresh
    /// when any server changes. Should be called on the UI thread (or marshalled via
    /// <see cref="McpScope.WithSynchronizationContext"/>).
    /// </summary>
    public void Track(McpServerStatusViewModel server)
    {
        if (server is null) return;
        server.PropertyChanged += OnServerPropertyChanged;
        Servers.Add(server);
        NotifyAggregates();
    }

    /// <summary>Sets the batch-loading flag and refreshes the aggregates.</summary>
    public void SetLoading(bool loading)
    {
        IsLoading = loading;
        NotifyAggregates();
    }

    /// <summary>Clears all server status (call before reloading).</summary>
    public void Reset()
    {
        foreach (var s in Servers)
            s.PropertyChanged -= OnServerPropertyChanged;
        Servers.Clear();
        NotifyAggregates();
    }

    private void OnServerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // ToolCount, Name and IsEnabled are part of the snapshot even though they are not part of the counts.
        if (e.PropertyName is nameof(McpServerStatusViewModel.State)
            or nameof(McpServerStatusViewModel.ToolCount)
            or nameof(McpServerStatusViewModel.Name)
            or nameof(McpServerStatusViewModel.IsEnabled))
            NotifyAggregates();
    }

    private void NotifyAggregates()
    {
        // Republish the snapshot here: every mutation of Servers and of a tracked server's observable
        // fields funnels through this method, and it always runs on the bound thread.
        _snapshot = [.. Servers.Select(s => new McpServerSummary
        {
            Name = s.Name,
            Description = s.Description,
            RunMode = s.RunMode,
            State = s.State,
            StateText = s.StateText,
            ToolCount = s.ToolCount,
            Error = s.Error,
            Endpoint = s.Endpoint,
            IsEnabled = s.IsEnabled,
        })];

        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(ConnectedCount));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(WorkingCount));
        OnPropertyChanged(nameof(IsAllReady));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsLoading));
    }
}
