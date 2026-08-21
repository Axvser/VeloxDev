using System.Reflection;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.Core.Test.TransitionSystem;

[TestClass]
public class TransitionPropertyTests
{
    private sealed class TestTarget
    {
        public double Value { get; set; }
        public string? Name { get; set; }
        public TestNested? Nested { get; set; }
    }

    private sealed class TestNested
    {
        public int Inner { get; set; }
    }

    private class BaseShape
    {
        public int Id { get; set; }
    }

    private sealed class CircleShape : BaseShape
    {
        public double Radius { get; set; }
    }

    private sealed class ShapeContainer
    {
        public BaseShape? Shape { get; set; }
    }

    [TestMethod]
    public void FromProperty_CreatesValidProperty()
    {
        var propInfo = typeof(TestTarget).GetProperty(nameof(TestTarget.Value))!;
        var tp = TransitionProperty.FromProperty(propInfo);

        Assert.AreEqual("Value", tp.Path);
        Assert.AreEqual(typeof(double), tp.PropertyType);
        Assert.IsTrue(tp.CanRead);
        Assert.IsTrue(tp.CanWrite);
    }

    [TestMethod]
    public void FromProperty_NullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => TransitionProperty.FromProperty(null!));
    }

    [TestMethod]
    public void TryCreate_SimpleProperty_ReturnsTrue()
    {
        var ok = TransitionProperty.TryCreate(
            (System.Linq.Expressions.Expression<Func<TestTarget, double>>)(t => t.Value),
            out var property);

        Assert.IsTrue(ok);
        Assert.IsNotNull(property);
        Assert.AreEqual("Value", property!.Path);
    }

    [TestMethod]
    public void TryCreate_NullExpression_ReturnsFalse()
    {
        var ok = TransitionProperty.TryCreate(null!, out var property);
        Assert.IsFalse(ok);
        Assert.IsNull(property);
    }

    [TestMethod]
    public void GetValue_ReadsFromTarget()
    {
        var target = new TestTarget { Value = 42.5 };
        var propInfo = typeof(TestTarget).GetProperty(nameof(TestTarget.Value))!;
        var tp = TransitionProperty.FromProperty(propInfo);

        var result = tp.GetValue(target);
        Assert.AreEqual(42.5, result);
    }

    [TestMethod]
    public void SetValue_WritesToTarget()
    {
        var target = new TestTarget { Value = 0 };
        var propInfo = typeof(TestTarget).GetProperty(nameof(TestTarget.Value))!;
        var tp = TransitionProperty.FromProperty(propInfo);

        var success = tp.SetValue(target, 99.9);
        Assert.IsTrue(success);
        Assert.AreEqual(99.9, target.Value);
    }

    [TestMethod]
    public void GetValue_NullTarget_Throws()
    {
        var propInfo = typeof(TestTarget).GetProperty(nameof(TestTarget.Value))!;
        var tp = TransitionProperty.FromProperty(propInfo);
        Assert.Throws<ArgumentNullException>(() => tp.GetValue(null!));
    }

    [TestMethod]
    public void SetValue_NullTarget_Throws()
    {
        var propInfo = typeof(TestTarget).GetProperty(nameof(TestTarget.Value))!;
        var tp = TransitionProperty.FromProperty(propInfo);
        Assert.Throws<ArgumentNullException>(() => tp.SetValue(null!, 0));
    }

    [TestMethod]
    public void Equals_SameProperty_ReturnsTrue()
    {
        var propInfo = typeof(TestTarget).GetProperty(nameof(TestTarget.Value))!;
        var a = TransitionProperty.FromProperty(propInfo);
        var b = TransitionProperty.FromProperty(propInfo);
        Assert.IsTrue(a.Equals(b));
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    [TestMethod]
    public void Equals_DifferentProperties_ReturnsFalse()
    {
        var a = TransitionProperty.FromProperty(typeof(TestTarget).GetProperty(nameof(TestTarget.Value))!);
        var b = TransitionProperty.FromProperty(typeof(TestTarget).GetProperty(nameof(TestTarget.Name))!);
        Assert.IsFalse(a.Equals(b));
    }

    [TestMethod]
    public void Equals_Null_ReturnsFalse()
    {
        var a = TransitionProperty.FromProperty(typeof(TestTarget).GetProperty(nameof(TestTarget.Value))!);
        Assert.IsFalse(a.Equals((TransitionProperty?)null));
        Assert.IsFalse(a.Equals((object?)null));
    }

    [TestMethod]
    public void ToString_ReturnsPath()
    {
        var a = TransitionProperty.FromProperty(typeof(TestTarget).GetProperty(nameof(TestTarget.Value))!);
        Assert.AreEqual("Value", a.ToString());
    }

    [TestMethod]
    public void Constructor_EmptySegments_Throws()
    {
        Assert.Throws<ArgumentException>(() => new TransitionProperty(Array.Empty<PropertyInfo>()));
    }

    [TestMethod]
    public void GetValue_IntermediateTypeMismatch_ReturnsUnreadablePath_NotTargetException()
    {
        // Path [ShapeContainer.Shape, CircleShape.Radius], but Shape's runtime type is BaseShape,
        // not CircleShape — the old implementation threw System.Reflection.TargetException; it must
        // now return UnreadablePath so the caller skips the property instead of interpolating it as a null value and distorting the result.
        var property = new TransitionProperty(new[]
        {
            typeof(ShapeContainer).GetProperty(nameof(ShapeContainer.Shape))!,
            typeof(CircleShape).GetProperty(nameof(CircleShape.Radius))!,
        });

        var target = new ShapeContainer { Shape = new BaseShape() };
        var result = property.GetValue(target);
        Assert.AreSame(TransitionProperty.UnreadablePath, result);
    }

    [TestMethod]
    public void GetValue_NullIntermediate_ReturnsNull_NotUnreadable()
    {
        // A null intermediate object → the value is genuinely null → return null (unlike the sentinel).
        // This lets Interpolate hand it to the interpolator to start from identity/default (rather than skipping),
        // so animations like 3D that start from null work correctly.
        var property = new TransitionProperty(new[]
        {
            typeof(ShapeContainer).GetProperty(nameof(ShapeContainer.Shape))!,
            typeof(CircleShape).GetProperty(nameof(CircleShape.Radius))!,
        });

        var target = new ShapeContainer { Shape = null };
        var result = property.GetValue(target);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void UnreadablePath_IsNotNull()
    {
        Assert.IsNotNull(TransitionProperty.UnreadablePath);
    }

    [TestMethod]
    public void SetValue_WriteReferenceProperty_Works()
    {
        // Simulates reference properties like RenderTransform: writing a concrete value over null
        var container = new ShapeContainer { Shape = null };
        var prop = TransitionProperty.FromProperty(typeof(ShapeContainer).GetProperty(nameof(ShapeContainer.Shape))!);

        var ok = prop.SetValue(container, new CircleShape { Radius = 3 });
        Assert.IsTrue(ok);
        Assert.IsInstanceOfType(container.Shape, typeof(CircleShape));
        Assert.AreEqual(3d, ((CircleShape)container.Shape!).Radius);
    }

    [TestMethod]
    public void SetValue_ClearReferenceProperty_ToNull_Works()
    {
        // Simulates a reset: clearing the reference property back to null (the initial state)
        var container = new ShapeContainer { Shape = new CircleShape { Radius = 3 } };
        var prop = TransitionProperty.FromProperty(typeof(ShapeContainer).GetProperty(nameof(ShapeContainer.Shape))!);

        var ok = prop.SetValue(container, null);
        Assert.IsTrue(ok);
        Assert.IsNull(container.Shape);
    }

    [TestMethod]
    public void SetValue_IntermediateTypeMismatch_ReturnsFalse_NotTargetException()
    {
        var property = new TransitionProperty(new[]
        {
            typeof(ShapeContainer).GetProperty(nameof(ShapeContainer.Shape))!,
            typeof(CircleShape).GetProperty(nameof(CircleShape.Radius))!,
        });

        var target = new ShapeContainer { Shape = new BaseShape() };
        var success = property.SetValue(target, 5.0);
        Assert.IsFalse(success);
    }

    [TestMethod]
    public void GetValue_IntermediateMatches_ReadsNestedValue()
    {
        var property = new TransitionProperty(new[]
        {
            typeof(ShapeContainer).GetProperty(nameof(ShapeContainer.Shape))!,
            typeof(CircleShape).GetProperty(nameof(CircleShape.Radius))!,
        });

        var target = new ShapeContainer { Shape = new CircleShape { Radius = 3.5 } };
        var result = property.GetValue(target);
        Assert.AreEqual(3.5, result);
    }
}
