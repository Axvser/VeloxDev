namespace VeloxDev.Docs;

public partial class ParagraphView : WikiElementViewBase
{
    public ParagraphView()
    {
        InitializeComponent();
        InitializeEditChrome(ChromeBorder, DisplayPanel, EditPanel);
    }
}
