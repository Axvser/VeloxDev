using System;
using System.Collections.Generic;

namespace VeloxDev.AI.Safety;

/// <summary>
/// One host-authored rule: a decision, and a pattern saying which calls it is about.
/// </summary>
/// <remarks>
/// <para>
/// A rule is the <i>fine</i> layer beneath the mode. It can be stricter than any mode — a
/// <see cref="PermissionDecision.Deny"/> rule holds even in <see cref="AgentPermissionMode.Bypass"/>, which is
/// what keeps "stop asking" from meaning "stop caring".
/// </para>
/// <para>
/// A pattern selects on one axis, and says which by its prefix:
/// </para>
/// <list type="table">
/// <item>
///   <term><c>DeleteNode</c>, <c>Get*</c>, <c>mcp:*</c></term>
///   <description>The tool's name, with <c>*</c> matching any run of characters.</description>
/// </item>
/// <item>
///   <term><c>category:Edit</c></term>
///   <description>Everything in one <see cref="AgentActionCategory"/>.</description>
/// </item>
/// <item>
///   <term><c>source:mcp:*</c>, <c>source:mcp:filesystem</c>, <c>source:skill</c></term>
///   <description>Where the tool came from.</description>
/// </item>
/// </list>
/// <para>
/// Matching is case-insensitive, and there is no escaping: a name that contains <c>*</c> cannot be matched
/// exactly. Tool names are C# identifiers, so that costs nothing.
/// </para>
/// </remarks>
/// <param name="Decision">What to do with a call this matches.</param>
/// <param name="Pattern">Which calls it is about, as described above.</param>
public sealed record AgentPermissionRule(PermissionDecision Decision, string Pattern)
{
    private const string CategoryPrefix = "category:";
    private const string SourcePrefix = "source:";

    /// <summary>Whether this rule is about <paramref name="invocation"/>.</summary>
    /// <param name="invocation">The call about to run.</param>
    /// <returns><see langword="true"/> when the rule applies.</returns>
    public bool Matches(ToolInvocation invocation)
    {
        if (invocation is null) throw new ArgumentNullException(nameof(invocation));
        if (string.IsNullOrEmpty(Pattern)) return false;

        if (Pattern.StartsWith(CategoryPrefix, StringComparison.OrdinalIgnoreCase))
            return CategoryMatches(invocation.Category, Pattern.Substring(CategoryPrefix.Length));

        if (Pattern.StartsWith(SourcePrefix, StringComparison.OrdinalIgnoreCase))
            return Wildcard(invocation.Source ?? string.Empty, Pattern.Substring(SourcePrefix.Length));

        return Wildcard(invocation.Name, Pattern);
    }

    private static bool CategoryMatches(AgentActionCategory category, string name)
        => Enum.TryParse<AgentActionCategory>(name.Trim(), ignoreCase: true, out var parsed) && parsed == category;

    /// <summary>A <c>*</c> glob over the whole string, case-insensitively.</summary>
    /// <remarks>
    /// Written out rather than compiled into a regex: this runs on every call, on model-supplied names, and a
    /// regex would allocate a matcher (and a culture-sensitive comparison) per call to answer a question whose
    /// wildcard set is one character wide.
    /// </remarks>
    internal static bool Wildcard(string value, string pattern)
    {
        var v = 0;
        var p = 0;
        var star = -1;
        var resume = 0;

        while (v < value.Length)
        {
            if (p < pattern.Length && (pattern[p] == '*' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(value[v])))
            {
                if (pattern[p] == '*')
                {
                    // Remember where to come back to if the rest fails to line up: after the star, at this value.
                    star = p++;
                    resume = v;
                    continue;
                }

                p++;
                v++;
                continue;
            }

            if (star < 0) return false;

            // Backtrack: let the star swallow one more character of the value.
            p = star + 1;
            v = ++resume;
        }

        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }
}
