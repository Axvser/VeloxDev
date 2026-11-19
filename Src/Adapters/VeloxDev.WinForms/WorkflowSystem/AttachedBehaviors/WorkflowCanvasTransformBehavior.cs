using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// WinForms analogue of the canvas render-transform behavior used by the XAML
/// adapters. Because WinForms has no attached-property system, the transform is
/// stored as a translate <see cref="Offset"/> per control in a
/// <see cref="ConditionalWeakTable{TKey, TValue}"/>.
/// </summary>
/// <remarks>
/// <see cref="WorkflowSurfaceBehavior"/> writes this value during its refresh
/// cycle. A self-drawn host canvas can read <see cref="GetTransform"/> in its
/// <c>OnPaint</c> to translate the drawing origin, mirroring how WPF node/link
/// views bind their <c>RenderTransform</c> to the attached <c>Transform</c>.
/// The value is a notification carrier only — the host control decides whether
/// to honor it.
/// </remarks>
public static class WorkflowCanvasTransformBehavior
{
    private sealed class TransformBox
    {
        public Offset Value = new();
    }

    private static readonly ConditionalWeakTable<Control, TransformBox> Transforms = new();

    /// <summary>
    /// Gets the current translate transform for the specified control, or <see langword="null"/> when none is set.
    /// </summary>
    public static Offset? GetTransform(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return Transforms.TryGetValue(element, out var box) ? box.Value : null;
    }

    /// <summary>
    /// Sets the translate transform for the specified control. Pass <see langword="null"/> to clear it.
    /// </summary>
    public static void SetTransform(Control element, Offset? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        if (value is { } offset)
        {
            Transforms.Remove(element);
            Transforms.Add(element, new TransformBox { Value = offset });
        }
        else
        {
            Transforms.Remove(element);
        }
    }

    internal static void Apply(Control element, Offset offset)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        Transforms.Remove(element);
        Transforms.Add(element, new TransformBox { Value = offset });
    }
}
