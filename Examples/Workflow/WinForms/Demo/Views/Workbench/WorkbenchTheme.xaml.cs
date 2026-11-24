using Easel;

namespace Demo.Views.Workbench;

/// <summary>The workbench's own palette and text styles, merged by every shell view before its XAML runs.</summary>
public sealed partial class WorkbenchTheme : ResourceDictionary
{
    /// <summary>Creates the dictionary and runs the generated initializer.</summary>
    public WorkbenchTheme()
    {
        InitializeComponent();
    }
}
