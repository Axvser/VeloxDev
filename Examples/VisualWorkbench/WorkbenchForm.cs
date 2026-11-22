using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;

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
