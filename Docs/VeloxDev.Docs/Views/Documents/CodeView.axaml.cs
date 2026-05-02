namespace VeloxDev.Docs;

public partial class CodeView : WikiElementViewBase
{
    public CodeView()
    {
        InitializeComponent();
        InitializeEditChrome(ChromeBorder, DisplayPanel, EditPanel);
        PreferOwnScrolling(DisplayScrollViewer);
        PreferOwnScrolling(EditScrollViewer);
    }
}