namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Selects the anchor point that workspace zoom collapses content toward.
/// Controls which world-space point stays visually fixed while <see cref="CanvasLayout.Scale"/> changes.
/// </summary>
public enum ZoomCenter
{
    /// <summary>
    /// Collapse toward the world origin (0,0): node positions and sizes divide by Scale about the origin.
    /// This is the historical, mathematically strict mode — the graph clusters tighter around (0,0) as you
    /// zoom in. Nodes far from the origin move on screen, but the canvas extent and every coordinate transform
    /// stay fixed (a viewport far from the origin can quickly lose its visible nodes).
    /// </summary>
    WorldOrigin = 0,

    /// <summary>
    /// Collapse toward the center of the currently visible viewport: node positions and sizes scale about
    /// the world point at the viewport center, so the content the user is looking at stays put while zooming.
    /// Visible nodes near the viewport center remain near it instead of flying off-screen.
    /// </summary>
    ViewportCenter = 1,
}
