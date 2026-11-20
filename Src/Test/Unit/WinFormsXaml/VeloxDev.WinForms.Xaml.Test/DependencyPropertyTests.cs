using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Test.Fixtures;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The dependency-property core: defaults, callbacks, coercion, local-value lifetime and attached properties on
/// controls that do not derive from <see cref="Control"/>.
/// </summary>
[STATestClass]
public sealed class DependencyPropertyTests
{
    private sealed class RegistrationOwner
    {
        public static DependencyProperty Register(string name) => DependencyProperty.Register(
            name,
            typeof(int),
            typeof(RegistrationOwner));
    }

    [TestMethod]
    public void DefaultValue_IsReadUntilALocalValueExists()
    {
        using var control = new ProbeControl();

        Assert.AreEqual(7, control.Number);
        Assert.AreEqual("(none)", control.Name);
        Assert.IsFalse(control.HasLocalValue(ProbeControl.NumberProperty));
        Assert.AreEqual(ProbeControl.NumberProperty.DefaultValue, control.GetValue(ProbeControl.NumberProperty));
    }

    [TestMethod]
    public void SetValue_StoresTheValueAndRaisesTheChangeCallback()
    {
        using var control = new ProbeControl();

        control.Number = 42;

        Assert.AreEqual(42, control.Number);
        Assert.IsTrue(control.HasLocalValue(ProbeControl.NumberProperty));
        Assert.HasCount(1, control.Changes);
        Assert.AreEqual(7, control.Changes[0].OldValue);
        Assert.AreEqual(42, control.Changes[0].NewValue);
        Assert.AreSame(ProbeControl.NumberProperty, control.Changes[0].Property);
    }

    [TestMethod]
    public void AVisualDependencyPropertyChange_InvalidatesTheXamlControl()
    {
        using var control = new ProbeControl();
        XamlTestSurface.EnsureHandle(control);
        var invalidations = 0;
        control.Invalidated += (_, _) => invalidations++;

        control.Number = 42;

        Assert.AreEqual(1, invalidations);
    }

    [TestMethod]
    public void ANonVisualDependencyPropertyChange_DoesNotInvalidateTheControl()
    {
        using var control = new ProbeControl();
        XamlTestSurface.EnsureHandle(control);
        var invalidations = 0;
        control.Invalidated += (_, _) => invalidations++;

        control.Configuration = 3;

        Assert.AreEqual(0, invalidations);
    }

    [TestMethod]
    public void ClearValue_RestoresTheDefaultAndRemovesTheLocalValue()
    {
        using var control = new ProbeControl();
        control.Number = 42;

        control.ClearValue(ProbeControl.NumberProperty);

        Assert.AreEqual(7, control.Number);
        Assert.IsFalse(control.HasLocalValue(ProbeControl.NumberProperty));
        Assert.HasCount(2, control.Changes);
        Assert.AreEqual(7, control.Changes[1].NewValue);
    }

    [TestMethod]
    public void Coerce_RunsBeforeTheValueIsStoredAndReported()
    {
        using var control = new ProbeControl();

        control.Number = 250;
        Assert.AreEqual(100, control.Number);

        control.Number = -5;
        Assert.AreEqual(0, control.Number);

        Assert.HasCount(2, control.Changes);
        Assert.AreEqual(100, control.Changes[0].NewValue);
        Assert.AreEqual(0, control.Changes[1].NewValue);
        Assert.AreEqual(100, control.Changes[1].OldValue);
    }

    [TestMethod]
    public void SettingTheSameValueTwice_RaisesTheCallbackOnce()
    {
        using var control = new ProbeControl();

        control.Number = 7;
        control.Number = 7;

        // 第一次写入虽然等于默认值，但它是新的局部值；第二次才是真正的无变化。
        Assert.HasCount(1, control.Changes);
        Assert.IsTrue(control.HasLocalValue(ProbeControl.NumberProperty));
    }

    [TestMethod]
    public void NullForANonNullableValueType_IsRejected()
    {
        using var control = new ProbeControl();

        Assert.ThrowsExactly<ArgumentException>(() => control.SetValue(ProbeControl.NumberProperty, null));
        Assert.AreEqual(7, control.Number);
    }

    [TestMethod]
    public void AConvertibleValue_IsConvertedToThePropertyType()
    {
        using var control = new ProbeControl();

        control.SetValue(ProbeControl.NumberProperty, "42");

        Assert.AreEqual(42, control.Number);
    }

    [TestMethod]
    public void Lookup_FindsThePropertyByOwnerAndName()
    {
        Assert.AreSame(ProbeControl.NumberProperty, DependencyProperty.Lookup(typeof(ProbeControl), "Number"));
        Assert.IsNull(DependencyProperty.Lookup(typeof(ProbeControl), "Missing"));
    }

    [TestMethod]
    public void RegisteringTheSameNameTwice_IsRejected()
    {
        _ = RegistrationOwner.Register("Once");

        Assert.ThrowsExactly<InvalidOperationException>(() => RegistrationOwner.Register("Once"));
    }

    [TestMethod]
    public void AttachedProperty_RoundTripsOnANativeButtonAndPanel()
    {
        AttachedProbe.Reset();
        using var button = new Button();
        using var panel = new VeloxDev.WinForms.Xaml.Controls.Panel();

        AttachedProbe.SetNote(button, "on a button");
        AttachedProbe.SetNote(panel, "on a panel");

        Assert.AreEqual("on a button", AttachedProbe.GetNote(button));
        Assert.AreEqual("on a panel", AttachedProbe.GetNote(panel));
        Assert.HasCount(2, AttachedProbe.Changes);
        Assert.AreSame(button, AttachedProbe.Changes[0].Target);
        Assert.AreEqual("on a button", AttachedProbe.Changes[0].NewValue);
    }

    [TestMethod]
    public void AttachedProperty_FirstSetReportsTheDefaultAsTheOldValue()
    {
        // 和 Control 的路径一致：写入前 GetValue 就是默认值，回调的 OldValue 必须是那个值。
        AttachedProbe.Reset();
        using var button = new Button();

        Assert.AreEqual("(none)", AttachedProbe.GetNote(button));

        AttachedProbe.SetNote(button, "value");

        Assert.AreEqual("(none)", AttachedProbe.Changes[0].OldValue);
        Assert.AreEqual("value", AttachedProbe.Changes[0].NewValue);
    }

    [TestMethod]
    public void AttachedProperty_IsStoredPerControl()
    {
        AttachedProbe.Reset();
        using var first = new Button();
        using var second = new Button();

        AttachedProbe.SetNote(first, "first");
        AttachedProbe.SetNote(second, "second");

        Assert.AreEqual("first", AttachedProbe.GetNote(first));
        Assert.AreEqual("second", AttachedProbe.GetNote(second));

        AttachedProbe.ClearNote(first);

        Assert.AreEqual("(none)", AttachedProbe.GetNote(first));
        Assert.AreEqual("second", AttachedProbe.GetNote(second));
    }

    [TestMethod]
    public void AttachedProperty_ClearRestoresTheDefaultAndRaisesTheCallback()
    {
        AttachedProbe.Reset();
        using var button = new Button();
        AttachedProbe.SetNote(button, "value");

        AttachedProbe.ClearNote(button);

        Assert.AreEqual("(none)", AttachedProbe.GetNote(button));
        Assert.HasCount(2, AttachedProbe.Changes);
        Assert.AreEqual("value", AttachedProbe.Changes[1].OldValue);
        Assert.AreEqual("(none)", AttachedProbe.Changes[1].NewValue);
    }

    [TestMethod]
    public void AttachedProperty_AlsoWorksOnAControl()
    {
        AttachedProbe.Reset();
        using var control = new ProbeControl();

        AttachedProbe.SetNote(control, "xaml host");

        Assert.AreEqual("xaml host", AttachedProbe.GetNote(control));
        Assert.IsTrue(control.HasLocalValue(AttachedProbe.NoteProperty));
    }

    [TestMethod]
    public void Register_RejectsANullDefaultForANonNullableValueType()
    {
        Assert.ThrowsExactly<ArgumentException>(() => DependencyProperty.Register(
            $"NullDefault_{Guid.NewGuid():N}",
            typeof(int),
            typeof(RegistrationOwner),
            new PropertyMetadata(null)));
    }

    [TestMethod]
    public void Register_AcceptsABoxedDefaultForANullableValueType()
    {
        var property = DependencyProperty.Register(
            $"NullableDefault_{Guid.NewGuid():N}",
            typeof(int?),
            typeof(RegistrationOwner),
            new PropertyMetadata(5));

        using var control = new ProbeControl();

        Assert.AreEqual(5, property.GetValue(control));
    }
}
