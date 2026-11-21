using System;
using System.Collections.Generic;
using System.Linq;
using VeloxDev.TransitionSystem;

namespace VeloxDev.WinForms.Xaml;

/// <summary>One value at a normalized point in a <see cref="KeyFrameAnimation"/>.</summary>
public sealed class KeyFrame
{
    /// <summary>The position in the animation, from 0 (start) to 1 (end).</summary>
    public double Offset { get; set; }

    /// <summary>The value the animated property reaches at <see cref="Offset"/>.</summary>
    public object? Value { get; set; }
}

/// <summary>
/// A trigger action that animates one dependency property through a sequence of keyframes.
/// </summary>
/// <remarks>
/// This is the same transition chain as <see cref="TransitionAction"/>, split into segments: each adjacent pair of
/// keyframes becomes one segment whose duration is proportional to its slice of <see cref="Duration"/>. The chain
/// therefore behaves like one mutual animation — a later call on the same control supersedes all of it, and
/// <see cref="XamlTransition.Stop"/> ends it where it stands.
/// </remarks>
public sealed class KeyFrameAnimation : TriggerAction
{
    /// <summary>The dependency property to animate.</summary>
    public DependencyProperty? Property { get; set; }

    /// <summary>The total duration of all segments; 240 milliseconds by default.</summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(240);

    /// <summary>The easing applied to every segment; <c>Cubic.Out</c> by default.</summary>
    public IEaseCalculator Ease { get; set; } = Eases.Cubic.Out;

    /// <summary>Additional full passes of each segment; zero runs once and <see cref="int.MaxValue"/> repeats forever.</summary>
    public int LoopTime { get; set; }

    /// <summary>Whether each segment also runs backwards.</summary>
    public bool IsAutoReverse { get; set; }

    /// <summary>The keyframes, in any order; they are sorted by <see cref="KeyFrame.Offset"/> when invoked.</summary>
    public IList<KeyFrame> KeyFrames { get; } = new List<KeyFrame>();

    /// <inheritdoc />
    public override void Invoke(VeloxDev.WinForms.Xaml.Controls.Control control)
    {
        if (Property is null)
        {
            throw new InvalidOperationException("A KeyFrameAnimation needs a Property.");
        }

        // 集合是公开可写的；null 元素排到 sort 比较器里会先炸成 NRE，而它本来就没有可用的 Offset。
        var frames = KeyFrames
            .Where(static frame => frame is not null)
            .OrderBy(static frame => frame.Offset)
            .ToArray();
        if (frames.Length == 0)
        {
            return;
        }

        XamlTransition.AnimateKeyFrames(control, Property, frames, Duration, Ease, LoopTime, IsAutoReverse);
    }
}
