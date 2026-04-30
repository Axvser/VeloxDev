using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class MarkdownProvider : IWikiElement
{
    [VeloxProperty] public partial IWikiElement? Parent { get; set; }
    [VeloxProperty] public partial string Text { get; set; }

    public MarkdownProvider()
    {
        Text = string.Empty;
    }
}
