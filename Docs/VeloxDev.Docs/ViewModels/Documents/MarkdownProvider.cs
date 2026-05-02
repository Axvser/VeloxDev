using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class MarkdownProvider : IWikiElement
{
    [VeloxProperty] public partial IWikiElement? Parent { get; set; }
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
