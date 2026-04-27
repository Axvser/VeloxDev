using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class SubtitleProvider : IWikiElement
{
    [VeloxProperty] public partial IWikiElement? Parent { get; set; }
    [VeloxProperty] public partial string Text { get; set; }

    public SubtitleProvider()
    {
        Text = string.Empty;
    }
}
