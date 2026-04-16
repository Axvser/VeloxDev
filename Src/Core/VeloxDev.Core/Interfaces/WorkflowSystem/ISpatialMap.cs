namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Defines methods for managing and querying spatial elements within a spatial index.
/// </summary>
/// <typeparam name="T">The type of elements that can be spatially indexed.</typeparam>
/// <remarks>
/// Implementations of this interface enable efficient insertion, removal, and spatial querying of
/// elements. It is intended for scenarios where rapid access to elements based on their spatial location is
/// required, such as in graphical workflow editors or visualization tools.
/// </remarks>
public interface ISpatialMap<T> where T : class, ISpatialBoundsProvider
{
    /// <summary>
    /// Inserts an element into the spatial index.
    /// </summary>
    /// <param name="item">The element to insert.</param>
    void Insert(T item);

    /// <summary>
    /// Removes an element from the spatial index.
    /// </summary>
    /// <param name="item">The element to remove.</param>
    void Remove(T item);

    /// <summary>
    /// Queries the elements within the specified viewport.
    /// </summary>
    /// <param name="viewport">The viewport to query.</param>
    /// <returns>An enumerable of elements that intersect with the viewport.</returns>
    IEnumerable<T> Query(Viewport viewport);

    /// <summary>
    /// Clears all elements from the spatial index.
    /// </summary>
    void Clear();
}
