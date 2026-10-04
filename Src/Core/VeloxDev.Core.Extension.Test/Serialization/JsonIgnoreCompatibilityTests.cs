using System.Text.Json.Serialization;
using VeloxDev.MVVM;
using VeloxDev.Serialization;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// <c>System.Text.Json</c>'s <c>[JsonIgnore]</c>, honoured as it stands so a type written for another serializer
/// does not have to be rewritten to take part in this one.
/// </summary>
/// <remarks>
/// Its <c>Condition</c> keeps its meaning: <c>Always</c> drops the member, <c>Never</c> is an explicit opt-in,
/// and the two conditional values guard the write rather than dropping anything.
/// </remarks>
[TestClass]
public partial class JsonIgnoreCompatibilityTests
{
    /// <summary>Every member here arrives or not by a different reading of the same standard attribute.</summary>
    internal sealed partial class IgnoreModel
    {
        [VeloxProperty] private int kept;

        [VeloxProperty]
        [JsonIgnore]
        private int dropped;

        [JsonIgnore]
        public string? Hidden { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Declared { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Optional { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int OptionalCount { get; set; }
    }

    private static string Write(string? optional, int optionalCount)
        => VeloxJsonSerializer.Serialize(new IgnoreModel
        {
            Kept = 1,
            Dropped = 99,
            Hidden = "hidden",
            Optional = optional,
            OptionalCount = optionalCount,
        });

    [TestMethod]
    public void Always_LeavesTheMemberOut_WhicheverMemberItIs()
    {
        var json = Write(optional: "v", optionalCount: 5);

        StringAssert.Contains(json, "\"Kept\"");
        Assert.IsFalse(json.Contains("\"Hidden\""), "a property marked [JsonIgnore] is not written");
        Assert.IsFalse(json.Contains("\"Dropped\""), "nor is the member a marked field would have produced");
    }

    [TestMethod]
    public void Never_IsAnExplicitOptIn()
    {
        StringAssert.Contains(Write(optional: null, optionalCount: 0), "\"Declared\"",
            "a null the author explicitly asked to keep is written");
    }

    [TestMethod]
    public void WhenWritingNull_WritesOnlyWhenThereIsSomething()
    {
        Assert.IsFalse(Write(optional: null, optionalCount: 0).Contains("\"Optional\""));
        StringAssert.Contains(Write(optional: "v", optionalCount: 0), "\"Optional\"");
    }

    [TestMethod]
    public void WhenWritingDefault_WritesOnlyWhenItDiffersFromDefault()
    {
        Assert.IsFalse(Write(optional: null, optionalCount: 0).Contains("\"OptionalCount\""));
        StringAssert.Contains(Write(optional: null, optionalCount: 5), "\"OptionalCount\"");
    }

    [TestMethod]
    public void AConditionallyWrittenMember_StillReadsBack()
    {
        var restored = VeloxJsonSerializer.Deserialize<IgnoreModel>(Write(optional: "v", optionalCount: 5))!;

        Assert.AreEqual(1, restored.Kept);
        Assert.AreEqual("v", restored.Optional);
        Assert.AreEqual(5, restored.OptionalCount);
        Assert.IsNull(restored.Hidden, "nothing wrote it, so nothing restored it");
        Assert.AreEqual(0, restored.Dropped);
    }
}
