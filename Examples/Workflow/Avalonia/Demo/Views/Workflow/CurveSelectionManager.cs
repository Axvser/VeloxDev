using System;

namespace Demo;

/// <summary>
/// Coordinates exclusive selection across all curve views (Bezier + Polyline).
/// </summary>
internal static class CurveSelectionManager
{
    public static event Action<object?>? SelectionChanged;

    private static object? _current;

    public static void Select(object? owner)
    {
        if (_current == owner) return;
        _current = owner;
        SelectionChanged?.Invoke(owner);
    }

    public static void Deselect(object? owner)
    {
        if (_current == owner)
        {
            _current = null;
            SelectionChanged?.Invoke(null);
        }
    }
}
