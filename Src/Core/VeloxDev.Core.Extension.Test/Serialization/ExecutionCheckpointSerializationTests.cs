using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.MVVM.Serialization;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// Persisting a run's place: the JSON form of <see cref="ExecutionCheckpoint"/> and the file-backed store.
/// <para>
/// The point of the round trip is the payloads — a checkpoint carries whatever the graph's nodes produced, and
/// <see cref="object"/>-typed values are the ones the format reshapes rather than copies: JSON has a single
/// integer type, so an integer payload comes back as a <see cref="long"/>, and a node that produced null has to
/// come back as null rather than absent. So these use payloads of several kinds rather than the strings a JSON
/// writer handles for free.
/// </para>
/// </summary>
[TestClass]
public class ExecutionCheckpointSerializationTests
{
    private static ExecutionCheckpoint Checkpoint() => new()
    {
        Attempt = 2,
        ActiveRedirectTarget = 1,
        Data = new Dictionary<string, object?> { ["n1"] = "A", ["n2"] = 42 },
        Outputs = new Dictionary<string, object?>
        {
            ["n1"] = "A",
            ["n2"] = 42,
            ["n3"] = new Dictionary<string, object?> { ["nested"] = "B" },
            ["n4"] = null,
        },
        Shape = ["n1", "n2", "n3", "n4"],
    };

    [TestMethod]
    public void ACheckpoint_RoundTripsThroughJson()
    {
        var restored = Checkpoint().SerializeCheckpoint().DeserializeCheckpoint();

        Assert.IsNotNull(restored);
        Assert.AreEqual(2, restored.Attempt);
        Assert.AreEqual(1, restored.ActiveRedirectTarget);
        CollectionAssert.AreEqual(new[] { "n1", "n2", "n3", "n4" }, restored.Shape);

        var payload = Assert.IsInstanceOfType<Dictionary<string, object?>>(restored.Data,
            "a dictionary payload has to come back as a dictionary");
        Assert.AreEqual("A", payload["n1"]);
        var n2 = Assert.IsInstanceOfType<long>(payload["n2"], "JSON has one integer type — see the note on CheckpointEx");
        Assert.AreEqual(42L, n2);

        Assert.AreEqual("A", restored.Outputs["n1"]);
        Assert.AreEqual(42L, restored.Outputs["n2"]);
        var nested = Assert.IsInstanceOfType<Dictionary<string, object?>>(restored.Outputs["n3"]);
        Assert.AreEqual("B", nested["nested"]);
        Assert.IsNull(restored.Outputs["n4"], "a node that produced null is recorded as having produced null");
    }

    [TestMethod]
    public void AnEmptyOrUnrelatedText_LoadsNothing()
    {
        Assert.IsNull("".DeserializeCheckpoint());
        Assert.IsNull("not a checkpoint".DeserializeCheckpoint());
    }

    /// <summary>A new store over the same file is the case a file store exists for: the process that took the place is gone.</summary>
    [TestMethod]
    public async Task FileCheckpointStore_KeepsThePlace_AcrossInstances()
    {
        var path = Path.Combine(Path.GetTempPath(), $"veloxdev-checkpoint-{Guid.NewGuid():N}.json");
        try
        {
            await new FileCheckpointStore(path).SaveAsync(Checkpoint(), CancellationToken.None);

            var loaded = await new FileCheckpointStore(path).LoadAsync(CancellationToken.None);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(2, loaded.Attempt);
            Assert.AreEqual("A", loaded.Outputs["n1"]);
            Assert.AreEqual(42L, loaded.Outputs["n2"]);
            Assert.IsInstanceOfType<Dictionary<string, object?>>(loaded.Outputs["n3"]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>A save replaces the last one: a store holds a place, not a history.</summary>
    [TestMethod]
    public async Task FileCheckpointStore_SaveReplacesWhatWasThere()
    {
        var path = Path.Combine(Path.GetTempPath(), $"veloxdev-checkpoint-{Guid.NewGuid():N}.json");
        try
        {
            var store = new FileCheckpointStore(path);
            await store.SaveAsync(Checkpoint(), CancellationToken.None);

            var later = Checkpoint();
            later.Attempt = 3;
            later.Outputs = new Dictionary<string, object?> { ["n1"] = "later" };
            await store.SaveAsync(later, CancellationToken.None);

            var loaded = await store.LoadAsync(CancellationToken.None);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(3, loaded.Attempt);
            Assert.HasCount(1, loaded.Outputs);
            Assert.AreEqual("later", loaded.Outputs["n1"]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [TestMethod]
    public async Task FileCheckpointStore_WithNoFileYet_LoadsNothing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"veloxdev-checkpoint-{Guid.NewGuid():N}.json");

        var loaded = await new FileCheckpointStore(path).LoadAsync(CancellationToken.None);

        Assert.IsNull(loaded, "nothing has been saved yet");
        Assert.IsFalse(File.Exists(path), "and reading must not create the file");
    }
}
