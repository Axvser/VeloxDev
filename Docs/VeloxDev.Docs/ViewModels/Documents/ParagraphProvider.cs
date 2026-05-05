using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class ParagraphProvider : IWikiElement
{
    [VeloxProperty] private IWikiElement? parent = null;
    [VeloxProperty] public partial string Text { get; set; }

    public ParagraphProvider()
    {
        Text = string.Empty;
    }
}
