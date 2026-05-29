namespace Demo.ViewModels;

/// <summary>A single route entry inside <see cref="CustomRouteSelector"/>.</summary>
public class RouteEntry
{
    /// <summary>Routing key used by the node to look up the target slot.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Human-readable label shown on the slot.</summary>
    public string Label { get; set; } = string.Empty;
}
