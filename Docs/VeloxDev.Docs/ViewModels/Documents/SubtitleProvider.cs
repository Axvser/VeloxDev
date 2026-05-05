using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class SubtitleProvider : IWikiElement
{
    [VeloxProperty] private IWikiElement? parent = null;
    [VeloxProperty] public partial string Text { get; set; }

    public SubtitleProvider()
    {
        Text = string.Empty;
    }
}
