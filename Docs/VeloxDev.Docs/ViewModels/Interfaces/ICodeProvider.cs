using Avalonia.Controls.Documents;
using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public interface ICodeProvider : IWikiElement
{
    public string Code { get; set; }
    public string Language { get; set; }
    public InlineCollection Inlines { get; }

    public IVeloxCommand CopyCommand { get; }
}
