using VeloxDev.Docs.Translation;
using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class ParagraphProvider : ITranslatableElement
{
    [VeloxProperty] private IWikiElement? parent = null;
    [TranslateTarget("paragraph body")]
    [VeloxProperty] public partial string Text { get; set; }

    public ParagraphProvider()
    {
        Text = string.Empty;
    }
}
