using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// What a tool description tells the model to pass has to be something the tool will accept.
/// </summary>
/// <remarks>
/// <para>
/// A description is the only thing the model has to go on, so a wrong one is not a typo — it is an instruction to
/// fail. The channel arguments were the live case: three tools advertised <c>OneSender</c> / <c>ManyBoth</c> /
/// <c>MultipleSenders</c>, none of which is a <see cref="SlotChannel"/> member, so a model that followed the
/// description was told "Invalid channel", had to guess, and found <c>MultipleTargets</c> by trial. The framework
/// had been carrying both spellings since before the names settled.
/// </para>
/// <para>
/// The check is by the enum rather than by a copy of the list: every quoted token in a channel-bearing
/// description must parse as a <see cref="SlotChannel"/>. That is what makes it a guard rather than a second
/// transcription — a member renamed or removed reddens this instead of quietly going stale.
/// </para>
/// </remarks>
[TestClass]
public class ToolDescriptionAccuracyTests
{
    /// <summary>A quoted token, the way a description spells the values it takes.</summary>
    private static readonly Regex QuotedToken = new(@"'([^']+)'", RegexOptions.Compiled);

    [TestMethod]
    public void EveryChannelNameAToolDescriptionOffers_IsAChannel()
    {
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var description in ChannelBearingDescriptions())
        {
            foreach (Match match in QuotedToken.Matches(description.Text))
            {
                var offered = match.Groups[1].Value;
                if (offered.Contains(' ') || offered.Contains('.')) continue;   // prose, not a value

                inspected++;
                if (Enum.TryParse<SlotChannel>(offered, ignoreCase: false, out _)) continue;

                offenders.Add($"{description.Where} offers '{offered}'");
            }
        }

        Assert.IsGreaterThan(0, inspected,
            "no channel-bearing description was found at all — the walk is no longer looking at what it was written to look at");

        Assert.IsEmpty(offenders,
            "a description that names a value the tool rejects sends the model into guesswork. Valid SlotChannel "
            + "members: " + string.Join(", ", Enum.GetNames<SlotChannel>()) + "\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Every <c>[Description]</c> on the workflow toolkit that talks about channels, with where it sits.
    /// </summary>
    /// <remarks>
    /// Both places are walked: two of the three live on the <c>channel</c> parameter, one on the method. A scan
    /// that looked at only one would have missed most of the defect.
    /// </remarks>
    private static IEnumerable<(string Where, string Text)> ChannelBearingDescriptions()
    {
        const BindingFlags declared =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var method in typeof(WorkflowAgentToolkit).GetMethods(declared))
        {
            var onMethod = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>(inherit: false);
            if (onMethod is not null && MentionsAChannel(onMethod.Description))
                yield return ($"{method.Name} (method)", onMethod.Description);

            foreach (var parameter in method.GetParameters())
            {
                var onParameter = parameter.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>(inherit: false);
                if (onParameter is null || !MentionsAChannel(onParameter.Description)) continue;

                yield return ($"{method.Name}({parameter.Name}) (parameter)", onParameter.Description);
            }
        }
    }

    /// <summary>Whether a description is offering channel values rather than using the word incidentally.</summary>
    private static bool MentionsAChannel(string? description)
        => description is not null
           && description.Contains("Channel", StringComparison.OrdinalIgnoreCase)
           && QuotedToken.IsMatch(description);
}
