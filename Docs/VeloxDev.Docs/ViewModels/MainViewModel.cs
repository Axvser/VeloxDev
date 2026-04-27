using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class MainViewModel
{
    [VeloxProperty] private DocumentProvider document = new();

    public MainViewModel()
    {
        Document = new DocumentProvider();
        Document.AddRootCommand.Execute(null);
        Document.AddTitleCommand.Execute("1");
        Document.AddParagraphCommand.Execute(null);
        Document.AddLinkCommand.Execute(null);
        Document.AddCodeCommand.Execute(null);
    }
}
