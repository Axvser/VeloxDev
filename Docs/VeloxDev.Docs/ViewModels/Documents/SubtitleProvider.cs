using VeloxDev.Docs.Translation;
using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class SubtitleProvider : ITranslatableElement
{
    [VeloxProperty] private IWikiElement? parent = null;
    [TranslateTarget("subtitle / block-quote text")]
    [VeloxProperty] public partial string Text { get; set; }

    public SubtitleProvider()
    {
        Text = string.Empty;
    }
}
