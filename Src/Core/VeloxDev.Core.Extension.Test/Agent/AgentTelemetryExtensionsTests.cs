using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.Core.Extension.Test.Agent.Workflow;

namespace VeloxDev.Core.Extension.Test.Agent;

/// <summary>
/// Covers the two promises <see cref="AgentTelemetryExtensions"/> makes: a run is recorded under the source
/// the host names, and the recording carries no prompt text unless the host asked for it.
/// </summary>
[TestClass]
public class AgentTelemetryExtensionsTests
{
    private const string Source = "veloxdev-test-agent-telemetry";

    /// <summary>
    /// Listens for <see cref="Source"/> and collects what stopped. The listener is returned rather than
    /// created inline because <see cref="ActivitySource"/> holds it weakly — a listener nothing references
    /// stops being called, and the assertion would then pass or fail for the wrong reason.
    /// </summary>
    private static ActivityListener Listen(ICollection<Activity> activities)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static AIAgent BuildAgent() =>
        new ChatClientAgent(new RecordingChatClient(), new ChatClientAgentOptions())
            .WithAgentTelemetry(Source);

    [TestMethod]
    public async Task WithAgentTelemetry_RecordsARunUnderTheGivenSource()
    {
        var activities = new List<Activity>();
        using var listener = Listen(activities);

        var response = await BuildAgent().RunAsync("hello");

        Assert.IsNotNull(response);
        Assert.IsTrue(activities.Count > 0,
            "a run must produce at least one activity under the source the host named — otherwise the host's "
            + "AddSource registration has nothing to receive");
    }

    [TestMethod]
    public async Task WithAgentTelemetry_KeepsPromptTextOutOfTheSpansByDefault()
    {
        var activities = new List<Activity>();
        using var listener = Listen(activities);

        const string sentinel = "PROMPT-SENTINEL-8134";
        await BuildAgent().RunAsync(sentinel);

        Assert.IsTrue(activities.Count > 0, "the run has to be recorded for this test to mean anything");
        var leaked = activities
            .SelectMany(a => a.TagObjects)
            .Where(t => (t.Value?.ToString() ?? string.Empty).Contains(sentinel))
            .Select(t => t.Key)
            .ToArray();

        Assert.AreEqual(0, leaked.Length,
            $"EnableSensitiveData defaults to off, so no tag may carry the prompt; found it on: {string.Join(", ", leaked)}");
    }
}
