using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VisualBorder = VeloxDev.WinForms.Xaml.Visuals.Controls.Border;
using VisualScrollViewer = VeloxDev.WinForms.Xaml.Visuals.Controls.ScrollViewer;

namespace VisualWorkbench;

/// <summary>The single window that hosts the visual tree on the secondary screen when one exists.</summary>
public sealed class WorkbenchForm : Form
{
    private readonly VisualSurface _surface;
    private readonly Screen _targetScreen;

    /// <summary>Creates the window and builds the tree in code.</summary>
    public WorkbenchForm()
    {
        Text = "Velox Visual Workbench";
        Size = new Size(1280, 800);
        MinimumSize = new Size(960, 640);
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Current.WindowBackground;

        _targetScreen = Screen.AllScreens.FirstOrDefault(screen => !screen.Primary)
                        ?? Screen.PrimaryScreen
                        ?? Screen.AllScreens[0];

        _surface = new VisualSurface
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Current.WindowBackground,
        };
        Controls.Add(_surface);
        _surface.Root = WorkbenchContent.CreateRoot(ToggleTheme);
    }

    // Opening on the secondary screen should not pull the user away from what they are doing there.
    /// <inheritdoc />
    protected override bool ShowWithoutActivation => true;

    /// <summary>Scrolls the outer content viewport to <paramref name="offset"/>; used by capture mode.</summary>
    /// <param name="offset">The vertical pixel offset.</param>
    public void SetScrollOffset(float offset)
    {
        if (_surface.Root is VisualBorder { Child: VisualScrollViewer scroll })
        {
            scroll.VerticalOffset = offset;
        }
    }

    /// <summary>Opens the first combo box in the tree; used by capture mode to prove the overlay path.</summary>
    public void OpenFirstComboBox()
    {
        if (FindComboBox(_surface.Root) is { } combo)
        {
            combo.IsDropDownOpen = true;
        }
    }

    /// <summary>Drags the first workflow node; used by capture mode to prove the model gesture path.</summary>
    public void DragFirstGraphNode()
    {
        if (FindGraph(_surface.Root) is not { } graph || graph.Tree.Nodes.Count == 0)
        {
            return;
        }

        var bounds = graph.NodeBounds(graph.Tree.Nodes[0]);
        if (bounds.IsEmpty)
        {
            return;
        }

        var center = new Point(
            (int)(bounds.X + (bounds.Width / 2f)),
            (int)(bounds.Y + (bounds.Height / 2f)));
        var target = new Point(center.X + 46, center.Y + 26);
        _surface.ProcessPointerDown(center, MouseButtons.Left);
        _surface.ProcessPointerMove(target);
        _surface.ProcessPointerUp(target, MouseButtons.Left);
    }

    /// <summary>Hovers the first workflow link and presses Delete; used by capture mode.</summary>
    public void DeleteFirstGraphLink()
    {
        if (FindGraph(_surface.Root) is not { } graph)
        {
            return;
        }

        var point = graph.FirstLinkMidpoint();
        if (point.IsEmpty)
        {
            return;
        }

        _surface.ProcessPointerMove(point);
        graph.Focus();
        _surface.ProcessKeyDown(Keys.Delete);
    }

    /// <summary>Zooms the workflow graph; used by capture mode.</summary>
    public void ZoomGraph()
    {
        FindGraph(_surface.Root)?.ZoomDemo();
    }

    private static Views.WorkflowGraphSample? FindGraph(VisualElement? element)
    {
        if (element is null)
        {
            return null;
        }

        if (element is Views.WorkflowGraphSample graph)
        {
            return graph;
        }

        foreach (var child in element.Children)
        {
            if (child is VisualElement childElement && FindGraph(childElement) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static VeloxDev.WinForms.Xaml.Visuals.Controls.ComboBox? FindComboBox(VisualElement? element)
    {
        if (element is null)
        {
            return null;
        }

        if (element is VeloxDev.WinForms.Xaml.Visuals.Controls.ComboBox combo)
        {
            return combo;
        }

        foreach (var child in element.Children)
        {
            if (child is VisualElement childElement && FindComboBox(childElement) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void ToggleTheme()
    {
        Theme.Mode = Theme.Mode == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark;
        Application.SetColorMode(Theme.Mode == ThemeMode.Dark ? SystemColorMode.Dark : SystemColorMode.Classic);
        BackColor = Theme.Current.WindowBackground;
        _surface.BackColor = Theme.Current.WindowBackground;

        var previous = _surface.Root;
        _surface.Root = WorkbenchContent.CreateRoot(ToggleTheme);
        previous?.Dispose();
    }

    /// <inheritdoc />
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        var area = _targetScreen.WorkingArea;
        var x = area.X + Math.Max(0, (area.Width - Width) / 2);
        var y = area.Y + Math.Max(0, (area.Height - Height) / 2);
        Location = new Point(Math.Max(area.X, x), Math.Max(area.Y, y));
    }
}
