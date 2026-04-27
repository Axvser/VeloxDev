using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class ParagraphProvider : IWikiElement
{
    [VeloxProperty] public partial IWikiElement? Parent { get; set; }
    [VeloxProperty] public partial string Text { get; set; }

    public ParagraphProvider()
    {
        Text = string.Empty;
    }
}
