namespace VeloxDev.TransitionSystem
{
    /// <summary>
    /// Specifies the direction strategy for rotation interpolation. Values can be combined bitwise to control per-axis behavior.
    /// <para>Pass as the <c>interpolationOptions</c> argument of <c>Property(..., interpolationOptions)</c>.</para>
    /// </summary>
    [Flags]
    public enum RotationDirection
    {
        /// <summary>
        /// Automatically selects the shortest-path interpolation without forcing a direction (default behavior).
        /// </summary>
        Auto = 0,

        /// <summary>
        /// Forces clockwise interpolation (applies to 2-D rotation or axis-agnostic 3-D rotation).
        /// </summary>
        ClockWise = 1 << 0,

        /// <summary>
        /// Forces counter-clockwise interpolation (applies to 2-D rotation or axis-agnostic 3-D rotation).
        /// </summary>
        CounterClockWise = 1 << 1,

        /// <summary>
        /// Forces clockwise interpolation around the X axis.
        /// </summary>
        ClockWiseX = 1 << 2,

        /// <summary>
        /// Forces counter-clockwise interpolation around the X axis.
        /// </summary>
        CounterClockWiseX = 1 << 3,

        /// <summary>
        /// Forces clockwise interpolation around the Y axis.
        /// </summary>
        ClockWiseY = 1 << 4,

        /// <summary>
        /// Forces counter-clockwise interpolation around the Y axis.
        /// </summary>
        CounterClockWiseY = 1 << 5,

        /// <summary>
        /// Forces clockwise interpolation around the Z axis.
        /// </summary>
        ClockWiseZ = 1 << 6,

        /// <summary>
        /// Forces counter-clockwise interpolation around the Z axis.
        /// </summary>
        CounterClockWiseZ = 1 << 7,
    }
}
