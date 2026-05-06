using VeloxDev.Docs.Translation;
using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class TitleProvider : ITranslatableElement
{
    [VeloxProperty] private IWikiElement? parent = null;
    [VeloxProperty] public partial string Level { get; set; }
    [TranslateTarget("heading text")]
    [VeloxProperty] public partial string Text { get; set; }

    public TitleProvider()
    {
        Level = "1";
        Text = string.Empty;
    }
}
