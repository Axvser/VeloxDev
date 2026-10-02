using System.Reflection;
using Newtonsoft.Json.Linq;
using VeloxDev.MVVM;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// The order members land in the JSON — the one thing a source-generated serializer cannot derive from the
/// source, and therefore the thing that decides whether it can be byte-for-byte compatible.
/// </summary>
/// <remarks>
/// <para>
/// Newtonsoft writes members in the order its contract resolver reports them, which is the CLR metadata order.
/// Half of a ViewModel's writable members are <c>[VeloxProperty]</c> fields promoted to properties by the MVVM
/// generator — those live in a <b>different partial file</b>, so their position is decided by how the compiler
/// concatenates partials, not by the author's source.
/// </para>
/// <para>
/// These tests pin the rule that makes the order reproducible from symbols: <b>hand-written writable properties
/// first, in declaration order, then the promoted fields in field order</b>. Measured and confirmed against the
/// real serializer for all six representative shapes — including <see cref="SlotEnumerator{TSlot}"/>, which
/// mixes a hand-written writable property with promoted ones. A serializer generator that follows the rule
/// reproduces today's bytes; if a change breaks one of these, the message carries all three orders so the new
/// order can be read off it.
/// </para>
/// </remarks>
[TestClass]
public class SerializationOrderTests
{
    /// <summary>
    /// The promoted property name for a backing field — the rule <c>AIContextNaming.PromotedPropertyName</c>
    /// applies and the serializer generator must reproduce.
    /// </summary>
    private static string Promoted(string fieldName)
    {
        var start = fieldName.StartsWith("_") ? 1 : 0;
        return char.ToUpper(fieldName[start]) + fieldName.Substring(start + 1);
    }

    private static bool HasVeloxProperty(FieldInfo field)
        => field.GetCustomAttribute<VeloxPropertyAttribute>() is not null;

    /// <summary>
    /// The members Newtonsoft actually writes, in the order it writes them, with its metadata keys removed:
    /// <c>$id</c>/<c>$type</c> are written first by the serializer, not by the contract.
    /// </summary>
    private static string[] JsonOrder(System.ComponentModel.INotifyPropertyChanged instance)
        => [.. JObject.Parse(instance.Serialize()).Properties()
                .Select(static p => p.Name)
                .Where(static name => !name.StartsWith('$'))];

    /// <summary>
    /// What a serializer generator could predict from the source: hand-written writable properties first (they
    /// are declared in the author's file), then the promoted <c>[VeloxProperty]</c> fields (they come from the
    /// generated partial, which the compiler appends).
    /// </summary>
    private static string[] Predicted(Type type)
    {
        var fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
            .Where(HasVeloxProperty)
            .ToArray();

        var promoted = fields.Select(static f => Promoted(f.Name)).ToHashSet(StringComparer.Ordinal);

        var handWritten = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true } && !promoted.Contains(p.Name))
            .Select(static p => p.Name);

        return [.. handWritten, .. fields.Select(static f => Promoted(f.Name))];
    }

    private static void AssertPredictionHolds(Type type, System.ComponentModel.INotifyPropertyChanged instance)
    {
        var json = JsonOrder(instance);
        var predicted = Predicted(type);

        Assert.AreEqual(
            string.Join(", ", predicted),
            string.Join(", ", json),
            $"{type.Name}: the JSON order must be predictable from source symbols.\n" +
            $"  metadata order : {string.Join(", ", type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.SetMethod is { IsPublic: true }).Select(p => p.Name))}\n" +
            $"  predicted      : {string.Join(", ", predicted)}\n" +
            $"  json           : {string.Join(", ", json)}");
    }

    [TestMethod]
    public void AllGeneratedMembers_AreWrittenInFieldOrder()
    {
        AssertPredictionHolds(typeof(SlotDefaultViewModel), new SlotDefaultViewModel());
        AssertPredictionHolds(typeof(CanvasLayout), new CanvasLayout());
        AssertPredictionHolds(typeof(Scale), new Scale(2, 3));
        AssertPredictionHolds(typeof(Anchor), new Anchor(1, 2, 3));
    }

    [TestMethod]
    public void MixedHandWrittenAndGeneratedMembers_KeepThePredictedOrder()
    {
        AssertPredictionHolds(typeof(TreeDefaultViewModel), new TreeDefaultViewModel());
        AssertPredictionHolds(typeof(SlotEnumerator<SlotDefaultViewModel>), new SlotEnumerator<SlotDefaultViewModel>());
    }

    /// <summary>
    /// Prints the three hard bones the plan flagged: reference ids, the enumerator written as an object, and the
    /// NaN anchor. Diagnostically useful in its own right if the byte-for-byte claim ever regresses.
    /// </summary>
    [TestMethod]
    public void HardBones_DumpTheirShape()
    {
        var slot = new SlotDefaultViewModel();
        var json = slot.Serialize();

        Assert.Contains("$id", json, "reference preservation is on, so objects carry $id");
        Assert.Contains("\"NaN\"", json, "the default slot anchor is NaN, written as a string");
        Assert.IsFalse(
            json.TrimStart().StartsWith('['),
            "SlotEnumerator implements IEnumerable but is written as an object, not an array");
    }
}
