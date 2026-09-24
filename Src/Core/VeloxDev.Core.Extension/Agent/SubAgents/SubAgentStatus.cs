using System;
using System.Collections.Generic;

namespace VeloxDev.AI.SubAgents;

/// <summary>
/// Where a spawned sub-agent is in its life.
/// <para>
/// <see cref="Cancelled"/> is deliberately its own value rather than a flavour of <see cref="Failed"/>:
/// a child stopped by the host, by its parent, or by the tree being taken down did not go wrong, and a
/// panel that painted it red would teach the user to distrust a control that worked.
/// </para>
/// </summary>
public enum SubAgentState
{
    /// <summary>Accepted, but its background run has not started yet.</summary>
    Queued,

    /// <summary>Running in the background.</summary>
    Running,

    /// <summary>Finished and produced a result.</summary>
    Completed,

    /// <summary>Finished by throwing.</summary>
    Failed,

    /// <summary>Stopped on request — by the host, by its parent, or because the tree is being disposed.</summary>
    Cancelled,
}

/// <summary>
/// An immutable copy of one sub-agent's state, safe to read from any thread.
/// <para>
/// The same arrangement as <c>McpServerSummary</c>: a row view-model is bound to the UI thread and cannot
/// be enumerated from elsewhere, while an agent invocation and a panel's rebuild both need to read the
/// roster from threads the host does not control.
/// </para>
/// </summary>
public sealed class SubAgentSummary
{
    /// <summary>The handle a spawn returned, and what every other tool takes.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The task's display title — what the spawn asked for, or a numbered stand-in.
    /// <para>
    /// A title rather than an identifier, because its consumer is the person watching the panel rather than
    /// the model: it sits beside the state lamp on a row that deliberately shows nothing else of the task.
    /// </para>
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The id of the sub-agent that spawned this one, or <c>null</c> for a child of the scope the panel was
    /// opened on. This is the whole of the parent/child relation: the roster is flat and the tree is a
    /// projection of it.
    /// </summary>
    public string? ParentId { get; set; }

    /// <summary>1 for a child of the root scope, 2 for a grandchild, and so on.</summary>
    public int Depth { get; set; }

    /// <summary>The task the spawn was asked to carry out.</summary>
    public string Task { get; set; } = string.Empty;

    /// <summary>Where the run is.</summary>
    public SubAgentState State { get; set; }

    /// <summary>Localized state text, matching <see cref="SubAgentStatusViewModel.StateText"/>.</summary>
    public string StateText { get; set; } = string.Empty;

    /// <summary>What the child answered, once it has finished; <c>null</c> before that.</summary>
    public string? Result { get; set; }

    /// <summary>Why it failed, or <c>null</c>.</summary>
    public string? Error { get; set; }

    /// <summary>How many tool calls the child and everything it spawned have made.</summary>
    public int CallCount { get; set; }

    /// <summary>The cap on that count that was granted at spawn time, or <c>null</c> for none.</summary>
    public int? MaxToolCalls { get; set; }

    /// <summary>
    /// How many tokens the child's own runs spent, or <c>null</c> when the provider reported none.
    /// <para>
    /// This is the child's own spend, not its subtree's. A parent's total is the sum over the tree, which
    /// only the tree can compute — see <c>SubAgentTreeNodeViewModel.SubtreeTokens</c>.
    /// </para>
    /// </summary>
    public long? TokensUsed { get; set; }

    /// <summary>The prompt-side half of <see cref="TokensUsed"/>, when the provider splits it.</summary>
    public long? InputTokens { get; set; }

    /// <summary>The completion-side half of <see cref="TokensUsed"/>, when the provider splits it.</summary>
    public long? OutputTokens { get; set; }

    /// <summary>When the child's run started, or <c>null</c> before it did.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When the child's run ended, or <c>null</c> while it is still going.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>How many tools the child was actually given.</summary>
    public int GrantedToolCount { get; set; }

    /// <summary>How many skills the child was given. Zero when it has no skill provider at all.</summary>
    public int GrantedSkillCount { get; set; }

    /// <summary>How many MCP servers the child was given. Zero is the ordinary case.</summary>
    public int GrantedMcpServerCount { get; set; }

    /// <summary>
    /// What the spawn asked for and did not get, one line each. Carried into the summary rather than only
    /// into the spawn's own reply, because a panel showing a child that quietly has fewer abilities than it
    /// asked for is exactly the failure this reporting exists to prevent.
    /// </summary>
    public IReadOnlyList<string> DroppedRequests { get; set; } = [];

    /// <summary>Whether this child is still running.</summary>
    public bool IsRunning => State is SubAgentState.Queued or SubAgentState.Running;

    /// <summary>Finished, however it finished. The pair to <see cref="IsRunning"/>, so a consumer reading
    /// the summary off another thread does not have to negate it by hand — and get the negation wrong when
    /// a new state is added.</summary>
    public bool IsFinished => !IsRunning;

    /// <summary>
    /// There is an error worth showing.
    /// <para>
    /// Tested rather than compared with <c>null</c>, because <see cref="Error"/> carries the row's default
    /// of the empty string for a child that succeeded — a distinction no consumer should have to know.
    /// </para>
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>There is a result worth showing.</summary>
    public bool HasResult => !string.IsNullOrEmpty(Result);

    /// <summary>Whether the provider reported a token count. False means "not measured", not zero.</summary>
    public bool HasTokens => TokensUsed is not null;

    /// <summary>How long the child ran, or has been running. <c>null</c> before it started.</summary>
    /// <remarks>
    /// Measured against the clock while the child is running, so a caller that re-reads the same summary
    /// gets a larger span — and <see cref="SubAgentScope.Snapshot"/> does not republish merely because time
    /// passed. Read it, do not cache it.
    /// </remarks>
    public TimeSpan? Duration
    {
        get
        {
            if (StartedAt is not { } from) return null;
            var span = (FinishedAt ?? DateTimeOffset.Now) - from;
            return span < TimeSpan.Zero ? TimeSpan.Zero : span;
        }
    }
}
