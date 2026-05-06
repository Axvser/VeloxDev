using VeloxDev.Docs.Translation;
using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class MarkdownProvider : ITranslatableElement
{
    [VeloxProperty] private IWikiElement? parent = null;
    [TranslateTarget("Markdown document body")]
    [VeloxProperty] public partial string Text { get; set; }
    [VeloxProperty] public partial bool AutoHeight { get; set; }
    [VeloxProperty] public partial double MaxHeightValue { get; set; }
    [VeloxProperty] public partial bool EmbeddedCodeAutoHeight { get; set; }
    [VeloxProperty] public partial double EmbeddedCodeMaxHeightValue { get; set; }

    public MarkdownProvider()
    {
        Text = string.Empty;
        AutoHeight = false;
        MaxHeightValue = 420;
        EmbeddedCodeAutoHeight = false;
        EmbeddedCodeMaxHeightValue = 300;
    }
}
