using Avalonia.Media;
using VeloxDev.Docs.ViewModels;

namespace VeloxDev.Docs;

public partial class TitleView : WikiElementViewBase
{
    public TitleView()
    {
        InitializeComponent();
        InitializeEditChrome(ChromeBorder, DisplayPanel, EditPanel);
        DataContextChanged += (_, _) => ApplyLevelStyle();
    }

    protected override void ExitEdit()
    {
        base.ExitEdit();
        ApplyLevelStyle();
    }

    private void ApplyLevelStyle()
    {
        if (DataContext is not TitleProvider vm) return;
        (DisplayPanel.FontSize, DisplayPanel.FontWeight) = vm.Level switch
        {
            "1" => (30.0, FontWeight.Bold),
            "2" => (24.0, FontWeight.SemiBold),
            "3" => (20.0, FontWeight.SemiBold),
            "4" => (16.0, FontWeight.Medium),
            _ => (24.0, FontWeight.SemiBold)
        };
    }
}
