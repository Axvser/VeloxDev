namespace VeloxDev.Docs;

public partial class LinkView : WikiElementViewBase
{
    public LinkView()
    {
        InitializeComponent();
        InitializeEditChrome(ChromeBorder, DisplayPanel, EditPanel);
    }
}
