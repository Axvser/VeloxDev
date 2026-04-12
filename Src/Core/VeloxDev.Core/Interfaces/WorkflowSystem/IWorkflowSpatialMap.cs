namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Defines methods for managing and querying workflow nodes within a spatial index.
/// </summary>
/// 
/// <remarks>
/// Implementations of this interface enable efficient insertion, removal, and spatial querying of
/// workflow nodes. It is intended for scenarios where rapid access to nodes based on their spatial location is
/// required, such as in graphical workflow editors or visualization tools.
/// </remarks>
public interface IWorkflowSpatialMap
{
    public void Insert(IWorkflowNodeViewModel node);
    public void Remove(IWorkflowNodeViewModel node);

    /// <summary>
    /// Query the nodes within the visible range
    /// </summary>
    /// <param name="viewport">visible range</param>
    /// <returns><seealso cref="IEnumerable{T}"/> T is <seealso cref="IWorkflowNodeViewModel"/></returns>
    public IEnumerable<IWorkflowNodeViewModel> Query(Viewport viewport);

    public void Clear();
}
