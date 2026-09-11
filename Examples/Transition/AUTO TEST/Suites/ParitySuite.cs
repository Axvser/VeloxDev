using VeloxDev.AT.Drivers;

namespace VeloxDev.AT.Suites;

/// <summary>
/// Checks the scenario tables against each other rather than against a demo. No single platform's suite can see
/// this: each one only knows its own table, so a platform that quietly stops declaring a scenario, or declares a
/// token nobody else uses, stays invisible until someone reads all seven side by side.
/// </summary>
/// <remarks>
/// Deliberately not gated behind <c>VELOXDEV_AT</c>: it launches nothing and reads no desktop, so it is worth running
/// on a machine that cannot run the UI suites at all.
/// </remarks>
[TestClass]
public class ParitySuite
{
    private static IReadOnlyList<IDemoDriver> AllDrivers()
        => [.. DemoCatalog.Platforms.Select(DemoCatalog.Create)];

    [TestMethod]
    public void EveryPlatform_DeclaresTheSameScenarios()
    {
        // The vocabulary has to be one vocabulary. A platform missing a scenario is coverage that silently stopped
        // existing; a platform inventing one is a test nobody else runs.
        var reference = default(HashSet<ScenarioId>);

        foreach (var driver in AllDrivers())
        {
            var ids = driver.Scenarios.Select(scenario => scenario.Id).ToHashSet();
            reference ??= ids;

            CollectionAssert.AreEquivalent(reference.ToList(), ids.ToList(),
                $"{driver.Platform} declares {string.Join(", ", ids)} where the others declare {string.Join(", ", reference)}");
        }
    }

    [TestMethod]
    public void EveryPlatform_UsesTheSamePayloadTokenForTheSameScenario()
    {
        // The token is what a test reads out of the payload to know which scenario just ran, so it must not vary by
        // platform: WinForms and Blazor both substitute the gradient scenario, and both keep the same token for it.
        var reference = new Dictionary<ScenarioId, string>();

        foreach (var driver in AllDrivers())
        {
            foreach (var scenario in driver.Scenarios)
            {
                if (reference.TryGetValue(scenario.Id, out var token))
                {
                    Assert.AreEqual(token, scenario.PayloadToken,
                        $"{driver.Platform} reports '{scenario.PayloadToken}' for {scenario.Id} where the others report '{token}'");
                }
                else
                {
                    reference[scenario.Id] = scenario.PayloadToken;
                }
            }
        }
    }

    [TestMethod]
    public void EveryPlatform_AgreesOnThePayloadVersion()
    {
        foreach (var driver in AllDrivers())
        {
            Assert.AreEqual(1, driver.PayloadVersion, $"{driver.Platform} declares payload version {driver.PayloadVersion}");
        }
    }

    [TestMethod]
    public void EveryScalarScenario_HasABoundThatCanTellAnOvershootFromNoMovement()
    {
        // Exercised here across all seven tables at once, because the guard throws rather than returning a meaningless
        // number: a bound that does not clear its own target would let the corresponding UI assertion pass with the
        // animation never having moved past it.
        foreach (var driver in AllDrivers())
        {
            foreach (var scenario in driver.Scenarios.Where(scenario => scenario.Scalar is not null))
            {
                var scalar = scenario.Scalar!;
                var threshold = scenario.OvershootThreshold;

                Assert.IsTrue(threshold > scalar.Target,
                    $"{driver.Platform}/{scenario.Id}: bound {threshold:F3} does not clear target {scalar.Target:F3}");
            }
        }
    }

    [TestMethod]
    public void EveryScenario_IsClickableAndDistinctlyIdentified()
    {
        foreach (var driver in AllDrivers())
        {
            var buttons = driver.Scenarios.Select(scenario => scenario.ButtonAutomationId).ToList();

            CollectionAssert.AllItemsAreUnique(buttons, $"{driver.Platform} reuses a button automation id");
            Assert.IsTrue(buttons.All(button => button.StartsWith("over.btn.", StringComparison.Ordinal)),
                $"{driver.Platform} has a button outside the over.btn.* vocabulary: {string.Join(", ", buttons)}");

            foreach (var scenario in driver.Scenarios)
            {
                Assert.IsTrue(scenario.Duration > TimeSpan.Zero, $"{driver.Platform}/{scenario.Id} has no duration");
                if (scenario.Scalar is { } scalar)
                {
                    Assert.IsTrue(scalar.ReadoutInterval > TimeSpan.Zero, $"{driver.Platform}/{scenario.Id} has no readout interval");
                }
            }
        }
    }
}
