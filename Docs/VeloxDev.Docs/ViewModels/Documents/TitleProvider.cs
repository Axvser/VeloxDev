using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class TitleProvider : IWikiElement
{
    [VeloxProperty] public partial IWikiElement? Parent { get; set; }
    [VeloxProperty] public partial string Level { get; set; }
    [VeloxProperty] public partial string Text { get; set; }

    public TitleProvider()
    {
        Level = "1";
        Text = string.Empty;
    }
}
