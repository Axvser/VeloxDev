using System;
using System.Collections.Generic;
using System.Linq;

namespace VeloxDev.AI.Safety;

/// <summary>
/// The session's permission policy: one mode, a list of rules, and — in
/// <see cref="AgentPermissionMode.Auto"/> — a judge the host supplies.
/// </summary>
/// <remarks>
/// <para>
/// Immutable. Every change returns a new policy, so a reader evaluating a call holds one consistent set of
/// rules for the whole call and needs no lock; the scope swaps the reference atomically.
/// </para>
/// <para>
/// <b>The evaluation order is the contract</b>, most powerful first:
/// </para>
/// <list type="number">
/// <item><description>A <see cref="PermissionDecision.Deny"/> rule — unconditional, and the reason
/// <see cref="AgentPermissionMode.Bypass"/> is not "the safety system off".</description></item>
/// <item><description><see cref="AgentPermissionMode.Plan"/>'s refusals. Also unconditional: an
/// <c>Allow</c> rule cannot open a graph edit while the session is planning, because "nothing changes while I
/// plan" is the promise the mode makes. Only <see cref="AgentActionCategory.Read"/> and
/// <see cref="AgentActionCategory.Interact"/> survive it.</description></item>
/// <item><description>An <see cref="PermissionDecision.Ask"/> rule.</description></item>
/// <item><description>An <see cref="PermissionDecision.Allow"/> rule.</description></item>
/// <item><description>What the mode says about that category — see <see cref="ModeDefault"/>.</description></item>
/// </list>
/// </remarks>
public sealed class AgentPermissionPolicy
{
    private readonly AgentPermissionRule[] _rules;

    private AgentPermissionPolicy(
        AgentPermissionMode mode,
        AgentPermissionRule[] rules,
        Func<ToolInvocation, PermissionDecision>? judge)
    {
        Mode = mode;
        _rules = rules;
        Judge = judge;
    }

    /// <summary>The mode in force.</summary>
    public AgentPermissionMode Mode { get; }

    /// <summary>The host's rules, in the order they were added.</summary>
    public IReadOnlyList<AgentPermissionRule> Rules => _rules;

    /// <summary>Decides calls the rules and the mode do not settle, in <see cref="AgentPermissionMode.Auto"/>.</summary>
    public Func<ToolInvocation, PermissionDecision>? Judge { get; }

    /// <summary>The policy a session starts on.</summary>
    /// <remarks>
    /// <see cref="AgentPermissionMode.Auto"/> rather than <see cref="AgentPermissionMode.Manual"/>, and the
    /// reason is what the library did before this existed: graph work ran without asking (the old tool-approval
    /// gate defaulted off) and only the server/curation surface was gated. <c>Auto</c> keeps that promise for
    /// the graph and moves the asking to exactly where it was missing — changing what the session may reach.
    /// A host that wants every edit confirmed asks for <see cref="AgentPermissionMode.Manual"/>, which is
    /// unusable by default because an editing agent cannot get anything done past a dialog it has no UI for.
    /// </remarks>
    public static AgentPermissionMode DefaultMode => AgentPermissionMode.Auto;

    /// <summary>A policy with no rules and no judge, in <paramref name="mode"/>.</summary>
    /// <param name="mode">The mode to start in.</param>
    /// <returns>The policy.</returns>
    public static AgentPermissionPolicy For(AgentPermissionMode mode) => new(mode, [], null);

    /// <summary>This policy, in another mode.</summary>
    /// <param name="mode">The mode to move to.</param>
    /// <returns>The new policy; this one is unchanged.</returns>
    public AgentPermissionPolicy WithMode(AgentPermissionMode mode) => new(mode, _rules, Judge);

    /// <summary>This policy, with one more rule. Rules are evaluated by decision, not by position.</summary>
    /// <param name="rule">The rule to add.</param>
    /// <returns>The new policy.</returns>
    public AgentPermissionPolicy WithRule(AgentPermissionRule rule)
        => new(Mode, [.. _rules, rule], Judge);

    /// <summary>This policy, with more rules.</summary>
    /// <param name="rules">The rules to add.</param>
    /// <returns>The new policy.</returns>
    public AgentPermissionPolicy WithRules(IEnumerable<AgentPermissionRule> rules)
        => new(Mode, [.. _rules, .. rules], Judge);

    /// <summary>This policy, with no rules. The mode is untouched.</summary>
    /// <returns>The new policy.</returns>
    public AgentPermissionPolicy WithoutRules() => new(Mode, [], Judge);

    /// <summary>This policy, with a judge for <see cref="AgentPermissionMode.Auto"/>.</summary>
    /// <param name="judge">The judge, or <see langword="null"/> to fall back to the manual default.</param>
    /// <returns>The new policy.</returns>
    public AgentPermissionPolicy WithJudge(Func<ToolInvocation, PermissionDecision>? judge)
        => new(Mode, _rules, judge);

    /// <summary>Decides one call.</summary>
    /// <param name="invocation">The call about to run.</param>
    /// <returns>What to do with it.</returns>
    public PermissionDecision Evaluate(ToolInvocation invocation)
    {
        if (invocation is null) throw new ArgumentNullException(nameof(invocation));

        if (Has(PermissionDecision.Deny, invocation)) return PermissionDecision.Deny;
        if (Mode == AgentPermissionMode.Plan && ModeDefault(Mode, invocation.Category) == PermissionDecision.Deny)
            return PermissionDecision.Deny;
        if (Has(PermissionDecision.Ask, invocation)) return PermissionDecision.Ask;
        if (Has(PermissionDecision.Allow, invocation)) return PermissionDecision.Allow;

        if (Mode == AgentPermissionMode.Auto && Judge is { } judge)
        {
            try
            {
                return judge(invocation);
            }
            catch (Exception)
            {
                // A judge that cannot answer must not answer "yes". Nothing here is worth failing open for.
                return PermissionDecision.Deny;
            }
        }

        return ModeDefault(Mode, invocation.Category);
    }

    /// <summary>What a mode says about a category, with no rules and no judge in play.</summary>
    /// <param name="mode">The mode.</param>
    /// <param name="category">What the tool does.</param>
    /// <returns>The decision for that cell of the matrix.</returns>
    public static PermissionDecision ModeDefault(AgentPermissionMode mode, AgentActionCategory category)
    {
        var read = category is AgentActionCategory.Read or AgentActionCategory.Interact;

        return mode switch
        {
            AgentPermissionMode.Plan => read ? PermissionDecision.Allow : PermissionDecision.Deny,
            AgentPermissionMode.Manual => read ? PermissionDecision.Allow : PermissionDecision.Ask,
            AgentPermissionMode.AutoEdit => category is AgentActionCategory.Read or AgentActionCategory.Interact
                or AgentActionCategory.Edit ? PermissionDecision.Allow : PermissionDecision.Ask,
            AgentPermissionMode.Auto => category == AgentActionCategory.Curate
                ? PermissionDecision.Ask
                : PermissionDecision.Allow,
            _ => PermissionDecision.Allow,
        };
    }

    private bool Has(PermissionDecision decision, ToolInvocation invocation)
        => _rules.Any(rule => rule.Decision == decision && rule.Matches(invocation));
}
