using UserControl = Easel.Visuals.Controls.UserControl;

namespace Demo.Views.Workbench;

/// <summary>
/// Base for every shell view. It merges <see cref="WorkbenchTheme"/> before the generated XAML runs, so
/// <c>{StaticResource Wb…}</c> resolves during construction instead of waiting for the view to join the tree.
/// </summary>
public class WorkbenchViewBase : UserControl
{
    /// <summary>Merges the workbench dictionary into this view's resources.</summary>
    protected WorkbenchViewBase()
    {
        Resources.MergedDictionaries.Add(new WorkbenchTheme());
    }
}
