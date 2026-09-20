using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VeloxDev.AI.Pipelines;
using VeloxDev.MVVM;

namespace VeloxDev.AI.SubAgents;

/// <summary>
/// One spawned sub-agent, as a bindable row.
/// <para>
/// Written on whatever thread the roster is marshalled to — a spawn runs inside a tool body that the
/// composing host marshalled, and a completion is posted back to the same context — and read by the
/// tree panel, which lives on that thread. Anything off it reads <see cref="SubAgentSummary"/> instead.
/// </para>
/// <para>
/// The two halves are kept apart on purpose: this type is the model's and the host's view of the same
/// child, and <see cref="GrantedTools"/>/<see cref="DroppedRequests"/> are populated at spawn time and
/// never revised, because rewriting what a running child was granted would be a lie about its history.
/// </para>
/// </summary>
public sealed partial class SubAgentStatusViewModel
{
    [VeloxProperty] private string name = string.Empty;
    [VeloxProperty] private string task = string.Empty;
    [VeloxProperty] private SubAgentState state = SubAgentState.Queued;
    [VeloxProperty] private string result = string.Empty;
    [VeloxProperty] private string error = string.Empty;
    [VeloxProperty] private string notes = string.Empty;
    [VeloxProperty] private int callCount = 0;
    [VeloxProperty] private int? maxToolCalls = null;
    [VeloxProperty] private DateTimeOffset? startedAt = null;
    [VeloxProperty] private DateTimeOffset? finishedAt = null;
    [VeloxProperty] private ObservableCollection<string> grantedTools = [];
    [VeloxProperty] private ObservableCollection<string> droppedRequests = [];

    /// <summary>The handle every other tool takes, and the row's identity in the tree.</summary>
    public string Id { get; }

    /// <summary>The id of the scope that spawned this one, or <c>null</c> for a child of the host's scope.</summary>
    public string? ParentId { get; }

    /// <summary>1 for a child of the root scope, 2 for a grandchild, and so on.</summary>
    public int Depth { get; }

    /// <summary>
    /// The child's own conversation, once its scope has one. Held rather than copied: a panel showing what
    /// a sub-agent did binds this directly, and a second transcript would be a second source of truth.
    /// </summary>
    public AgentTranscript? Transcript { get; internal set; }

    internal SubAgentStatusViewModel(string id, string name, string? parentId, int depth, string task)
    {
        Id = id;
        ParentId = parentId;
        Depth = depth;
        Name = name;
        Task = task;
    }

    /// <summary>Chinese display text for the state, matching the other panels' convention.</summary>
    public string StateText => State switch
    {
        SubAgentState.Queued => "排队中",
        SubAgentState.Running => "运行中",
        SubAgentState.Completed => "已完成",
        SubAgentState.Failed => "失败",
        SubAgentState.Cancelled => "已取消",
        _ => "未知",
    };

    /// <summary>Still working — the only states in which cancelling means anything.</summary>
    public bool IsRunning => State is SubAgentState.Queued or SubAgentState.Running;

    /// <summary>Finished, however it finished.</summary>
    public bool IsFinished => !IsRunning;

    /// <summary>There is an error worth showing.</summary>
    public bool HasError => Error.Length > 0;

    /// <summary>There is a result worth showing.</summary>
    public bool HasResult => Result.Length > 0;

    /// <summary>Something the spawn asked for was refused — worth surfacing, not worth alarming about.</summary>
    public bool HasDroppedRequests => DroppedRequests.Count > 0;

    /// <summary>The granted capability, one line per entry, for a panel that shows it without a list control.</summary>
    public string GrantedSummary => GrantedTools.Count == 0
        ? "无工具"
        : string.Join("、", GrantedTools);

    partial void OnStateChanged(SubAgentState oldValue, SubAgentState newValue)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsFinished));
    }

    partial void OnResultChanged(string oldValue, string newValue) => OnPropertyChanged(nameof(HasResult));

    partial void OnErrorChanged(string oldValue, string newValue) => OnPropertyChanged(nameof(HasError));

    /// <summary>Called by the roster when the dropped list is filled in at spawn time.</summary>
    internal void SetDroppedRequests(IEnumerable<string> dropped)
    {
        DroppedRequests.Clear();
        foreach (var line in dropped) DroppedRequests.Add(line);
        OnPropertyChanged(nameof(HasDroppedRequests));
    }

    /// <summary>Called by the roster when the granted tool list is filled in at spawn time.</summary>
    internal void SetGrantedTools(IEnumerable<string> granted)
    {
        GrantedTools.Clear();
        foreach (var tool in granted) GrantedTools.Add(tool);
        OnPropertyChanged(nameof(GrantedSummary));
    }
}
