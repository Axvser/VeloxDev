namespace VeloxDev.Docs;

public partial class SubtitleView : WikiElementViewBase
{
    public SubtitleView()
    {
        InitializeComponent();
        InitializeEditChrome(ChromeBorder, DisplayPanel, EditPanel);
    }
}
