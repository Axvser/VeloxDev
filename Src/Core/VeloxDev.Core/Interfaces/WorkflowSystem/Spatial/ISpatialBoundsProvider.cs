using System.ComponentModel;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Defines a contract for objects that can provide spatial bounds for spatial indexing.
/// Implementers must notify when their bounds change via <see cref="INotifyPropertyChanged"/>.
/// </summary>
/// <remarks>
/// This interface enables any class to participate in spatial indexing mechanisms
/// such as <see cref="SpatialGridHashMap{T}"/>, allowing efficient viewport-based queries
/// for nodes, links, or any other spatial elements.
/// </remarks>
public interface ISpatialBoundsProvider : INotifyPropertyChanged
{
    /// <summary>
    /// Gets the current bounds of the object in viewport coordinates.
    /// </summary>
    /// <remarks>
    /// Implementers should raise <see cref="INotifyPropertyChanged.PropertyChanged"/>
    /// with the property name "Bounds" whenever the bounds change.
    /// </remarks>
    Viewport Bounds { get; }
}
