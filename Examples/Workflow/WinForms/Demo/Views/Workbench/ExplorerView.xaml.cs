using System.Drawing;
using Demo.ViewModels;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workbench;

/// <summary>
/// The sidebar's explorer page: the file/undo actions, the minimap switch and the element lists. The host owns
/// the workflow session; this view only raises intent.
/// </summary>
public sealed partial class ExplorerView : WorkbenchViewBase
{
    /// <summary>Creates the page from XAML.</summary>
    public ExplorerView()
    {
        InitializeComponent();
        Elements.SelectedElementChanged += (_, entry) => SelectedElementChanged?.Invoke(this, entry);
    }

    /// <summary>Raised for the save action.</summary>
    public event EventHandler? SaveRequested;

    /// <summary>Raised for the load action.</summary>
    public event EventHandler? LoadRequested;

    /// <summary>Raised for the undo action.</summary>
    public event EventHandler? UndoRequested;

    /// <summary>Raised for the redo action.</summary>
    public event EventHandler? RedoRequested;

    /// <summary>Raised for the reload action.</summary>
    public event EventHandler? ReloadRequested;

    /// <summary>Raised with the requested minimap visibility.</summary>
    public event EventHandler<bool>? MinimapVisibilityRequested;

    /// <summary>Raised when an element row is picked.</summary>
    public event EventHandler<WorkflowElementEntry?>? SelectedElementChanged;

    /// <summary>Binds the element lists to a tree.</summary>
    public void SetTree(ITreeComponent? tree) => Elements.SetTree(tree);

    /// <summary>Highlights the tree's selected element.</summary>
    public void SetSelectedElement(WorkflowElementEntry? entry) => Elements.SetSelectedElement(entry);

    /// <summary>Mirrors the surface's minimap state into the switch.</summary>
    public void SetMinimapState(bool visible)
    {
        MinimapToggle.Text = visible ? "开" : "关";
        MinimapToggle.Foreground = visible ? WorkbenchPalette.Success : WorkbenchPalette.TextDisabled;
    }

    private void OnSave(object? sender, EventArgs e) => SaveRequested?.Invoke(this, e);

    private void OnLoad(object? sender, EventArgs e) => LoadRequested?.Invoke(this, e);

    private void OnUndo(object? sender, EventArgs e) => UndoRequested?.Invoke(this, e);

    private void OnRedo(object? sender, EventArgs e) => RedoRequested?.Invoke(this, e);

    private void OnReload(object? sender, EventArgs e) => ReloadRequested?.Invoke(this, e);

    private void OnMinimapToggle(object? sender, EventArgs e)
    {
        var visible = MinimapToggle.Text != "开";
        SetMinimapState(visible);
        MinimapVisibilityRequested?.Invoke(this, visible);
    }
}
