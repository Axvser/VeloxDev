namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>
/// The scrolling contract a <see cref="ScrollViewer"/> can delegate to, in the units the implementer chooses.
/// </summary>
/// <remarks>
/// A pixel-scrolling panel reports pixels; an item panel may report items and implement its own line/page steps.
/// The viewer only moves the bars and routes wheel/key gestures through this interface, so the content owns what a
/// line or a page means.
/// </remarks>
public interface IScrollInfo
{
    /// <summary>Whether horizontal offset changes are honoured.</summary>
    bool CanHorizontallyScroll { get; set; }

    /// <summary>Whether vertical offset changes are honoured.</summary>
    bool CanVerticallyScroll { get; set; }

    /// <summary>The full scrollable width, in this implementer's units.</summary>
    double ExtentWidth { get; }

    /// <summary>The full scrollable height, in this implementer's units.</summary>
    double ExtentHeight { get; }

    /// <summary>The visible width, in this implementer's units.</summary>
    double ViewportWidth { get; }

    /// <summary>The visible height, in this implementer's units.</summary>
    double ViewportHeight { get; }

    /// <summary>The current horizontal offset, in this implementer's units.</summary>
    double HorizontalOffset { get; }

    /// <summary>The current vertical offset, in this implementer's units.</summary>
    double VerticalOffset { get; }

    /// <summary>The viewer that owns this scroll info, or <see langword="null"/>.</summary>
    ScrollViewer? ScrollOwner { get; set; }

    /// <summary>Moves one line up.</summary>
    void LineUp();

    /// <summary>Moves one line down.</summary>
    void LineDown();

    /// <summary>Moves one line left.</summary>
    void LineLeft();

    /// <summary>Moves one line right.</summary>
    void LineRight();

    /// <summary>Moves one page up.</summary>
    void PageUp();

    /// <summary>Moves one page down.</summary>
    void PageDown();

    /// <summary>Moves one page left.</summary>
    void PageLeft();

    /// <summary>Moves one page right.</summary>
    void PageRight();

    /// <summary>Moves the amount one wheel notch asks for, upward.</summary>
    void MouseWheelUp();

    /// <summary>Moves the amount one wheel notch asks for, downward.</summary>
    void MouseWheelDown();

    /// <summary>Moves the amount one wheel notch asks for, leftward.</summary>
    void MouseWheelLeft();

    /// <summary>Moves the amount one wheel notch asks for, rightward.</summary>
    void MouseWheelRight();

    /// <summary>Sets the horizontal offset, clamped by the implementer.</summary>
    void SetHorizontalOffset(double offset);

    /// <summary>Sets the vertical offset, clamped by the implementer.</summary>
    void SetVerticalOffset(double offset);
}
