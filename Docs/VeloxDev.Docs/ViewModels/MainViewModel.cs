using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class MainViewModel
{
    [VeloxProperty] private DocumentProvider document = new();

    public MainViewModel()
    {
        Document = DocumentProvider.LoadDefault().GetAwaiter().GetResult();
    }
}
