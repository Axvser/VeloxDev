using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The dependency-property core: defaults, callbacks, coercion, local-value lifetime and attached properties on both
/// native controls and visual elements.
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

    /// <summary>A plain native control used to exercise the dependency-property store; it is not a XAML control.</summary>
    private sealed class ProbeControl : Control
    {
        public static readonly DependencyProperty NumberProperty = DependencyProperty.Register(
            nameof(Number),
            typeof(int),
            typeof(ProbeControl),
            new PropertyMetadata(7, OnNumberChanged, CoerceNumber));

        public static readonly DependencyProperty ConfigurationProperty = DependencyProperty.Register(
            nameof(Configuration),
            typeof(int),
            typeof(ProbeControl),
            new PropertyMetadata(0) { AffectsRender = false });

        public static List<(DependencyProperty Property, object? OldValue, object? NewValue)> Changes { get; } = [];

        public int Number
        {
            get => (int)NumberProperty.GetValue(this)!;
            set => NumberProperty.SetValue(this, value);
        }

        public int Configuration
        {
            get => (int)ConfigurationProperty.GetValue(this)!;
            set => ConfigurationProperty.SetValue(this, value);
        }

        private static object? CoerceNumber(object target, object? baseValue)
            => Math.Clamp((int)baseValue!, 0, 100);

        private static void OnNumberChanged(Control target, DependencyPropertyChangedEventArgs e)
            => Changes.Add((e.Property, e.OldValue, e.NewValue));
    }

    /// <summary>Visual-target properties: one that has no conversion and one that coerces.</summary>
    private static class VisualProbe
    {
        public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
            "Color",
            typeof(System.Drawing.Color),
            typeof(VisualProbe));

        public static readonly DependencyProperty RatioProperty = DependencyProperty.Register(
            "Ratio",
            typeof(double),
            typeof(VisualProbe),
            new PropertyMetadata(0.5d, null, CoerceRatio));

        private static object? CoerceRatio(object target, object? baseValue)
            => Math.Clamp((double)baseValue!, 0d, 1d);
    }

    /// <summary>An attached property whose accessors take a visual element, as the generator requires.</summary>
    private static class AttachedProbe
    {
        public static readonly DependencyProperty NoteProperty = DependencyProperty.RegisterAttached(
            "Note",
            typeof(string),
            typeof(AttachedProbe),
            new PropertyMetadata("(none)", OnNoteChanged));

        public static List<(object Target, object? OldValue, object? NewValue)> Changes { get; } = [];

        public static string? GetNote(VeloxDev.WinForms.Xaml.Visuals.VisualElement target)
            => (string?)NoteProperty.GetValue(target);

        public static void SetNote(VeloxDev.WinForms.Xaml.Visuals.VisualElement target, string? value)
            => NoteProperty.SetValue(target, value);

        public static void ClearNote(VeloxDev.WinForms.Xaml.Visuals.VisualElement target)
            => NoteProperty.ClearValue(target);

        public static void Reset() => Changes.Clear();

        private static void OnNoteChanged(Control target, DependencyPropertyChangedEventArgs e)
            => Changes.Add((target, e.OldValue, e.NewValue));
    }

    [TestMethod]
    public void ClrWrapper_ReadsTheDefaultUntilALocalValueExists()
    {
        using var control = new ProbeControl();

        Assert.AreEqual(7, control.Number);
        Assert.AreEqual(7, ProbeControl.NumberProperty.DefaultValue);
        Assert.AreEqual(7, ProbeControl.NumberProperty.GetValue(control));
    }

    [TestMethod]
    public void SetValue_StoresTheValueAndRaisesTheChangeCallback()
    {
        ProbeControl.Changes.Clear();
        using var control = new ProbeControl();

        control.Number = 42;

        Assert.AreEqual(42, control.Number);
        Assert.HasCount(1, ProbeControl.Changes);
        Assert.AreEqual(7, ProbeControl.Changes[0].OldValue);
        Assert.AreEqual(42, ProbeControl.Changes[0].NewValue);
        Assert.AreSame(ProbeControl.NumberProperty, ProbeControl.Changes[0].Property);
    }

    [TestMethod]
    public void ClearValue_RestoresTheDefaultAndRaisesTheCallback()
    {
        ProbeControl.Changes.Clear();
        using var control = new ProbeControl();
        control.Number = 42;

        ProbeControl.NumberProperty.ClearValue(control);

        Assert.AreEqual(7, control.Number);
        Assert.HasCount(2, ProbeControl.Changes);
        Assert.AreEqual(7, ProbeControl.Changes[1].NewValue);
    }

    [TestMethod]
    public void Coerce_RunsBeforeTheValueIsStoredAndReported()
    {
        ProbeControl.Changes.Clear();
        using var control = new ProbeControl();

        control.Number = 250;
        Assert.AreEqual(100, control.Number);

        control.Number = -5;
        Assert.AreEqual(0, control.Number);

        Assert.HasCount(2, ProbeControl.Changes);
        Assert.AreEqual(100, ProbeControl.Changes[0].NewValue);
        Assert.AreEqual(0, ProbeControl.Changes[1].NewValue);
        Assert.AreEqual(100, ProbeControl.Changes[1].OldValue);
    }

    [TestMethod]
    public void SettingTheSameValueTwice_RaisesTheCallbackOnce()
    {
        ProbeControl.Changes.Clear();
        using var control = new ProbeControl();

        control.Number = 7;
        control.Number = 7;

        // 第一次写入虽然等于默认值，但它是新的局部值；第二次才是真正的无变化。
        Assert.HasCount(1, ProbeControl.Changes);
    }

    [TestMethod]
    public void NullForANonNullableValueType_IsRejected()
    {
        using var control = new ProbeControl();

        Assert.ThrowsExactly<ArgumentException>(() => ProbeControl.NumberProperty.SetValue(control, null));
        Assert.AreEqual(7, control.Number);
    }

    [TestMethod]
    public void AConvertibleValue_IsConvertedToThePropertyType()
    {
        using var control = new ProbeControl();

        ProbeControl.NumberProperty.SetValue(control, "42");

        Assert.AreEqual(42, control.Number);
    }

    [TestMethod]
    public void VisualTarget_RejectsAValueThatCannotBeConverted()
    {
        using var border = new VC.Border();

        // Color 没有从 int 出发的转换，转换器会原样返回；写入端必须在拆箱处报错之前拦住它。
        Assert.ThrowsExactly<ArgumentException>(() => border.SetValue(VisualProbe.ColorProperty, 42));
        Assert.AreEqual(System.Drawing.Color.Empty, border.GetValue(VisualProbe.ColorProperty));
    }

    [TestMethod]
    public void VisualTarget_InvokesTheCoerceCallback()
    {
        using var border = new VC.Border();

        border.SetValue(VisualProbe.RatioProperty, 5d);
        Assert.AreEqual(1d, (double)border.GetValue(VisualProbe.RatioProperty)!);

        border.SetValue(VisualProbe.RatioProperty, -3d);
        Assert.AreEqual(0d, (double)border.GetValue(VisualProbe.RatioProperty)!);
    }

    [TestMethod]
    public void Lookup_FindsThePropertyByOwnerAndName()
    {
        Assert.AreSame(ProbeControl.NumberProperty, DependencyProperty.Lookup(typeof(ProbeControl), nameof(ProbeControl.Number)));
        Assert.IsNull(DependencyProperty.Lookup(typeof(ProbeControl), "Missing"));
    }

    [TestMethod]
    public void RegisteringTheSameNameTwice_IsRejected()
    {
        _ = RegistrationOwner.Register("Once");

        Assert.ThrowsExactly<InvalidOperationException>(() => RegistrationOwner.Register("Once"));
    }

    [TestMethod]
    public void AttachedProperty_RoundTripsOnVisualElements()
    {
        AttachedProbe.Reset();
        using var first = new VC.Border();
        using var second = new VC.Border();

        Assert.AreEqual("(none)", AttachedProbe.GetNote(first));

        AttachedProbe.SetNote(first, "first");
        AttachedProbe.SetNote(second, "second");

        Assert.AreEqual("first", AttachedProbe.GetNote(first));
        Assert.AreEqual("second", AttachedProbe.GetNote(second));
        Assert.IsTrue(first.IsSet(AttachedProbe.NoteProperty));
    }

    [TestMethod]
    public void AttachedProperty_ClearRestoresTheDefault()
    {
        using var border = new VC.Border();
        AttachedProbe.SetNote(border, "value");

        AttachedProbe.ClearNote(border);

        Assert.AreEqual("(none)", AttachedProbe.GetNote(border));
        Assert.IsFalse(border.IsSet(AttachedProbe.NoteProperty));
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
