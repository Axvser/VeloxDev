using System.Drawing;
using Easel.Visuals.Controls;

namespace Demo.Views.Workbench;

/// <summary>
/// The 24px status bar: run state, compiled segment count and element totals on the left, realized count,
/// zoom and the gesture hint on the right. The owner pushes text into the labels; nothing here reads the model.
/// </summary>
public sealed partial class StatusBarView : WorkbenchViewBase
{
    /// <summary>Creates the status bar from XAML.</summary>
    public StatusBarView()
    {
        InitializeComponent();
    }

    /// <summary>"空闲" / "运行中".</summary>
    public TextBlock RunStateLabel => RunState;

    /// <summary>"编译 N 段".</summary>
    public TextBlock CompiledLabel => Compiled;

    /// <summary>"元素 N".</summary>
    public TextBlock ElementsLabel => Elements;

    /// <summary>"可见 N".</summary>
    public TextBlock VisibleLabel => VisibleCount;

    /// <summary>"100%".</summary>
    public TextBlock ZoomLabel => Zoom;

    /// <summary>"Ctrl+滚轮缩放" plus the latest status line.</summary>
    public TextBlock HintLabel => Hint;

    /// <summary>Paints the same state the status text reports, so the dot and the words cannot disagree.</summary>
    public void SetState(bool running)
    {
        RunDot.Foreground = running ? WorkbenchPalette.Warning : WorkbenchPalette.Success;
        RunState.Text = running ? "运行中" : "空闲";
        RunState.Foreground = running ? WorkbenchPalette.Warning : WorkbenchPalette.StatusBarText;
    }
}
