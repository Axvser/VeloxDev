using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using VeloxDev.AI.MCP;
using VeloxDev.AI.Skills;
using VeloxDev.AI.Workflow;
using VeloxDev.MVVM;

namespace VeloxDev.AI.Dashboard;

/// <summary>
/// The host-facing panel over one <see cref="WorkflowAgentScope"/>: what the Agent can currently do, how
/// busy each piece is, and a switch per member.
/// <list type="bullet">
///   <item><see cref="SystemTools"/> — the workflow built-ins plus developer-registered tools.</item>
///   <item><see cref="SkillTools"/> — the four skill-management tools.</item>
///   <item><see cref="McpServers"/> — one row per configured server, each expandable to its tools.</item>
///   <item><see cref="Skills"/> — the discovered skills.</item>
/// </list>
/// <para>
/// <b>Threading.</b> The dashboard captures the <see cref="SynchronizationContext"/> it is constructed on
/// and marshals every scope-driven update onto it, so it can be built on the UI thread and still follow a
/// scope that the Agent drives from somewhere else. With no context captured it is fully synchronous,
/// which is what makes it testable without a dispatcher.
/// </para>
/// <para>
/// <b>Rebuild coalescing.</b> <see cref="WorkflowAgentScope.Changed"/> is raised by every configuring
/// <c>With*</c> call — <c>WithAutoDiscovery</c> alone raises it once per discovered type. Rebuilds are
/// therefore coalesced into a single posted pass rather than run per event.
/// </para>
/// </summary>
public sealed partial class AgentDashboardViewModel : IDisposable
{
    private readonly WorkflowAgentScope _scope;
    private readonly SynchronizationContext? _ui;

    private bool _rebuildQueued;
    private bool _disposed;

    private ObservableCollection<SkillStatusViewModel>? _skillRows;
    private ObservableCollection<McpServerStatusViewModel>? _mcpRows;

    [VeloxProperty] private ObservableCollection<ToolMemberViewModel> systemTools = [];
    [VeloxProperty] private ObservableCollection<ToolMemberViewModel> skillTools = [];
    [VeloxProperty] private ObservableCollection<McpServerMemberViewModel> mcpServers = [];
    [VeloxProperty] private ObservableCollection<SkillMemberViewModel> skills = [];
    [VeloxProperty] private string summaryText = string.Empty;

    private AgentDashboardViewModel(WorkflowAgentScope scope)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _ui = SynchronizationContext.Current;

        _scope.Changed += OnScopeChanged;
        _scope.ToolCalled += OnToolCalled;

        Rebuild();
    }

    /// <summary>
    /// Builds the panel over <paramref name="scope"/>. The scope is normally read directly, so it does not
    /// have to be configured beforehand — a <c>With*</c> call afterwards arrives through
    /// <see cref="WorkflowAgentScope.Changed"/> and the panel follows.
    /// </summary>
    public static AgentDashboardViewModel Create(WorkflowAgentScope scope) => new(scope);

    /// <summary>Tools switched on, over tools offered.</summary>
    public string SystemToolSummary => $"{SystemTools.Count(t => t.IsEnabled)}/{SystemTools.Count}";

    /// <summary>Skills switched on, over skills discovered.</summary>
    public string SkillSummary => $"{Skills.Count(s => s.IsEnabled)}/{Skills.Count}";

    /// <summary>Servers connected, over servers configured.</summary>
    public string McpSummary => $"{McpServers.Count(s => s.IsConnected)}/{McpServers.Count}";

    /// <summary>
    /// Alive / failed split, the aggregate the MCP rows roll up to. Counted from the servers' own states,
    /// which are exclusive — counting "has an error message" would let one server appear in both buckets.
    /// </summary>
    public string McpStatusText =>
        $"存活 {McpServers.Count(s => s.IsConnected)} · 错误 {McpServers.Count(s => s.IsFailedState)}";

    /// <summary>Whether a server is mid-connect, so a reload would be fighting one already in flight.</summary>
    public bool CanReloadMcp => _scope.Mcp is not null && McpServers.All(s => !s.IsWorking);

    /// <summary>
    /// Reconnects every server the host registered. This is the aggregate counterpart of a server row's own
    /// reload, and replaces the standalone MCP panel's 重载 button.
    /// </summary>
    public async System.Threading.Tasks.Task ReloadAllServersAsync()
    {
        if (_scope.Mcp is null) return;
        // LoadAsync, not AddAsync: the point of the aggregate action is to re-establish the whole set, and
        // it re-tracks every server's status row itself.
        await _scope.Mcp.LoadAsync(_scope.Mcp.RegisteredServers).ConfigureAwait(false);
    }

    /// <summary>Clients that connected, were switched off, and fell back to a stopwatch.</summary>
    public string CallSummary => $"{AllMembers().Sum(m => m.CallCount)} 次调用";

    // ── Wiring ──────────────────────────────────────────────────────────────

    private void OnScopeChanged(object? sender, EventArgs e) => QueueRebuild();

    private void OnToolCalled(object? sender, AgentToolCallEventArgs e)
    {
        // This runs inside TrackedAIFunction's try block, whose catch turns any exception into the tool's
        // error result — so a throw here would be reported to the model as the tool failing. Never throw.
        try { Post(() => RecordCall(e.ToolName)); }
        catch { /* a panel must not be able to break a tool call */ }
    }

    private void RecordCall(string toolName)
    {
        var member = AllMembers().FirstOrDefault(
            m => string.Equals(m.Name, toolName, StringComparison.OrdinalIgnoreCase));
        member?.RecordCall(DateTime.Now);
        OnPropertyChanged(nameof(CallSummary));
    }

    private IEnumerable<AgentMemberViewModel> AllMembers()
    {
        foreach (var t in SystemTools) yield return t;
        foreach (var t in SkillTools) yield return t;
        foreach (var s in McpServers)
        {
            yield return s;
            foreach (var t in s.Tools) yield return t;
        }
        foreach (var s in Skills) yield return s;
    }

    private void Post(Action action)
    {
        var ui = _ui;
        if (ui is null || ReferenceEquals(ui, SynchronizationContext.Current)) { action(); return; }
        ui.Post(_ => action(), null);
    }

    /// <summary>
    /// Queues one rebuild. Safe to call from any thread; at most one pass is pending at a time, so a burst
    /// of <see cref="WorkflowAgentScope.Changed"/> events costs one rebuild rather than one each.
    /// </summary>
    private void QueueRebuild()
    {
        if (_disposed || _rebuildQueued) return;
        _rebuildQueued = true;
        Post(() =>
        {
            _rebuildQueued = false;
            if (_disposed) return;
            Rebuild();
        });
    }

    // ── Rebuild ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-reads every member from the scope. Public so a host can force a pass after changing a scope
    /// directly, and so a test can drive it deterministically.
    /// </summary>
    public void Rebuild()
    {
        RebuildSystemTools();
        RebuildSkillTools();
        BindSkills();
        BindMcp();
        RebuildSkills();
        RebuildMcpServers();
        NotifyAggregates();
    }

    private void RebuildSystemTools()
    {
        // CreateAllTools, not CreateTools: the panel has to list the switched-off ones too, or nothing
        // could ever be switched back on. Same for the four skill tools below.
        var live = _scope.CreateToolkit().CreateAllTools();
        SyncTools(SystemTools, live);
    }

    private void RebuildSkillTools()
    {
        var tools = _scope.Skills is null
            ? []
            : _scope.CreateSkillToolkit().CreateTools();

        SyncTools(SkillTools, tools);
    }

    private void SyncTools(ObservableCollection<ToolMemberViewModel> target, IEnumerable<Microsoft.Extensions.AI.AITool> tools)
    {
        var kept = target.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        target.Clear();
        foreach (var tool in tools)
            target.Add(kept.TryGetValue(tool.Name, out var existing)
                ? existing
                : new ToolMemberViewModel(_scope, tool.Name, tool.Description ?? string.Empty, GateNote(tool.Name)));
    }

    /// <summary>
    /// Why a registered tool still refuses. These gates live in code rather than in the tool set — the tool
    /// is offered and returns "disabled by host policy" — so the panel says so instead of showing a green
    /// row for something the model cannot actually use.
    /// </summary>
    private string GateNote(string toolName)
    {
        switch (toolName)
        {
            case "ExecuteNode":
            case "ExecuteNodes":
            case "BroadcastNode":
            case "ReverseBroadcastNode":
            case "RunCompiledWorkflow":
            case "GetNodeResult":
                return _scope.AllowNodeExecution ? string.Empty : "宿主未开启节点执行（WithAllowNodeExecution）";
            case "ExecuteCommandOnNode":
            case "ExecuteCommandById":
                return _scope.HasAllowedGenericCommands ? string.Empty : "宿主未放行任何通用命令（WithAllowedGenericCommands）";
            default:
                return string.Empty;
        }
    }

    // ── Skills ──────────────────────────────────────────────────────────────

    private void BindSkills()
    {
        var rows = _scope.Skills?.Status.Skills;
        if (ReferenceEquals(rows, _skillRows)) return;

        if (_skillRows is not null)
        {
            _skillRows.CollectionChanged -= OnSkillRowsChanged;
            foreach (var row in _skillRows) row.PropertyChanged -= OnSkillRowChanged;
        }

        _skillRows = rows;

        if (_skillRows is not null)
        {
            _skillRows.CollectionChanged += OnSkillRowsChanged;
            foreach (var row in _skillRows) row.PropertyChanged += OnSkillRowChanged;
        }
    }

    private void OnSkillRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Refresh() and SetEnabled both go through Status.Reset(), so Add/Remove alone would leave the
        // panel bound to rows the scope has already discarded.
        if (e.OldItems is not null)
            foreach (SkillStatusViewModel row in e.OldItems) row.PropertyChanged -= OnSkillRowChanged;
        if (e.NewItems is not null)
            foreach (SkillStatusViewModel row in e.NewItems) row.PropertyChanged += OnSkillRowChanged;

        if (e.Action == NotifyCollectionChangedAction.Reset) BindSkills();
        QueueRebuild();
    }

    private void OnSkillRowChanged(object? sender, PropertyChangedEventArgs e) => QueueRebuild();

    private void RebuildSkills()
    {
        var rows = _scope.Skills?.Status.Skills;
        if (rows is null) { Skills.Clear(); return; }

        var kept = Skills.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        Skills.Clear();
        foreach (var row in rows)
        {
            if (kept.TryGetValue(row.Name, out var existing)) { existing.Sync(row); Skills.Add(existing); }
            else Skills.Add(new SkillMemberViewModel(_scope.Skills!, row));
        }
    }

    // ── MCP ─────────────────────────────────────────────────────────────────

    private void BindMcp()
    {
        var rows = _scope.Mcp?.Status.Servers;
        if (ReferenceEquals(rows, _mcpRows)) return;

        if (_mcpRows is not null)
        {
            _mcpRows.CollectionChanged -= OnMcpRowsChanged;
            foreach (var row in _mcpRows) row.PropertyChanged -= OnMcpRowChanged;
        }

        _mcpRows = rows;

        if (_mcpRows is not null)
        {
            _mcpRows.CollectionChanged += OnMcpRowsChanged;
            foreach (var row in _mcpRows) row.PropertyChanged += OnMcpRowChanged;
        }
    }

    private void OnMcpRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // LoadAsync() calls Status.Reset(), which the demo hits on every reload.
        if (e.OldItems is not null)
            foreach (McpServerStatusViewModel row in e.OldItems) row.PropertyChanged -= OnMcpRowChanged;
        if (e.NewItems is not null)
            foreach (McpServerStatusViewModel row in e.NewItems) row.PropertyChanged += OnMcpRowChanged;

        if (e.Action == NotifyCollectionChangedAction.Reset) BindMcp();
        QueueRebuild();
    }

    private void OnMcpRowChanged(object? sender, PropertyChangedEventArgs e) => QueueRebuild();

    private void RebuildMcpServers()
    {
        var rows = _scope.Mcp?.Status.Servers;
        if (rows is null) { McpServers.Clear(); return; }

        var kept = McpServers.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        McpServers.Clear();
        foreach (var row in rows)
        {
            var member = kept.TryGetValue(row.Name, out var existing)
                ? existing
                : new McpServerMemberViewModel(_scope.Mcp!, row);
            member.Sync(row);
            member.RebuildTools();
            member.DescribeTools();
            McpServers.Add(member);
        }
    }

    private void NotifyAggregates()
    {
        SummaryText = $"工具 {SystemToolSummary} · Skill {SkillSummary} · MCP {McpStatusText} · {CallSummary}";
        OnPropertyChanged(nameof(SystemToolSummary));
        OnPropertyChanged(nameof(SkillSummary));
        OnPropertyChanged(nameof(McpSummary));
        OnPropertyChanged(nameof(McpStatusText));
        OnPropertyChanged(nameof(CanReloadMcp));
        OnPropertyChanged(nameof(CallSummary));
    }

    /// <summary>Detaches from the scope. Call when the view that owns the panel goes away.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _scope.Changed -= OnScopeChanged;
        _scope.ToolCalled -= OnToolCalled;

        if (_skillRows is not null)
        {
            _skillRows.CollectionChanged -= OnSkillRowsChanged;
            foreach (var row in _skillRows) row.PropertyChanged -= OnSkillRowChanged;
        }
        if (_mcpRows is not null)
        {
            _mcpRows.CollectionChanged -= OnMcpRowsChanged;
            foreach (var row in _mcpRows) row.PropertyChanged -= OnMcpRowChanged;
        }
    }
}
