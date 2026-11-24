namespace VeloxDev.WorkflowSystem;

/// <summary>Which parts of a node view the pipeline measures, and the coordinate host those measurements use.</summary>
/// <remarks>
/// The attached properties stay strings so a template author can write
/// <c>PART_InputSlot,PART_OutputSlot</c>; this type parses and de-duplicates them once, preserving declared order,
/// so the measurement and write-back order is deterministic.
/// </remarks>
public sealed class SlotLayoutConfiguration
{
    /// <summary>A configuration with no parts to measure.</summary>
    public static SlotLayoutConfiguration Empty { get; } = new(null, null, null, null);

    /// <summary>Creates a configuration from the template's declared names.</summary>
    /// <param name="slotNames">Comma-separated names of elements that each render one slot.</param>
    /// <param name="enumeratorNames">Comma-separated names of controllers whose realized items render slots.</param>
    /// <param name="coordinateHostName">The name of the element that supplies the coordinate space; optional.</param>
    /// <param name="coordinateHostType">The element type used when the name does not resolve; optional.</param>
    public SlotLayoutConfiguration(
        string? slotNames,
        string? enumeratorNames,
        string? coordinateHostName,
        Type? coordinateHostType)
    {
        SlotNames = ParseNames(slotNames);
        EnumeratorNames = ParseNames(enumeratorNames);
        CoordinateHostName = string.IsNullOrWhiteSpace(coordinateHostName) ? null : coordinateHostName;
        CoordinateHostType = coordinateHostType;
    }

    /// <summary>The single-slot part names, in declared order.</summary>
    public IReadOnlyList<string> SlotNames { get; }

    /// <summary>The enumerator part names, in declared order.</summary>
    public IReadOnlyList<string> EnumeratorNames { get; }

    /// <summary>The named coordinate host, or <see langword="null"/> when the template does not name one.</summary>
    public string? CoordinateHostName { get; }

    /// <summary>The coordinate-host element type, or <see langword="null"/> for the platform default.</summary>
    public Type? CoordinateHostType { get; }

    private static IReadOnlyList<string> ParseNames(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in text!.Split(','))
        {
            var name = raw.Trim();
            if (name.Length > 0 && seen.Add(name))
            {
                names.Add(name);
            }
        }

        return names;
    }
}
